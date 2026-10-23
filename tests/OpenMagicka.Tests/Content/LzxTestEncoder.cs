namespace OpenMagicka.Tests.Content;

/// <summary>
/// Minimal LZX encoder for tests: verbatim blocks with a uniform 9-bit main tree (every one of
/// the 512 main symbols of a 64 KB window gets length 9, so the code of a symbol is the symbol
/// itself) and uncompressed blocks. Enough to exercise every decoder path without game data.
/// </summary>
internal sealed class LzxTestEncoder
{
    private const int MainSymbols = 512;
    private const int LengthSymbols = 249;

    private readonly List<ushort> words = [];
    private uint pending;
    private int pendingBits;
    private bool headerWritten;

    /// <summary>Starts a new frame: XNB frames restart the bit stream.</summary>
    public byte[] TakeFrame()
    {
        if (pendingBits > 0)
        {
            Bits(0, 16 - pendingBits);
        }
        var bytes = new byte[words.Count * 2];
        for (var i = 0; i < words.Count; i++)
        {
            bytes[2 * i] = (byte)words[i];
            bytes[2 * i + 1] = (byte)(words[i] >> 8);
        }
        words.Clear();
        return bytes;
    }

    public LzxTestEncoder VerbatimBlock(int size)
    {
        Header(blockType: 1, size);
        // Main tree, symbols 0-255 then 256-511: every length goes 0 -> 9.
        TreeLengths(MainSymbols / 2, fromZeroTo: 9);
        TreeLengths(MainSymbols / 2, fromZeroTo: 9);
        // Length tree stays all zero: matches in tests are at most 8 bytes long.
        TreeLengths(LengthSymbols, fromZeroTo: 0);
        return this;
    }

    public LzxTestEncoder Literal(byte value) => Symbol(value);

    /// <summary>Match of 2..8 bytes using the most recent offset (R0).</summary>
    public LzxTestEncoder RepeatMatch(int length) => Symbol(256 + (0 << 3) + LengthHeader(length));

    /// <summary>Match of 2..8 bytes at offset 2 or 3 (position slot 4, one extra bit).</summary>
    public LzxTestEncoder OffsetMatch(int offset, int length)
    {
        if (offset is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        Symbol(256 + (4 << 3) + LengthHeader(length));
        return Bits((uint)(offset - 2), 1);
    }

    public LzxTestEncoder UncompressedBlock(ReadOnlySpan<byte> data, uint r0 = 1, uint r1 = 1, uint r2 = 1)
    {
        Header(blockType: 3, data.Length);
        // Align to 16 bits; when already aligned the format still skips one 16-bit word.
        Bits(0, pendingBits == 0 ? 16 : 16 - pendingBits);
        var raw = new List<byte>();
        foreach (var r in new[] { r0, r1, r2 })
        {
            raw.AddRange(BitConverter.GetBytes(r));
        }
        raw.AddRange(data.ToArray());
        if (raw.Count % 2 == 1)
        {
            raw.Add(0);
        }
        for (var i = 0; i < raw.Count; i += 2)
        {
            words.Add((ushort)(raw[i] | (raw[i + 1] << 8)));
        }
        return this;
    }

    public LzxTestEncoder RawHeader(int blockType, int size)
    {
        Header(blockType, size);
        return this;
    }

    private void Header(int blockType, int size)
    {
        if (!headerWritten)
        {
            Bits(0, 1); // no E8 call translation
            headerWritten = true;
        }
        Bits((uint)blockType, 3);
        Bits((uint)(size >> 8), 16);
        Bits((uint)(size & 0xFF), 8);
    }

    // Pretree with two symbols of length 1: 0 ("keep the length", code 0) and
    // 17 - target ("previous minus 17-target mod 17", code 1).
    private void TreeLengths(int count, int fromZeroTo)
    {
        var delta = (17 - fromZeroTo) % 17;
        for (var sym = 0; sym < 20; sym++)
        {
            Bits(sym == 0 || sym == delta ? 1u : 0u, 4);
        }
        for (var i = 0; i < count; i++)
        {
            Bits(delta == 0 ? 0u : 1u, 1);
        }
    }

    private static int LengthHeader(int length) => length is >= 2 and <= 8
        ? length - 2
        : throw new ArgumentOutOfRangeException(nameof(length));

    private LzxTestEncoder Symbol(int symbol) => Bits((uint)symbol, 9);

    private LzxTestEncoder Bits(uint value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            pending = (pending << 1) | ((value >> i) & 1);
            if (++pendingBits == 16)
            {
                words.Add((ushort)pending);
                pending = 0;
                pendingBits = 0;
            }
        }
        return this;
    }
}
