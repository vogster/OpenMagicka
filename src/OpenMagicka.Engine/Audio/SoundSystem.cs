using System.Numerics;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// The game's sounds at play, as the original's audio manager (docs/engine/ARCHITECTURE.md §8): cues by bank and
/// name hash, played flat or for the camera's listener, kept in a list till they stop (to stop, pause and resume them
/// all); the music apart from them; the reverb, the music's focus and its threat as the sound's global variables.
/// Logic thread.
/// </summary>
public sealed partial class SoundSystem : IDisposable
{
    /// <summary>The volume settings' scale (0…10) and their defaults.</summary>
    public const int MaxVolume = 10, DefaultVolume = 7;

    /// <summary>The longest step the smoothing takes (a hitch does not jump the reverb).</summary>
    public const float MaxStep = 0.05f;

    public static readonly string[] SoundCategories = ["Ambient", "Default", "Sfx", "UI", "VO", "VOloc"];

    private readonly List<ISoundCue> active = [];
    private readonly List<ISoundCue> paused = [];
    private bool disposed;

    public SoundSystem(IAudioBackend backend, SoundNames names)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Names = names ?? throw new ArgumentNullException(nameof(names));
        SetVolumes(DefaultVolume, DefaultVolume);
    }

    public IAudioBackend Backend { get; }

    public SoundNames Names { get; }

    /// <summary>Where the sounds are heard: over the camera's ground point, looking down (the camera sets it each tick).</summary>
    public SoundPlace Listener { get; private set; } = new(new Vector3(0, ListenerHeight, 0), -Vector3.UnitY, -Vector3.UnitZ);

    public const float ListenerHeight = 20;

    /// <summary>The cues playing or prepared (not the music).</summary>
    public IReadOnlyList<ISoundCue> Active => active;

    /// <summary>The listener over a ground point.</summary>
    public void Listen(Vector3 ground) => Listener = new SoundPlace(ground + new Vector3(0, ListenerHeight, 0), -Vector3.UnitY, -Vector3.UnitZ);

    /// <summary>The settings' volumes (0…10): the music's category, and the others' together.</summary>
    public void SetVolumes(int music, int sound)
    {
        Backend.SetVolume("Music", Math.Clamp(music, 0, MaxVolume) * 0.1f);
        foreach (var category in SoundCategories)
        {
            Backend.SetVolume(category, Math.Clamp(sound, 0, MaxVolume) * 0.1f);
        }
    }

    /// <summary>A cue of a bank by its name's hash, prepared with its variables (not played); none for a name the game lacks.</summary>
    public ISoundCue? Get(SoundBanks bank, int hash, params ReadOnlySpan<(string Name, float Value)> variables)
    {
        if (disposed || Names.Find(hash) is not { } name || Backend.GetCue(bank, name) is not { } cue)
        {
            return null;
        }
        foreach (var (variable, value) in variables)
        {
            cue.SetVariable(variable, value);
        }
        active.Add(cue);
        return cue;
    }

    /// <summary>A cue by name (in any case) as <see cref="Get(SoundBanks, int, ReadOnlySpan{ValueTuple{string, float}})"/>.</summary>
    public ISoundCue? Get(SoundBanks bank, string name, params ReadOnlySpan<(string Name, float Value)> variables) =>
        Get(bank, Content.LegacyStringHash.Lower(name ?? ""), variables);

    /// <summary>A cue played flat, or for the listener from a place.</summary>
    public ISoundCue? Play(SoundBanks bank, int hash, SoundPlace? at = null, params ReadOnlySpan<(string Name, float Value)> variables)
    {
        var cue = Get(bank, hash, variables);
        if (cue is not null)
        {
            if (at is { } emitter)
            {
                cue.Apply3D(Listener, emitter);
            }
            cue.Play();
        }
        return cue;
    }

    public ISoundCue? Play(SoundBanks bank, string name, SoundPlace? at = null, params ReadOnlySpan<(string Name, float Value)> variables) =>
        Play(bank, Content.LegacyStringHash.Lower(name ?? ""), at, variables);

    /// <summary>A cue placed again for the listener (one that follows its source).</summary>
    public void Place(ISoundCue cue, SoundPlace emitter)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (!cue.IsStopped)
        {
            cue.Apply3D(Listener, emitter);
        }
    }

    /// <summary>
    /// A step, in the original's order: stopped music and cues are let go, the reverb's mix moves to its target, the
    /// music's focus and threat are worked out, the device steps. <paramref name="players"/> are the living avatars.
    /// </summary>
    public void Update(float dt, IEnumerable<Vector3> players)
    {
        if (disposed)
        {
            return;
        }
        dt = Math.Clamp(dt, 0, MaxStep);
        ForgetStoppedMusic();
        for (var i = active.Count - 1; i >= 0; i--)
        {
            if (active[i].IsStopped)
            {
                active[i].Dispose();
                active.RemoveAt(i);
            }
        }
        StepReverb(dt);
        StepMusic(dt, players);
        Backend.Update();
    }

    /// <summary>Every cue stops (not the music): at once (a restart), or as authored (leaving the level).</summary>
    public void StopAll(bool immediate)
    {
        foreach (var cue in active.Where(c => !c.IsStopped))
        {
            cue.Stop(immediate);
        }
    }

    /// <summary>Every playing cue pauses (not the music), to be resumed together.</summary>
    public void PauseAll()
    {
        paused.Clear();
        foreach (var cue in active.Where(c => c.IsPlaying && !c.IsPaused))
        {
            cue.Pause();
            paused.Add(cue);
        }
    }

    public void ResumeAll()
    {
        foreach (var cue in paused.Where(c => c.IsPaused))
        {
            cue.Resume();
        }
        paused.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (var cue in active)
        {
            cue.Stop(immediate: true);
            cue.Dispose();
        }
        active.Clear();
        paused.Clear();
        DisposeMusic();
    }
}
