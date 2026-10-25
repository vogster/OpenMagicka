using System.Buffers.Binary;
using System.Text;

namespace OpenMagicka.Content.Audio;

/// <summary>A wave in a bank: its flags and length in samples, its format, where its data is (in the bank's wave segment), its loop and seek table.</summary>
public sealed record WaveBankEntry(int Flags, int Duration, MiniWaveFormat Format, int Offset, int Length, int LoopStart, int LoopLength,
    IReadOnlyList<uint>? SeekTable, string? Name);

/// <summary>
/// A whole in-memory wave bank (XACT content version 45, docs/engine/ARCHITECTURE.md §8.2): its name, flags and
/// alignment, every entry with its seek table and name, and its waves. The layout: "WBND", the version (45) and the
/// header's (43), five segments (offset, length): the bank's data, the entries (24 bytes each, or 4 when compact),
/// the seek tables (an offset per entry, −1 for none, then each table: a count and the decoded byte counts), the
/// names (64 bytes each) and the waves.
/// </summary>
public sealed class WaveBankFile
{
    public const uint StreamingFlag = 0x1, NamesFlag = 0x10000, CompactFlag = 0x20000, SeekTablesFlag = 0x80000;

    private readonly byte[] data;
    private readonly int waves;

    private WaveBankFile(byte[] data, int waves, string name, uint flags, int alignment, ulong buildTime, IReadOnlyList<WaveBankEntry> entries) =>
        (this.data, this.waves, Name, Flags, Alignment, BuildTime, Entries) = (data, waves, name, flags, alignment, buildTime, entries);

    public string Name { get; }

    public uint Flags { get; }

    public int Alignment { get; }

    public ulong BuildTime { get; }

    public IReadOnlyList<WaveBankEntry> Entries { get; }

    public static WaveBankFile Read(string path) => Read(File.ReadAllBytes(path ?? throw new ArgumentNullException(nameof(path))));

    public static WaveBankFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 52 || !data.AsSpan(0, 4).SequenceEqual("WBND"u8))
        {
            throw new InvalidDataException("not a wave bank");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4)) is < 42 or > 46)
        {
            throw new InvalidDataException("a wave bank of another XACT version");
        }
        var segments = Enumerable.Range(0, 5).Select(i => (Offset: I32(data, 12 + i * 8), Length: I32(data, 16 + i * 8))).ToArray();
        foreach (var (offset, length) in segments)
        {
            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
            {
                throw new InvalidDataException("a wave bank segment out of the file");
            }
        }
        var bank = segments[0].Offset;
        if (segments[0].Length < 96)
        {
            throw new InvalidDataException("a short wave bank header");
        }
        var flags = U32(data, bank);
        var count = I32(data, bank + 4);
        var name = Encoding.ASCII.GetString(data, bank + 8, 64).Split('\0')[0];
        var entrySize = I32(data, bank + 72);
        var alignment = I32(data, bank + 80);
        if ((flags & CompactFlag) != 0)
        {
            throw new InvalidDataException("compact wave banks are not read");
        }
        if (count < 0 || entrySize < 24 || (long)count * entrySize > segments[1].Length)
        {
            throw new InvalidDataException("a bad wave bank entry table");
        }
        var entries = new WaveBankEntry[count];
        for (var i = 0; i < count; i++)
        {
            var at = segments[1].Offset + i * entrySize;
            var flagsAndDuration = U32(data, at);
            var (offset, length) = (I32(data, at + 8), I32(data, at + 12));
            if (offset < 0 || length < 0 || (long)offset + length > segments[4].Length)
            {
                throw new InvalidDataException($"wave {i} out of the bank");
            }
            entries[i] = new WaveBankEntry((int)(flagsAndDuration & 0xF), (int)(flagsAndDuration >> 4), MiniWaveFormat.From(U32(data, at + 4)), offset, length,
                I32(data, at + 16), I32(data, at + 20), SeekTable(data, flags, segments[2], i, count), Named(data, flags, segments[3], i));
        }
        return new WaveBankFile(data, segments[4].Offset, name, flags, alignment, BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(bank + 88, 8)), entries);
    }

    /// <summary>A wave's data as the bank has it.</summary>
    public ReadOnlySpan<byte> Wave(WaveBankEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return data.AsSpan(waves + entry.Offset, entry.Length);
    }

    private static IReadOnlyList<uint>? SeekTable(byte[] data, uint flags, (int Offset, int Length) segment, int index, int count)
    {
        if ((flags & SeekTablesFlag) == 0 || segment.Length == 0)
        {
            return null;
        }
        if ((long)count * 4 > segment.Length)
        {
            throw new InvalidDataException("a short seek table segment");
        }
        var offset = I32(data, segment.Offset + index * 4);
        if (offset == -1)
        {
            return null;
        }
        var at = segment.Offset + count * 4 + offset;
        var entries = at >= segment.Offset && at + 4 <= segment.Offset + segment.Length ? I32(data, at) : -1;
        if (entries < 0 || at + 4 + (long)entries * 4 > segment.Offset + segment.Length)
        {
            throw new InvalidDataException($"wave {index}: a bad seek table");
        }
        return [.. Enumerable.Range(0, entries).Select(j => U32(data, at + 4 + j * 4))];
    }

    private static string? Named(byte[] data, uint flags, (int Offset, int Length) segment, int index) =>
        (flags & NamesFlag) != 0 && segment.Offset + (index + 1) * 64 <= segment.Offset + segment.Length
            ? Encoding.ASCII.GetString(data, segment.Offset + index * 64, 64).Split('\0')[0]
            : null;

    private static int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at, 4));

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}
