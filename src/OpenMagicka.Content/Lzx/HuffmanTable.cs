namespace OpenMagicka.Content.Lzx;

/// <summary>
/// Canonical Huffman decoding table: codes are assigned in order of length, then symbol.
/// Decoding is a single lookup of the longest code length. Incomplete trees are allowed (the
/// format uses them), over-subscribed ones are rejected.
/// </summary>
internal sealed class HuffmanTable
{
    public const int MaxCodeLength = 16;

    private readonly ushort[] symbols;
    private readonly byte[] lengths;
    private readonly int tableBits;

    private HuffmanTable(ushort[] symbols, byte[] lengths, int tableBits)
    {
        this.symbols = symbols;
        this.lengths = lengths;
        this.tableBits = tableBits;
    }

    public static HuffmanTable Build(ReadOnlySpan<byte> codeLengths)
    {
        var longest = 0;
        foreach (var length in codeLengths)
        {
            if (length > MaxCodeLength)
            {
                throw new InvalidDataException($"Huffman code length {length} exceeds {MaxCodeLength}");
            }
            longest = Math.Max(longest, length);
        }

        var tableBits = Math.Max(longest, 1);
        var size = 1 << tableBits;
        var symbols = new ushort[size];
        var lengths = new byte[size];
        var code = 0;
        for (var length = 1; length <= longest; length++)
        {
            for (var symbol = 0; symbol < codeLengths.Length; symbol++)
            {
                if (codeLengths[symbol] != length)
                {
                    continue;
                }
                var first = code << (tableBits - length);
                var last = (code + 1) << (tableBits - length);
                if (last > size)
                {
                    throw new InvalidDataException("over-subscribed Huffman tree");
                }
                symbols.AsSpan(first, last - first).Fill((ushort)symbol);
                lengths.AsSpan(first, last - first).Fill((byte)length);
                code++;
            }
            code <<= 1;
        }
        return new HuffmanTable(symbols, lengths, tableBits);
    }

    public int Decode(ref LzxBitReader reader)
    {
        var index = (int)reader.Peek(tableBits);
        var length = lengths[index];
        if (length == 0)
        {
            throw new InvalidDataException("invalid Huffman code");
        }
        reader.Skip(length);
        return symbols[index];
    }
}
