using System.Security.Cryptography;
using OpenMagicka.Content.Audio;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// The game's WMA wave banks transcoded once into OpenMagicka's data folder (docs/engine/ARCHITECTURE.md §8.2), as
/// FAudio cannot play WMA: each WMA wave decoded (<see cref="IWmaDecoder"/>) and coded again as MS-ADPCM in 70-byte
/// blocks a channel (as the game's own ADPCM banks), the other waves kept, every wave at its index with its name, so
/// the sound banks find them as before. A bank is kept by its source file's hash: a changed file (another language's
/// speech, a patched game) is transcoded anew. Nothing is written to the game's folder.
/// </summary>
public sealed class TranscodedBanks(string directory, Func<IWmaDecoder?> decoder)
{
    /// <summary>A channel's MS-ADPCM block (128 samples), as the game's ADPCM banks have.</summary>
    public const int ChannelBlock = 70;

    /// <summary>Changes when the transcoding does, so older files are made again.</summary>
    public const string Format = "adpcm70-1";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, long Length, DateTime Written), string> names = new();
    private IWmaDecoder? found;
    private bool looked;

    public string Directory { get; } = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>OpenMagicka's own data folder for them (the user's local application data).</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenMagicka", "Audio");

    /// <summary>The decoder found (looked for once); none here.</summary>
    public IWmaDecoder? Decoder
    {
        get
        {
            if (!looked)
            {
                (found, looked) = (decoder(), true);
            }
            return found;
        }
    }

    /// <summary>Where a source bank's transcoded copy is (or goes); the source is hashed once while it is unchanged.</summary>
    public string PathOf(string source)
    {
        var file = new FileInfo(source ?? throw new ArgumentNullException(nameof(source)));
        return names.GetOrAdd((file.FullName, file.Length, file.LastWriteTimeUtc), key =>
        {
            using var stream = File.OpenRead(key.Path);
            return Path.Combine(Directory, $"{Convert.ToHexString(SHA256.HashData(stream))[..24].ToLowerInvariant()}-{Format}.xwb");
        });
    }

    /// <summary>The transcoded copy when it was made already; none.</summary>
    public string? Find(string source)
    {
        var path = PathOf(source);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The transcoded copy, made now when it is not there; none without a decoder.</summary>
    public string? FindOrMake(string source, CancellationToken token = default)
    {
        if (Find(source) is { } made)
        {
            return made;
        }
        if (Decoder is not { } wma)
        {
            return null;
        }
        var bytes = Transcode(WaveBankFile.Read(source), wma, token);
        var path = PathOf(source);
        System.IO.Directory.CreateDirectory(Directory);
        var partial = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.part";   // two games at once write apart
        File.WriteAllBytes(partial, bytes);
        File.Move(partial, path, overwrite: true);                           // whole or not at all
        return path;
    }

    /// <summary>
    /// A bank with its WMA waves as MS-ADPCM (more than two channels as PCM: FAudio's MS-ADPCM is mono or stereo), the
    /// others as they are. A wave that does not decode is silence, the rest of the bank kept.
    /// </summary>
    public static byte[] Transcode(WaveBankFile bank, IWmaDecoder decoder, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(decoder);
        var waves = new WaveBankWave[bank.Entries.Count];
        var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        Parallel.For(0, waves.Length, options, i =>                          // each wave on its own: the decoders are made per wave
        {
            var entry = bank.Entries[i];
            var format = entry.Format;
            waves[i] = format.Tag == WaveFormat.Wma
                ? Transcoded(entry, Decoded(decoder, bank, entry))
                : new WaveBankWave(entry.Flags, entry.Duration, format, bank.Wave(entry).ToArray(), entry.LoopStart, entry.LoopLength, entry.Name);
        });
        return WaveBankWriter.Write(bank.Name, waves, Math.Max(bank.Alignment, 4), bank.BuildTime);
    }

    private static short[] Decoded(IWmaDecoder decoder, WaveBankFile bank, WaveBankEntry entry)
    {
        try
        {
            return decoder.Decode(entry, bank.Wave(entry));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new short[entry.Duration * entry.Format.Channels];         // one bad wave is silent, not the whole bank
        }
    }

    private static WaveBankWave Transcoded(WaveBankEntry entry, short[] samples)
    {
        var (channels, rate) = (entry.Format.Channels, entry.Format.SampleRate);
        return channels <= 2
            ? new WaveBankWave(entry.Flags, entry.Duration, MiniWaveFormat.Adpcm(channels, rate, ChannelBlock), MsAdpcm.Encode(samples, channels, ChannelBlock),
                entry.LoopStart, entry.LoopLength, entry.Name)
            : new WaveBankWave(entry.Flags, entry.Duration, new MiniWaveFormat(WaveFormat.Pcm, channels, rate, channels * 2, true),
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), entry.LoopStart, entry.LoopLength, entry.Name);
    }
}
