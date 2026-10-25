namespace OpenMagicka.Content.Audio;

/// <summary>
/// A wave bank entry's packed format (XACT content version 45, docs/engine/ARCHITECTURE.md §8.2): 2 bits of encoding,
/// 3 of channels, 18 of sample rate, 8 of block alignment and 1 of sample width. For MS-ADPCM the block field is a
/// channel's block size less 22; for WMA its low 5 bits index the block sizes and its high 3 the byte rates.
/// </summary>
public readonly record struct MiniWaveFormat(WaveFormat Tag, int Channels, int SampleRate, int BlockAlign, bool Wide)
{
    /// <summary>The WMA byte rates and block sizes the block field indexes (XACT's documented tables).</summary>
    public static readonly int[] WmaBytesPerSecond = [12000, 24000, 4000, 6000, 8000, 20000, 2500];

    public static readonly int[] WmaBlockSizes = [929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304, 1536, 1485, 1008, 2731, 4096, 6827, 5462, 1280];

    /// <summary>The block field of MS-ADPCM's blocks is a channel's block less this.</summary>
    public const int AdpcmBlockBias = 22;

    public static MiniWaveFormat From(uint packed) => new((WaveFormat)(packed & 3), (int)((packed >> 2) & 7), (int)((packed >> 5) & 0x3FFFF),
        (int)((packed >> 23) & 0xFF), (packed >> 31) != 0);

    public uint Packed
    {
        get
        {
            if (Channels is < 0 or > 7 || SampleRate is < 0 or > 0x3FFFF || BlockAlign is < 0 or > 0xFF)
            {
                throw new InvalidOperationException($"{this} does not pack");
            }
            return (uint)Tag | (uint)Channels << 2 | (uint)SampleRate << 5 | (uint)BlockAlign << 23 | (Wide ? 1u << 31 : 0);
        }
    }

    /// <summary>A WMA entry's byte rate; 0 for one out of the table.</summary>
    public int WmaByteRate => BlockAlign >> 5 < WmaBytesPerSecond.Length ? WmaBytesPerSecond[BlockAlign >> 5] : 0;

    /// <summary>The byte rates xWMA allows, by channels and sample rate.</summary>
    private static readonly (int Channels, int Rate, int[] Allowed)[] XwmaByteRates =
    [
        (6, 48000, [16000, 24000]), (2, 48000, [6000, 8000, 12000, 20000, 24000]), (6, 44100, [16000, 24000]),
        (2, 44100, [4000, 6000, 12000, 24000]), (1, 44100, [4000, 6000]), (2, 32000, [4000, 6000]), (1, 32000, [2500]),
        (2, 22050, [4000]), (1, 22050, [2500]),
    ];

    /// <summary>
    /// The byte rate the WMA data is really at: XACT writes rates xWMA does not allow (6000 for 22 kHz mono, which is
    /// 2500), and Windows' decoder only takes allowed ones. The highest allowed rate not above the bank's, else the
    /// lowest (as XAudio and FAudio correct it).
    /// </summary>
    public int AllowedWmaByteRate
    {
        get
        {
            var (stated, channels, rate) = (WmaByteRate, Channels, SampleRate);
            var (_, _, allowed) = Array.Find(XwmaByteRates, r => r.Channels == channels && r.Rate == rate);
            return allowed is null ? stated : allowed.Where(a => a <= stated).DefaultIfEmpty(allowed[0]).Max();
        }
    }

    /// <summary>A WMA entry's packet size; 0 for one out of the table.</summary>
    public int WmaBlockSize => (BlockAlign & 0x1F) < WmaBlockSizes.Length ? WmaBlockSizes[BlockAlign & 0x1F] : 0;

    /// <summary>An MS-ADPCM entry of channels at a rate, in blocks of a channel's size.</summary>
    public static MiniWaveFormat Adpcm(int channels, int sampleRate, int channelBlock) =>
        new(WaveFormat.Adpcm, channels, sampleRate, channelBlock - AdpcmBlockBias, false);
}
