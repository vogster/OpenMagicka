using System.Buffers.Binary;
using System.Text;
using OpenMagicka.Content.Lzx;

namespace OpenMagicka.Content.Xnb;

/// <summary>
/// An XNA 3.1 content file (XNB format version 4): header, LZX-decompressed body, type reader
/// table and shared resource count. <see cref="Objects"/> holds the primary object followed by the
/// shared resources, to be read by the matching content readers.
/// </summary>
public sealed class XnbFile
{
    public const byte SupportedVersion = 4;

    private const int HeaderSize = 10;
    private const int CompressedHeaderSize = 14;
    private const byte CompressedFlag = 0x80;
    private const int DefaultFrameSize = 0x8000;
    private const int MaxDecompressedSize = 256 * 1024 * 1024;
    private const int MaxTypeReaders = 1024;

    private XnbFile(char platform, byte formatVersion, bool isCompressed,
        IReadOnlyList<XnbTypeReader> typeReaders, int sharedResourceCount, ReadOnlyMemory<byte> objects)
    {
        Platform = platform;
        FormatVersion = formatVersion;
        IsCompressed = isCompressed;
        TypeReaders = typeReaders;
        SharedResourceCount = sharedResourceCount;
        Objects = objects;
    }

    /// <summary>Target platform: <c>w</c> for Windows.</summary>
    public char Platform { get; }

    public byte FormatVersion { get; }

    public bool IsCompressed { get; }

    public IReadOnlyList<XnbTypeReader> TypeReaders { get; }

    public int SharedResourceCount { get; }

    /// <summary>Serialized primary object followed by the shared resources.</summary>
    public ReadOnlyMemory<byte> Objects { get; }

    public static XnbFile Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize || !file[..3].SequenceEqual("XNB"u8))
        {
            throw new InvalidDataException("not an XNB file");
        }
        var platform = (char)file[3];
        var version = file[4];
        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"XNB format version {version} is not supported; Magicka uses version {SupportedVersion} (XNA 3.1)");
        }
        var compressed = (file[5] & CompressedFlag) != 0;
        var fileSize = BinaryPrimitives.ReadInt32LittleEndian(file[6..]);
        if (fileSize < HeaderSize || fileSize > file.Length)
        {
            throw new InvalidDataException($"XNB size {fileSize} does not match the data ({file.Length} bytes)");
        }

        var body = compressed ? Decompress(file[..fileSize]) : file[HeaderSize..fileSize].ToArray();
        return Parse(platform, version, compressed, body);
    }

    private static byte[] Decompress(ReadOnlySpan<byte> file)
    {
        if (file.Length < CompressedHeaderSize)
        {
            throw new InvalidDataException("compressed XNB without a size field");
        }
        var size = BinaryPrimitives.ReadInt32LittleEndian(file[HeaderSize..]);
        // Every frame needs at least a 2-byte header and a 1-byte block.
        var maxFrames = (file.Length - CompressedHeaderSize) / 3;
        if (size is < 0 or > MaxDecompressedSize || size > (long)maxFrames * DefaultFrameSize)
        {
            throw new InvalidDataException($"implausible decompressed XNB size {size}");
        }

        var output = new byte[size];
        var decoder = new LzxDecoder();
        var position = CompressedHeaderSize;
        var written = 0;
        while (written < size)
        {
            var (frameSize, blockSize, headerLength) = ReadFrameHeader(file, position);
            position += headerLength;
            if (blockSize == 0 || position + blockSize > file.Length)
            {
                throw new InvalidDataException("XNB compressed frame runs past the end of the file");
            }
            var count = Math.Min(frameSize, size - written);
            decoder.Decompress(file.Slice(position, blockSize), output.AsSpan(written, count));
            position += blockSize;
            written += count;
        }
        return output;
    }

    /// <summary>A frame header is a big-endian block size, or 0xFF, frame size and block size.</summary>
    private static (int FrameSize, int BlockSize, int HeaderLength) ReadFrameHeader(ReadOnlySpan<byte> file, int position)
    {
        if (position + 2 > file.Length)
        {
            throw new InvalidDataException("XNB compressed data ends before its declared size");
        }
        if (file[position] != 0xFF)
        {
            return (DefaultFrameSize, (file[position] << 8) | file[position + 1], 2);
        }
        if (position + 5 > file.Length)
        {
            throw new InvalidDataException("truncated XNB frame header");
        }
        var frameSize = (file[position + 1] << 8) | file[position + 2];
        var blockSize = (file[position + 3] << 8) | file[position + 4];
        if (frameSize is 0 or > DefaultFrameSize)
        {
            throw new InvalidDataException($"XNB frame size {frameSize} outside 1..{DefaultFrameSize}");
        }
        return (frameSize, blockSize, 5);
    }

    private static XnbFile Parse(char platform, byte version, bool compressed, byte[] body)
    {
        using var stream = new MemoryStream(body, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        try
        {
            var count = reader.Read7BitEncodedInt();
            if (count is < 0 or > MaxTypeReaders)
            {
                throw new InvalidDataException($"implausible type reader count {count}");
            }
            var readers = new XnbTypeReader[count];
            for (var i = 0; i < count; i++)
            {
                readers[i] = new XnbTypeReader(reader.ReadString(), reader.ReadInt32());
            }
            var shared = reader.Read7BitEncodedInt();
            if (shared < 0)
            {
                throw new InvalidDataException($"negative shared resource count {shared}");
            }
            var start = (int)stream.Position;
            return new XnbFile(platform, version, compressed, readers, shared, body.AsMemory(start));
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            throw new InvalidDataException("XNB type reader table is truncated or malformed", e);
        }
    }
}
