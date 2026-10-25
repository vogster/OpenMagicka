using System.Buffers.Binary;
using System.Text;

namespace OpenMagicka.Content.Audio;

/// <summary>How a wave bank entry is encoded (the low two bits of XACT's packed format).</summary>
public enum WaveFormat : byte { Pcm, Xma, Adpcm, Wma }

/// <summary>
/// What a wave bank (<c>.xwb</c>, XACT content version 45) says of itself (docs/engine/ARCHITECTURE.md §8): its
/// name, whether it streams, and each entry's encoding. FAudio cannot play WMA (RISK_CHECKS.md §2), so a bank with
/// any WMA entry is left out until it is transcoded (OM-61).
/// </summary>
/// <remarks>
/// The file starts "WBND", its version and its header's, then five segments (offset, length): the bank's data,
/// the entries' metadata, seek tables, names and waves. The bank's data is its flags (bit 0: streaming; bit 17:
/// compact entries), its entry count, its 64-byte name, the metadata entry size, the name size, the alignment and
/// the compact format. A full entry is its flags and duration, then its packed format.
/// </remarks>
public sealed record WaveBankInfo(string Name, bool IsStreaming, IReadOnlyList<WaveFormat> Entries)
{
    private const uint StreamingFlag = 0x1, CompactFlag = 0x20000;
    private const int HeaderSize = 12 + 5 * 8;

    public bool UsesWma => Entries.Contains(WaveFormat.Wma);

    public static WaveBankInfo Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var file = File.OpenRead(path);
        var header = new byte[HeaderSize];
        file.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual("WBND"u8))
        {
            throw new InvalidDataException($"{path} is not a wave bank");
        }
        var (bankAt, entriesAt) = (Segment(header, 0).Offset, Segment(header, 1).Offset);
        var bank = new byte[96];
        file.Position = bankAt;
        file.ReadExactly(bank);
        var flags = U32(bank, 0);
        var count = (int)U32(bank, 4);
        var name = Encoding.ASCII.GetString(bank, 8, 64).TrimEnd('\0');
        var entrySize = (int)U32(bank, 72);
        var compactFormat = U32(bank, 84);
        if (count < 0 || entrySize < ((flags & CompactFlag) != 0 ? 4 : 8)
            || ((flags & CompactFlag) == 0 && (long)count * entrySize > Segment(header, 1).Length))
        {
            throw new InvalidDataException($"{path}: a bad entry table");
        }
        var formats = new WaveFormat[count];
        if ((flags & CompactFlag) != 0)
        {
            Array.Fill(formats, (WaveFormat)(compactFormat & 3));
        }
        else
        {
            var entries = new byte[checked(count * entrySize)];
            file.Position = entriesAt;
            file.ReadExactly(entries);
            for (var i = 0; i < count; i++)
            {
                formats[i] = (WaveFormat)(U32(entries, i * entrySize + 4) & 3);
            }
        }
        return new WaveBankInfo(name, (flags & StreamingFlag) != 0, formats);
    }

    private static (uint Offset, uint Length) Segment(byte[] header, int index) => (U32(header, 12 + index * 8), U32(header, 16 + index * 8));

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}
