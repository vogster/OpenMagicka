using System.Buffers.Binary;
using System.Text;

namespace OpenMagicka.Content.Audio;

/// <summary>A wave to write into a bank: its entry's flags, length in samples, format, data, loop and name.</summary>
public sealed record WaveBankWave(int Flags, int Duration, MiniWaveFormat Format, byte[] Data, int LoopStart, int LoopLength, string? Name);

/// <summary>
/// Writes an in-memory wave bank as <see cref="WaveBankFile"/> reads it (content version 45, header version 43):
/// the bank's data, full 24-byte entries in their order (sound banks name waves by index), no seek tables (no WMA),
/// the names when the waves have them, and the waves, each at the alignment.
/// </summary>
public static class WaveBankWriter
{
    public const int Version = 45, HeaderVersion = 43, EntrySize = 24, NameSize = 64;

    public static byte[] Write(string name, IReadOnlyList<WaveBankWave> waves, int alignment = 4, ulong buildTime = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(waves);
        if (Encoding.ASCII.GetByteCount(name) >= 64 || alignment < 1 || waves.Any(w => w.Format.Tag == WaveFormat.Wma))
        {
            throw new ArgumentException("a bank name of 63 characters at most, an alignment of 1 or more, and no WMA");
        }
        var named = waves.Any(w => w.Name is not null);
        var bank = 12 + 5 * 8;
        var entries = bank + 96;
        var seek = entries + waves.Count * EntrySize;
        var names = seek;
        var data = Align(names + (named ? waves.Count * NameSize : 0), alignment);
        var offsets = new int[waves.Count];
        var length = 0;
        for (var i = 0; i < waves.Count; i++)
        {
            offsets[i] = length;
            length = checked(Align(length + waves[i].Data.Length, alignment));
        }
        var file = new byte[checked(data + length)];
        "WBND"u8.CopyTo(file);
        Put(file, 4, Version);
        Put(file, 8, HeaderVersion);
        (int Offset, int Length)[] segments =
            [(bank, 96), (entries, waves.Count * EntrySize), (seek, 0), (names, named ? waves.Count * NameSize : 0), (data, length)];
        for (var i = 0; i < segments.Length; i++)
        {
            Put(file, 12 + i * 8, segments[i].Offset);
            Put(file, 16 + i * 8, segments[i].Length);
        }
        Put(file, bank, named ? (int)WaveBankFile.NamesFlag : 0);
        Put(file, bank + 4, waves.Count);
        Encoding.ASCII.GetBytes(name).CopyTo(file, bank + 8);
        Put(file, bank + 72, EntrySize);
        Put(file, bank + 76, NameSize);
        Put(file, bank + 80, alignment);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(bank + 88, 8), buildTime);
        for (var i = 0; i < waves.Count; i++)
        {
            var (wave, at) = (waves[i], entries + i * EntrySize);
            if (wave.Duration is < 0 or >= 1 << 28 || wave.Flags is < 0 or > 0xF)
            {
                throw new ArgumentException($"wave {i}: a length or flags the entry cannot hold");
            }
            Put(file, at, wave.Flags | wave.Duration << 4);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4, 4), wave.Format.Packed);
            Put(file, at + 8, offsets[i]);
            Put(file, at + 12, wave.Data.Length);
            Put(file, at + 16, wave.LoopStart);
            Put(file, at + 20, wave.LoopLength);
            wave.Data.CopyTo(file, data + offsets[i]);
            if (wave.Name is { } waveName)
            {
                var bytes = Encoding.ASCII.GetBytes(waveName);
                bytes.AsSpan(0, Math.Min(bytes.Length, NameSize - 1)).CopyTo(file.AsSpan(names + i * NameSize));
            }
        }
        return file;
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static void Put(byte[] file, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at, 4), value);
}
