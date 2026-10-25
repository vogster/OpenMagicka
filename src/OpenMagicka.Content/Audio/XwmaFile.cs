using System.Buffers.Binary;

namespace OpenMagicka.Content.Audio;

/// <summary>
/// A WMA wave of a bank as a stand-alone xWMA file (docs/engine/ARCHITECTURE.md §8.2), which decoders read: RIFF
/// "XWMA" with a "fmt " chunk (WAVEFORMATEX: WMA 2, or WMA Pro for the wide flag, the channels, the rate, the
/// allowed byte rate and the table's packet size, 16 bits), a "dpds" chunk (the seek table: the decoded bytes at each packet's
/// end) and the "data" chunk.
/// </summary>
public static class XwmaFile
{
    public const ushort Wma2 = 0x161, WmaPro = 0x162;

    public static byte[] Wrap(WaveBankEntry entry, ReadOnlySpan<byte> wave)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var format = entry.Format;
        if (format.Tag != WaveFormat.Wma || entry.SeekTable is not { Count: > 0 } seek || format.WmaByteRate == 0 || format.WmaBlockSize == 0)
        {
            throw new InvalidDataException("not a WMA wave with a seek table");
        }
        const int formatSize = 18;
        var length = 4 + (8 + formatSize) + (8 + seek.Count * 4) + (8 + wave.Length);
        var file = new byte[8 + length];
        var at = 0;
        void Tag(string tag)
        {
            for (var i = 0; i < 4; i++)
            {
                file[at++] = (byte)tag[i];
            }
        }
        void U16(int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at, 2), (ushort)value);
            at += 2;
        }
        void U32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at, 4), value);
            at += 4;
        }
        Tag("RIFF");
        U32((uint)length);
        Tag("XWMA");
        Tag("fmt ");
        U32(formatSize);
        U16(format.Wide ? WmaPro : Wma2);
        U16(format.Channels);
        U32((uint)format.SampleRate);
        U32((uint)format.AllowedWmaByteRate);                                  // the rate the data is at (the bank's may be wrong)
        U16(format.WmaBlockSize);
        U16(16);
        U16(0);
        Tag("dpds");
        U32((uint)(seek.Count * 4));
        foreach (var bytes in seek)
        {
            U32(bytes);
        }
        Tag("data");
        U32((uint)wave.Length);
        wave.CopyTo(file.AsSpan(at));
        return file;
    }
}
