using System.Buffers.Binary;

namespace OpenMagicka.Content.Lzx;

/// <summary>
/// LZX bit stream: 16-bit little-endian words read most significant bit first. Reading a little
/// past the end yields zero bits (the format pads its last word); reading far past it is an error.
/// </summary>
internal ref struct LzxBitReader
{
    private const int MaxOverrunBytes = 16;

    private readonly ReadOnlySpan<byte> data;
    private int position;
    private ulong buffer;
    private int bitCount;

    public LzxBitReader(ReadOnlySpan<byte> data)
    {
        this.data = data;
    }

    public uint Peek(int count)
    {
        while (bitCount < count)
        {
            FillWord();
        }
        return (uint)(buffer >> (bitCount - count)) & ((1u << count) - 1);
    }

    public void Skip(int count)
    {
        bitCount -= count;
        buffer &= (1UL << bitCount) - 1;
    }

    public uint Read(int count)
    {
        if (count == 0)
        {
            return 0;
        }
        var value = Peek(count);
        Skip(count);
        return value;
    }

    /// <summary>
    /// Moves to the next 16-bit boundary before the raw part of an uncompressed block. Like the
    /// reference decoder this reads 1 to 16 bits, so an already aligned stream skips a word.
    /// </summary>
    public void AlignForRawData()
    {
        if (bitCount < 16)
        {
            FillWord();
        }
        if (bitCount > 16)
        {
            position -= 2;
        }
        bitCount = 0;
        buffer = 0;
    }

    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        if (bitCount != 0)
        {
            throw new InvalidOperationException("raw data must start on a word boundary");
        }
        if (position + count > data.Length)
        {
            throw new InvalidDataException("LZX uncompressed block runs past the end of its frame");
        }
        var raw = data.Slice(position, count);
        position += count;
        return raw;
    }

    /// <summary>Skips the padding byte that keeps an odd-sized raw block word aligned.</summary>
    public void SkipRawPadding(int rawCount)
    {
        if ((rawCount & 1) != 0)
        {
            position++;
        }
    }

    private void FillWord()
    {
        uint word = 0;
        if (position + 1 < data.Length)
        {
            word = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        }
        else if (position - data.Length >= MaxOverrunBytes)
        {
            throw new InvalidDataException("LZX bit stream runs past the end of its frame");
        }
        position += 2;
        buffer = (buffer << 16) | word;
        bitCount += 16;
    }
}
