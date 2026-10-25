using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;

namespace OpenMagicka.Tests.Engine;

/// <summary>A sound device that only remembers what it was told (OM-51).</summary>
internal sealed class FakeAudio(params string[] cues) : IAudioBackend
{
    public Dictionary<string, float> Globals { get; } = [];

    public Dictionary<string, float> Volumes { get; } = [];

    public List<FakeCue> Made { get; } = [];

    public int Updates { get; private set; }

    public bool IsLoaded(SoundBanks bank) => true;

    public ISoundCue? GetCue(SoundBanks bank, string name)
    {
        if (!cues.Contains(name))
        {
            return null;
        }
        var cue = new FakeCue(name, bank);
        Made.Add(cue);
        return cue;
    }

    public void SetGlobal(string name, float value) => Globals[name] = value;

    public void SetVolume(string category, float volume) => Volumes[category] = volume;

    public void Update() => Updates++;

    public void Dispose()
    {
    }
}

internal sealed class FakeCue(string name, SoundBanks bank) : ISoundCue
{
    public string Name { get; } = name;

    public SoundBanks Bank { get; } = bank;

    public Dictionary<string, float> Variables { get; } = [];

    public List<SoundPlace> Places { get; } = [];

    public bool IsPlaying { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsStopping { get; private set; }

    public bool IsStopped { get; private set; }

    public bool StoppedAtOnce { get; private set; }

    public bool IsDisposed { get; private set; }

    public void Play() => IsPlaying = !IsStopped;

    public void Stop(bool immediate)
    {
        (IsStopping, StoppedAtOnce) = (!immediate, immediate);
        if (immediate)
        {
            Finish();
        }
    }

    /// <summary>Its authored tail ends, or it played out.</summary>
    public void Finish() => (IsPlaying, IsPaused, IsStopping, IsStopped) = (false, false, false, true);

    public void Pause() => IsPaused = IsPlaying;

    public void Resume() => IsPaused = false;

    public void SetVariable(string name, float value) => Variables[name] = value;

    public void Apply3D(SoundPlace listener, SoundPlace emitter) => Places.Add(emitter);

    public void Dispose() => IsDisposed = true;
}
