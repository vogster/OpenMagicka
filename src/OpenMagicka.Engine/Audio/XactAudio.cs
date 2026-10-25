using OpenMagicka.Content;
using OpenMagicka.Content.Audio;
using OpenMagicka.Content.Magicka;
using Microsoft.Xna.Framework.Audio;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// The game's sound through FNA's XACT (FACT), as the original's audio manager (docs/engine/ARCHITECTURE.md §8):
/// <c>Audio/magicka.xgs</c>, then the banks in the original's order on a thread of their own (Music, Ambience and
/// AdditionalMusic stream), then the language's speech bank. FAudio cannot play WMA (RISK_CHECKS.md §2): a wave bank
/// with WMA entries is loaded from its transcoded copy (<see cref="TranscodedBanks"/>); one not transcoded yet is made
/// after the other banks have loaded. Without a WMA decoder it is left out, and the cues that use it play without
/// those waves.
/// </summary>
public sealed class XactAudio : IAudioBackend
{
    /// <summary>A slot's sound bank and wave bank (in <c>Audio/</c>), and whether its waves stream.</summary>
    public sealed record BankFiles(SoundBanks Bank, string Sounds, string Waves, bool Streams);

    /// <summary>Sound bank slots by the bit of their flag, in the original's loading order; the speech bank comes last.</summary>
    public static readonly BankFiles[] Banks =
    [
        new(SoundBanks.WaveBank, "Sound Bank", "Wave Bank", false), Same(SoundBanks.Music, true), Same(SoundBanks.Ambience, true),
        Same(SoundBanks.UI), Same(SoundBanks.Spells), Same(SoundBanks.Characters), Same(SoundBanks.Footsteps), Same(SoundBanks.Weapons),
        Same(SoundBanks.Misc), Same(SoundBanks.Additional), Same(SoundBanks.AdditionalMusic, true),
    ];

    private static BankFiles Same(SoundBanks bank, bool streams = false) => new(bank, bank.ToString(), bank.ToString(), streams);

    public const string Settings = "Audio/magicka.xgs";
    private const short PacketSize = 64;

    private readonly AudioEngine engine;
    private readonly ContentFileSystem files;
    private readonly SoundBank?[] soundBanks = new SoundBank?[Banks.Length];
    private readonly List<WaveBank> waveBanks = [];
    private readonly Task loading;
    private readonly CancellationTokenSource stop = new();
    private readonly List<string> skipped = [];
    private readonly List<(string Asset, string Path)> wma = [];
    private readonly TranscodedBanks? transcoded;
    private int loaded;
    private bool disposed;

    private XactAudio(AudioEngine engine, ContentFileSystem files, string language, TranscodedBanks? transcoded)
    {
        (this.engine, this.files, this.transcoded) = (engine, files, transcoded);
        loading = Task.Run(() => LoadBanks(language));
    }

    /// <summary>The device and the game's settings; none when there is no device or the content lacks them (the game is then silent).</summary>
    /// <param name="transcoded">Where the WMA banks' copies are (and are made); none leaves those banks out.</param>
    public static XactAudio? Open(ContentFileSystem files, string language, out string? failure, TranscodedBanks? transcoded = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        failure = null;
        if (files.Resolve(Settings) is not { } settings)
        {
            failure = $"{Settings} is missing";
            return null;
        }
        try
        {
            return new XactAudio(new AudioEngine(settings), files, language, transcoded);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or ArgumentException or DllNotFoundException
            or EntryPointNotFoundException or TypeInitializationException)
        {
            failure = $"{e.GetType().Name}: {e.Message}";
            return null;
        }
    }

    /// <summary>The banks left out (WMA, missing, unreadable) and why, as loading found them.</summary>
    public IReadOnlyList<string> Skipped
    {
        get
        {
            lock (skipped)
            {
                return [.. skipped];
            }
        }
    }

    /// <summary>Waits for the banks (for tests and tools).</summary>
    public bool WaitLoaded(TimeSpan timeout) => loading.Wait(timeout);

    public bool IsLoaded(SoundBanks bank) => bank != SoundBanks.None && (Volatile.Read(ref loaded) & (int)bank) == (int)bank;

    public ISoundCue? GetCue(SoundBanks bank, string name)
    {
        if (disposed || string.IsNullOrEmpty(name) || Slot(bank) is not { } slot || !IsLoaded(Banks[slot].Bank) || soundBanks[slot] is not { } sounds)
        {
            return null;
        }
        try
        {
            return new XactCue(sounds.GetCue(name));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;                                                      // a name the bank lacks
        }
    }

