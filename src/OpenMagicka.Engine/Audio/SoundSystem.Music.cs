using System.Numerics;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// The music, its focus and threat, and the reverb (docs/engine/ARCHITECTURE.md §8): one music cue at a time (a new one
/// fades the old as authored; the same one again goes on), outside the cues stopped or paused together. Its cues read
/// <c>MusicFocus</c> (how far the nearest living avatar is from a focus point, over its radius, or a default) and
/// <c>MusicThreat</c> (up 5 a second to 10 while something fights, down 2). The scene's <c>RoomType</c> is set at once,
/// its <c>ReverbMix</c> moves 50 a second.
/// </summary>
public sealed partial class SoundSystem
{
    public const float ThreatRise = 5, ThreatFall = 2, MaxThreat = 10, ReverbSpeed = 50;

    private readonly List<ISoundCue> fading = [];
    private ISoundCue? music;
    private Vector3? focusAt;
    private float focusRadius, defaultFocus = 1;
    private float reverbMix, reverbTarget;

    /// <summary>Something fights (an AI has a target): the music's threat rises.</summary>
    public bool Threat { get; set; }

    public float ThreatLevel { get; private set; }

    public float MusicFocus { get; private set; } = 1;

    public float ReverbMix => reverbMix;

    /// <summary>The music playing (or fading out while nothing replaced it); none.</summary>
    public string? Music => music?.Name;

    /// <summary>
    /// The music changes to a cue: a focus point is let go, a focus value given becomes the default; the same cue
    /// playing goes on, another fades as authored. A name the game lacks starts nothing.
    /// </summary>
    public void PlayMusic(SoundBanks bank, int hash, float? focus = null)
    {
        if (disposed)
        {
            return;
        }
        focusAt = null;
        defaultFocus = focus ?? defaultFocus;
        var name = Names.Find(hash);
        if (music is { } playing)
        {
            if (string.Equals(playing.Name, name, StringComparison.OrdinalIgnoreCase) && !playing.IsStopping && !playing.IsStopped)
            {
                return;
            }
            playing.Stop(immediate: false);
            fading.Add(playing);
        }
        if (name is not null && Backend.GetCue(bank, name) is { } cue)
        {
            cue.Play();
            music = cue;
        }
    }

    public void PlayMusic(SoundBanks bank, string name, float? focus = null) => PlayMusic(bank, Content.LegacyStringHash.Lower(name ?? ""), focus);

    /// <summary>The music fades out as authored.</summary>
    public void StopMusic()
    {
        music?.Stop(immediate: false);
        fading.ForEach(c => c.Stop(immediate: false));
    }

    /// <summary>The music's focus follows the distance to a point over a radius; <paramref name="clearTo"/> is the value without one.</summary>
    public void FocusMusic(Vector3 at, float radius, float clearTo)
    {
        (focusAt, focusRadius, defaultFocus) = (at, radius, clearTo);
    }

    /// <summary>The focus point is let go (the scene ends).</summary>
    public void ClearMusicFocus() => focusAt = null;

    /// <summary>A scene's reverb: its room at once, its mix moving to it.</summary>
    public void SetRoom(RoomType room, float mix)
    {
        Backend.SetGlobal("RoomType", (int)room);
        reverbTarget = mix;
    }

    /// <summary>The reverb fades out (a scene's transition begins).</summary>
    public void FadeReverb() => reverbTarget = 0;

    private void StepReverb(float dt)
    {
        var step = ReverbSpeed * dt;
        reverbMix = reverbTarget > reverbMix ? MathF.Min(reverbMix + step, reverbTarget) : MathF.Max(reverbMix - step, reverbTarget);   // ours: no undershoot
        Backend.SetGlobal("ReverbMix", reverbMix);
    }

    private void StepMusic(float dt, IEnumerable<Vector3> players)
    {
        MusicFocus = focusAt is { } at ? Focus(at, players) : defaultFocus;
        Backend.SetGlobal("MusicFocus", MusicFocus);
        ThreatLevel = Threat ? MathF.Min(ThreatLevel + ThreatRise * dt, MaxThreat) : MathF.Max(ThreatLevel - ThreatFall * dt, 0);
        Backend.SetGlobal("MusicThreat", ThreatLevel);
    }

    private float Focus(Vector3 at, IEnumerable<Vector3> players)
    {
        var nearest = players.Select(p => Vector3.Distance(p, at)).DefaultIfEmpty(float.MaxValue).Min();
        return focusRadius > 0 ? Math.Clamp(nearest / focusRadius, 0, 1) : nearest > 0 ? 1 : 0;
    }

    private void ForgetStoppedMusic()
    {
        if (music is { IsStopped: true } done)
        {
            fading.Remove(done);
            done.Dispose();
            music = null;
        }
        for (var i = fading.Count - 1; i >= 0; i--)
        {
            if (fading[i].IsStopped)
            {
                fading[i].Dispose();
                fading.RemoveAt(i);
            }
        }
    }

    private void DisposeMusic()
    {
        foreach (var cue in fading.Append(music).OfType<ISoundCue>().Distinct())
        {
            cue.Stop(immediate: true);
            cue.Dispose();
        }
        (music, focusAt) = (null, null);
        fading.Clear();
    }
}
