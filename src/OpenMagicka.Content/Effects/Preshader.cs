using System.Buffers.Binary;

namespace OpenMagicka.Content.Effects;

/// <summary>
/// Preshaders: CPU-side bytecode in an FXLC block inside a shader's PRES comment. An FXLC block
/// is a u32 instruction count, then per instruction an opcode token (opcode in the high 16 bits,
/// element count in the low 8), a source operand count, and (count + 1) operands of u32 array
/// count, u32 kind, u32 index plus 2 u32 per array; the last operand is the destination.
/// </summary>
public static class Preshader
{
    private const uint Comment = 0xFFFE;
    private const uint Pres = 0x53455250;  // "PRES"
    private const uint Fxlc = 0x434C5846;  // "FXLC"
    private const uint TempKind = 7;
    private const uint Mov = 0x1000;
    private const int MaxCommentLength = 0x7FFF;

    /// <summary>Real preshaders use a handful of temps; larger indices are malformed data.</summary>
    private const uint MaxTempIndex = 4096;

    /// <summary>The FXLC payloads (without the "FXLC" tag) of every preshader in a shader.</summary>
    public static IReadOnlyList<byte[]> Find(byte[] shader)
    {
        var found = new List<byte[]>();
        foreach (var (inner, innerLength) in FxlcBlocks(shader))
        {
            found.Add(shader.AsSpan(inner + 8, 4 * innerLength - 4).ToArray());
        }
        return found;
    }

    public static int InstructionCount(ReadOnlySpan<byte> fxlc) => (int)U32(fxlc, 0);

    /// <summary>
    /// Temps MojoShader allocates (highest temp index an operand starts at, plus one, rounded up
    /// to 4) and temps the instructions write (destination index plus element count).
    /// </summary>
    public static (int Allocated, int Needed) TempUsage(ReadOnlySpan<byte> fxlc)
    {
        var highestStart = -1;
        var needed = 0;
        Walk(fxlc, (elements, operands) =>
        {
            foreach (var (kind, index) in operands)
            {
                if (kind == TempKind)
                {
                    if (index > MaxTempIndex)
                    {
                        throw new InvalidDataException($"preshader temp index {index}");
                    }
                    highestStart = Math.Max(highestStart, (int)index);
                }
            }
            var (destKind, destIndex) = operands[^1];
            if (destKind == TempKind)
            {
                needed = Math.Max(needed, (int)destIndex + elements);
            }
        });
        return ((highestStart + 1 + 3) & ~3, needed);
    }

    /// <summary>
    /// Works around MojoShader under-allocating preshader temps (its DOT writes as many elements
    /// as its inputs have): appends MOV temp[need-1] &lt;- temp[need-1] to every overflowing
    /// preshader. The result is unchanged, MojoShader then allocates enough temps.
    /// </summary>
    public static (byte[] Shader, int Widened) Widen(byte[] shader)
    {
        var output = shader;
        var widened = 0;
        // Patch from the last block back so earlier offsets stay valid.
        foreach (var (inner, innerLength, pres) in Enumerable.Reverse(FxlcBlocksWithParent(shader)))
        {
            var fxlc = shader.AsSpan(inner + 8, 4 * innerLength - 4);
            var (allocated, needed) = TempUsage(fxlc);
            if (needed <= allocated)
            {
                continue;
            }
            var mov = new uint[] { (Mov << 16) | 1, 1, 0, TempKind, (uint)needed - 1, 0, TempKind, (uint)needed - 1 };
            var presLengthBefore = (int)(U32(output, pres) >> 16) & 0x7FFF;
            if (presLengthBefore + mov.Length > MaxCommentLength)
            {
                throw new InvalidDataException("preshader too large to widen");
            }
            var insertAt = inner + 8 + InstructionsEnd(fxlc);
            var patched = new byte[output.Length + mov.Length * 4];
            output.AsSpan(0, insertAt).CopyTo(patched);
            for (var i = 0; i < mov.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(insertAt + 4 * i), mov[i]);
            }
            output.AsSpan(insertAt).CopyTo(patched.AsSpan(insertAt + mov.Length * 4));
            BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(inner + 8), U32(fxlc, 0) + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(inner), ((uint)(innerLength + mov.Length) << 16) | Comment);
            var presLength = (int)(U32(patched, pres) >> 16) & 0x7FFF;
            BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(pres), ((uint)(presLength + mov.Length) << 16) | Comment);
            output = patched;
            widened++;
        }
        return (output, widened);
    }

    private static IEnumerable<(int Inner, int InnerLength)> FxlcBlocks(byte[] shader) =>
        FxlcBlocksWithParent(shader).Select(b => (b.Inner, b.InnerLength));

    /// <summary>Positions of FXLC comment tokens and of their enclosing PRES comment token.</summary>
    private static List<(int Inner, int InnerLength, int Pres)> FxlcBlocksWithParent(byte[] shader)
    {
        var blocks = new List<(int, int, int)>();
        var position = 4;                                   // after the version token
        while (position + 8 <= shader.Length && (U32(shader, position) & 0xFFFF) == Comment)
        {
            var length = CommentLength(shader, position);
            if (U32(shader, position + 4) == Pres)
            {
                var end = position + 4 + 4 * length;
                var inner = position + 12;                  // comment token, "PRES", preshader version
                while (inner + 8 <= end && (U32(shader, inner) & 0xFFFF) == Comment)
                {
                    var innerLength = CommentLength(shader, inner);
                    if (innerLength < 2 || inner + 4 + 4 * innerLength > end)
                    {
                        throw new InvalidDataException("malformed preshader block");
                    }
                    if (U32(shader, inner + 4) == Fxlc)
                    {
                        blocks.Add((inner, innerLength, position));
                    }
                    inner += 4 + 4 * innerLength;
                }
            }
            position += 4 + 4 * length;
        }
        return blocks;
    }

    private static int CommentLength(ReadOnlySpan<byte> shader, int position)
    {
        var length = (int)(U32(shader, position) >> 16) & 0x7FFF;
        if (position + 4 + 4L * length > shader.Length)
        {
            throw new InvalidDataException("shader comment runs past the end of the shader");
        }
        return length;
    }

    /// <summary>Byte offset just past the last instruction (the block may carry trailing data).</summary>
    private static int InstructionsEnd(ReadOnlySpan<byte> fxlc)
    {
        var end = 4;
        Walk(fxlc, (_, _) => { }, position => end = position);
        return end;
    }

    private static void Walk(ReadOnlySpan<byte> fxlc, Action<int, (uint Kind, uint Index)[]> visit, Action<int>? done = null)
    {
        var count = U32(fxlc, 0);
        var position = 4;
        for (var i = 0u; i < count; i++)
        {
            var opcode = U32(fxlc, position);
            var sources = U32(fxlc, position + 4);
            position += 8;
            if (sources > 16)
            {
                throw new InvalidDataException($"preshader instruction with {sources} sources");
            }
            var operands = new (uint, uint)[sources + 1];
            for (var o = 0; o < operands.Length; o++)
            {
                var arrays = U32(fxlc, position);
                operands[o] = (U32(fxlc, position + 4), U32(fxlc, position + 8));
                position += 12;
                if (arrays > (uint)(fxlc.Length - position) / 8)
                {
                    throw new InvalidDataException("preshader operand runs past the end of its block");
                }
                position += (int)arrays * 8;
            }
            visit((int)(opcode & 0xFF), operands);
        }
        done?.Invoke(position);
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && (long)offset + 4 <= data.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])
            : throw new InvalidDataException("preshader data truncated");
}