    public void SetGlobal(string name, float value)
    {
        try
        {
            engine.SetGlobalVariable(name, value);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
        {
        }
    }

    public void SetVolume(string category, float volume)
    {
        try
        {
            engine.GetCategory(category).SetVolume(Math.Clamp(volume, 0, 1));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
        }
    }

    public void Update()
    {
        if (!disposed)
        {
            engine.Update();
        }
    }

    /// <summary>The bank's slot: its lowest flag (a cue names one bank).</summary>
    private static int? Slot(SoundBanks bank) => bank == SoundBanks.None ? null : Array.FindIndex(Banks, b => (b.Bank & bank) != 0) is var i and >= 0 ? i : null;

    private void LoadBanks(string language)
    {
        for (var slot = 0; slot < Banks.Length && !stop.IsCancellationRequested; slot++)
        {
            var (bank, sound, wave, streams) = Banks[slot];
            LoadWaveBank($"Audio/{wave}.xwb", streams);
            if (files.Resolve($"Audio/{sound}.xsb") is { } path)
            {
                try
                {
                    soundBanks[slot] = new SoundBank(engine, path);
                    Interlocked.Or(ref loaded, (int)bank);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Skip($"{sound}.xsb: {e.Message}");                        // one bad bank leaves the others
                }
            }
            else
            {
                Skip($"{sound}.xsb: missing");
            }
        }
        var own = $"Languages/{language}/Loc.xwb";
        LoadWaveBank(files.Exists(own) ? own : "Languages/eng/Loc.xwb", streams: false);
        foreach (var (asset, path) in wma)
        {
            if (stop.IsCancellationRequested)
            {
                return;
            }
            LoadTranscoded(asset, path);
        }
    }

    /// <summary>A WMA bank's transcoded copy, made now when there is none (the other banks play meanwhile).</summary>
    private void LoadTranscoded(string asset, string path)
    {
        try
        {
            if (transcoded!.FindOrMake(path, stop.Token) is { } copy)
            {
                Add(new WaveBank(engine, copy));
            }
            else
            {
                Skip($"{asset}: WMA, and no WMA decoder here");
            }
        }
        catch (OperationCanceledException)
        {
            // quitting: the copy is made next time
        }
        catch (Exception e)
        {
            Skip($"{asset}: WMA, not transcoded: {e.Message}");
        }
    }

    /// <summary>A transcoded copy loaded; false (and it deleted, to be made again) for one that does not load.</summary>
    private bool LoadCopy(string copy)
    {
        try
        {
            Add(new WaveBank(engine, copy));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            File.Delete(copy);
            return false;
        }
    }

    private void Add(WaveBank bank)
    {
        lock (waveBanks)
        {
            waveBanks.Add(bank);
        }
    }

    private void LoadWaveBank(string asset, bool streams)
    {
        if (files.Resolve(asset) is not { } path)
        {
            Skip($"{asset}: missing");
            return;
        }
        try
        {
            if (WaveBankInfo.Read(path).UsesWma)
            {
                if (transcoded?.Find(path) is { } copy && LoadCopy(copy))
                {
                    return;
                }
                if (transcoded is not null)
                {
                    wma.Add((asset, path));                                   // made once the others have loaded
                }
                else
                {
                    Skip($"{asset}: WMA, not transcoded");
                }
                return;
            }
            Add(streams ? new WaveBank(engine, path, 0, PacketSize) : new WaveBank(engine, path));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Skip($"{asset}: {e.Message}");
        }
    }

    private void Skip(string why)
    {
        lock (skipped)
        {
            skipped.Add(why);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        stop.Cancel();
        bool finished;
        try
        {
            finished = loading.Wait(DisposeWait);
        }
        catch (AggregateException)
        {
            finished = true;                                                  // it ended (stopped mid-transcoding): its banks are freed below
        }
        if (!finished)
        {
            return;                                                           // quitting does not wait for a big bank: the process ends with it
        }
        Release();
    }

    /// <summary>How long quitting waits for the bank being loaded.</summary>
    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(1);

    private void Release()
    {
        foreach (var bank in soundBanks)
        {
            bank?.Dispose();
        }
        lock (waveBanks)
        {
            waveBanks.ForEach(w => w.Dispose());
        }
        engine.Dispose();
        stop.Dispose();
    }
}
