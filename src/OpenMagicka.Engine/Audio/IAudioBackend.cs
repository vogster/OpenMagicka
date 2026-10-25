using System.Numerics;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Audio;

/// <summary>Where a sound is heard from or at: a point, the way it faces and its up (no velocity: the original has no Doppler).</summary>
public readonly record struct SoundPlace(Vector3 Position, Vector3 Forward, Vector3 Up)
{
    public static SoundPlace At(Vector3 position, Vector3 forward) =>
        new(position, forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : -Vector3.UnitZ, Vector3.UnitY);
}

/// <summary>A cue of a sound bank (docs/engine/ARCHITECTURE.md §8): prepared, played, placed, stopped.</summary>
public interface ISoundCue : IDisposable
{
    string Name { get; }

    bool IsPlaying { get; }

    bool IsPaused { get; }

    bool IsStopping { get; }

    bool IsStopped { get; }

    void Play();

    /// <param name="immediate">At once, or as authored (its fade or tail).</param>
    void Stop(bool immediate);

    void Pause();

    void Resume();

    /// <summary>Sets one of the cue's variables (Volume, Magnitude, NormalizedDistance…); one it lacks is ignored.</summary>
    void SetVariable(string name, float value);

    /// <summary>Places it for the listener (a snapshot: a cue follows its source only when placed again).</summary>
    void Apply3D(SoundPlace listener, SoundPlace emitter);
}

/// <summary>
/// The sound device and the game's banks (docs/engine/ARCHITECTURE.md §8), as ADR-0001 plans: XACT through FNA, or
/// silence. Its calls are the logic thread's.
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>Whether a bank is loaded (they load in the background; one not loaded yet plays nothing).</summary>
    bool IsLoaded(SoundBanks bank);

    /// <summary>A prepared cue of a bank, by its name; none when the bank or the cue is not there.</summary>
    ISoundCue? GetCue(SoundBanks bank, string name);

    /// <summary>Sets a global variable (RoomType, ReverbMix, MusicFocus, MusicThreat); one the engine lacks is ignored.</summary>
    void SetGlobal(string name, float value);

    /// <summary>Sets a category's volume (0…1).</summary>
    void SetVolume(string category, float volume);

    /// <summary>The device's step (once a tick).</summary>
    void Update();
}

/// <summary>No sound: no device, no game content, or a test.</summary>
public sealed class SilentAudio : IAudioBackend
{
    public bool IsLoaded(SoundBanks bank) => false;

    public ISoundCue? GetCue(SoundBanks bank, string name) => null;

    public void SetGlobal(string name, float value)
    {
    }

    public void SetVolume(string category, float volume)
    {
    }

    public void Update()
    {
    }

    public void Dispose()
    {
    }
}
