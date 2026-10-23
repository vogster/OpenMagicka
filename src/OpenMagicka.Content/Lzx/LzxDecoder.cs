using System.Buffers.Binary;

namespace OpenMagicka.Content.Lzx;

/// <summary>
/// LZX decompressor as used by XNA content (no E8 call translation). One instance decodes one
/// stream: call <see cref="Decompress"/> once per frame, in order. The sliding window, the
/// Huffman code lengths and the repeated offsets carry over between frames; a match may run
/// past the end of a frame and is then continued at the start of the next one. After an
/// <see cref="InvalidDataException"/> the stream state is undefined and the decoder refuses
/// further frames.
/// </summary>
public sealed class LzxDecoder
{
    private const int MinWindowBits = 15;
    // Window sizes of 2^20 and 2^21 use more position slots than 2 * windowBits; XNA uses 2^16.
    private const int MaxWindowBits = 19;
    private const int LengthSymbols = 249;
    private const int PretreeSymbols = 20;
    private const int AlignedSymbols = 8;
    private const int MinMatch = 2;

    private static readonly int[] ExtraBits = BuildExtraBits();
    private static readonly int[] PositionBase = BuildPositionBase();

    private readonly byte[] window;
    private readonly byte[] mainLengths;
    private readonly byte[] lengthLengths = new byte[LengthSymbols];
    private int windowPosition;
    private bool headerRead;
    private LzxBlockType blockType;
    private int blockRemaining;
    private int carry;
    private bool faulted;
    private uint r0 = 1, r1 = 1, r2 = 1;
    private HuffmanTable? mainTree, lengthTree, alignedTree;

    public LzxDecoder(int windowBits = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowBits, MinWindowBits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(windowBits, MaxWindowBits);
        window = new byte[1 << windowBits];
        mainLengths = new byte[256 + windowBits * 2 * 8];
    }

    /// <summary>Decodes one frame of compressed data into exactly <c>output.Length</c> bytes.</summary>
    public void Decompress(ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        if (output.Length > window.Length / 2)
        {
            throw new ArgumentException("frame larger than half the window", nameof(output));
        }
        if (faulted)
        {
            throw new InvalidOperationException("the LZX stream failed earlier and cannot continue");
        }
        try
        {
            DecompressFrame(compressed, output);
        }
        catch (InvalidDataException)
        {
            faulted = true;
            throw;
        }
    }

    private void DecompressFrame(ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        var reader = new LzxBitReader(compressed);
        if (!headerRead)
        {
            if (reader.Read(1) != 0)
            {
                throw new InvalidDataException("LZX E8 call translation is not supported");
            }
            headerRead = true;
        }

        var produced = carry;
        while (produced < output.Length)
        {
            if (blockRemaining == 0)
            {
                ReadBlockHeader(ref reader);
            }
            var run = Math.Min(blockRemaining, output.Length - produced);
            var decoded = blockType == LzxBlockType.Uncompressed
                ? CopyRaw(ref reader, run)
                : DecodeSymbols(ref reader, run);
            if (decoded > blockRemaining)
            {
                throw new InvalidDataException("LZX match runs past the end of its block");
            }
            blockRemaining -= decoded;
            produced += decoded;
        }

        carry = produced - output.Length;
        var start = windowPosition - produced;
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = window[(start + i) & (window.Length - 1)];
        }
    }

    private void ReadBlockHeader(ref LzxBitReader reader)
    {
        blockType = (LzxBlockType)reader.Read(3);
        blockRemaining = (int)((reader.Read(16) << 8) | reader.Read(8));
        switch (blockType)
        {
            case LzxBlockType.Aligned:
                Span<byte> aligned = stackalloc byte[AlignedSymbols];
                for (var i = 0; i < AlignedSymbols; i++)
                {
                    aligned[i] = (byte)reader.Read(3);
                }
                alignedTree = HuffmanTable.Build(aligned);
                ReadTrees(ref reader);
                break;
            case LzxBlockType.Verbatim:
                ReadTrees(ref reader);
                break;
            case LzxBlockType.Uncompressed:
                reader.AlignForRawData();
                var repeats = reader.ReadRaw(12);
                r0 = BinaryPrimitives.ReadUInt32LittleEndian(repeats);
                r1 = BinaryPrimitives.ReadUInt32LittleEndian(repeats[4..]);
                r2 = BinaryPrimitives.ReadUInt32LittleEndian(repeats[8..]);
                break;
            default:
                throw new InvalidDataException($"unknown LZX block type {(int)blockType}");
        }
        if (blockRemaining == 0)
        {
            throw new InvalidDataException("empty LZX block");
        }
    }

    private void ReadTrees(ref LzxBitReader reader)
    {
        ReadLengths(ref reader, mainLengths.AsSpan(0, 256));
        ReadLengths(ref reader, mainLengths.AsSpan(256));
        mainTree = HuffmanTable.Build(mainLengths);
        ReadLengths(ref reader, lengthLengths);
        lengthTree = HuffmanTable.Build(lengthLengths);
    }

    /// <summary>Code lengths are coded as deltas to the previous block's lengths through a pretree.</summary>
    private static void ReadLengths(ref LzxBitReader reader, Span<byte> lengths)
    {
        Span<byte> pretreeLengths = stackalloc byte[PretreeSymbols];
        for (var i = 0; i < PretreeSymbols; i++)
        {
            pretreeLengths[i] = (byte)reader.Read(4);
        }
        var pretree = HuffmanTable.Build(pretreeLengths);

        var x = 0;
        while (x < lengths.Length)
        {
            var code = pretree.Decode(ref reader);
            switch (code)
            {
                case 17:
                    x = Fill(lengths, x, (int)reader.Read(4) + 4, 0);
                    break;
                case 18:
                    x = Fill(lengths, x, (int)reader.Read(5) + 20, 0);
                    break;
                case 19:
                    var count = (int)reader.Read(1) + 4;
                    var delta = pretree.Decode(ref reader);
                    if (delta > 16)
                    {
                        throw new InvalidDataException("LZX code length run with a run code as its value");
                    }
                    x = Fill(lengths, x, count, Delta(lengths[x], delta));
                    break;
                default:
                    lengths[x] = Delta(lengths[x], code);
                    x++;
                    break;
            }
        }
    }

    private static byte Delta(byte previous, int code) => (byte)((previous - code + 17) % 17);

    private static int Fill(Span<byte> lengths, int start, int count, byte value)
    {
        if (start + count > lengths.Length)
        {
            throw new InvalidDataException("LZX code length run past the end of its tree");
        }
        lengths.Slice(start, count).Fill(value);
        return start + count;
    }

    private int CopyRaw(ref LzxBitReader reader, int count)
    {
        foreach (var b in reader.ReadRaw(count))
        {
            Put(b);
        }
        if (blockRemaining == count)
        {
            reader.SkipRawPadding(count);
        }
        return count;
    }

    private int DecodeSymbols(ref LzxBitReader reader, int run)
    {
        var main = mainTree ?? throw new InvalidDataException("LZX block without trees");
        var decoded = 0;
        while (decoded < run)
        {
            var symbol = main.Decode(ref reader);
            if (symbol < 256)
            {
                Put((byte)symbol);
                decoded++;
                continue;
            }
            symbol -= 256;
            var length = symbol & 7;
            if (length == 7)
            {
                length += lengthTree!.Decode(ref reader);
            }
            length += MinMatch;
            var offset = MatchOffset(ref reader, symbol >> 3);
            CopyMatch(offset, length);
            decoded += length;
        }
        return decoded;
    }

    private uint MatchOffset(ref LzxBitReader reader, int slot)
    {
        switch (slot)
        {
            case 0:
                return r0;
            case 1:
                (r0, r1) = (r1, r0);
                return r0;
            case 2:
                (r0, r2) = (r2, r0);
                return r0;
        }

        var extra = ExtraBits[slot];
        uint offset;
        if (blockType == LzxBlockType.Aligned && extra >= 3)
        {
            var verbatim = extra > 3 ? reader.Read(extra - 3) << 3 : 0;
            offset = (uint)PositionBase[slot] - 2 + verbatim + (uint)alignedTree!.Decode(ref reader);
        }
        else
        {
            offset = (uint)PositionBase[slot] - 2 + reader.Read(extra);
        }
        (r2, r1, r0) = (r1, r0, offset);
        return offset;
    }

    private void CopyMatch(uint offset, int length)
    {
        if (offset == 0 || offset > window.Length || offset > windowPosition)
        {
            throw new InvalidDataException($"LZX match offset {offset} outside the decoded data");
        }
        var mask = window.Length - 1;
        var source = windowPosition - (int)offset;
        for (var i = 0; i < length; i++)
        {
            Put(window[(source + i) & mask]);
        }
    }

    private void Put(byte value)
    {
        window[windowPosition & (window.Length - 1)] = value;
        windowPosition++;
    }

    private static int[] BuildExtraBits()
    {
        var bits = new int[52];
        var j = 0;
        for (var i = 0; i < 52; i += 2)
        {
            bits[i] = bits[i + 1] = j;
            if (i != 0 && j < 17)
            {
                j++;
            }
        }
        return bits;
    }

    private static int[] BuildPositionBase()
    {
        var bases = new int[52];
        for (var i = 1; i < 52; i++)
        {
            bases[i] = bases[i - 1] + (1 << ExtraBits[i - 1]);
        }
        return bases;
    }
}
