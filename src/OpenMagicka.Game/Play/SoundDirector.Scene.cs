using System.Numerics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Scripting;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The scripts' sounds (docs/engine/ARCHITECTURE.md §8, LEVELS_TRIGGERS.md §9.3): a cue without a name plays once;
/// a named one is the scene's, kept to stop or tune: a global one (no place) plays again when asked again, a located
/// one plays once for as long as it lasts, its <c>NormalizedDistance</c> the camera's distance over its radius. A
/// scene left with its NPCs kept pauses its sounds till it is entered again; else they stop. The music is the
/// scripts' too, and with their named sounds goes into a checkpoint. A dialog's lines (negative ids) are no scene's:
/// each plays once, stopped by its id.
/// </summary>
public sealed partial class SoundDirector : IScriptSounds
{
    private readonly Dictionary<int, SceneSounds> scenes = [];
    private readonly Dictionary<int, ISoundCue> lines = [];

    /// <summary>The music the scripts last chose (bank, cue, focus value), for a checkpoint.</summary>
    public (SoundBanks Bank, string Cue, float? Focus)? Music { get; private set; }

    /// <summary>A scene's named sounds: the global ones by id, and the located ones.</summary>
    private sealed class SceneSounds
    {
        public Dictionary<int, Named> Global { get; } = [];

        public Dictionary<int, Named> Located { get; } = [];
    }

    private sealed record Named(SoundBanks Bank, string Cue, float? Volume, Vector3? At, float Radius, bool Apply3D)
    {
        public ISoundCue? Playing { get; set; }

        public float? Volume { get; set; } = Volume;
    }

    /// <summary>A request of the scripts for the sound: true when it was one.</summary>
    public bool Take(ScriptRequest request)
    {
        switch (request)
        {
            case PlaySoundRequest { Id: < 0 } line:
                lines.Remove(line.Id, out var said);
                said?.Stop(immediate: false);
                if (Sounds.Play(line.Bank, line.Cue, line.At is { } speaker ? SoundPlace.At(speaker, -Vector3.UnitZ) : null, Volume(line.Volume)) is { } saying)
                {
                    lines[line.Id] = saying;
                }
                return true;
            case StopSoundRequest { Id: < 0 } hush:
                if (lines.Remove(hush.Id, out var hushed))
                {
                    hushed.Stop(hush.Instant);
                }
                return true;
            case PlaySoundRequest { Id: 0 } once:
                Sounds.Play(once.Bank, once.Cue, once is { At: { } at, Apply3D: true } ? SoundPlace.At(at, -Vector3.UnitZ) : null, Volume(once.Volume));
                return true;
            case PlaySoundRequest { At: null } global:
                PlayGlobal(global);
                return true;
            case PlaySoundRequest located:
                PlayLocated(located);
                return true;
            case StopSoundRequest stop:
                Stop(stop.Id, stop.Instant);
                return true;
            case SetVolumeRequest tune:
                Tune(tune.Id, tune.Volume);
                return true;
            case PlayMusicRequest music:
                Music = (music.Bank, music.Cue, music.Focus);
                Sounds.PlayMusic(music.Bank, music.Cue, music.Focus);
                return true;
            case StopMusicRequest:
                Sounds.StopMusic();
                return true;
            case MusicFocusRequest focus:
                Sounds.FocusMusic(focus.At, focus.Radius, focus.ClearTo);
                return true;
            case SceneChangedRequest change:
                if (change.From != change.To)                                 // a restart in its own scene keeps what the checkpoint gave back
                {
                    Leave(change.From, change.Kept);
                    Enter(change.To);
                }
                return false;                                                 // the others want it too
            default:
                return false;
        }
    }

    private static (string, float)[] Volume(float? volume) => volume is { } v ? [("Volume", v)] : [];

    private SceneSounds Here => scenes.TryGetValue(level.Current?.Key ?? 0, out var here) ? here : scenes[level.Current?.Key ?? 0] = new SceneSounds();

    private void PlayGlobal(PlaySoundRequest request)
    {
        var global = Here.Global;
        if (!global.TryGetValue(request.Id, out var named) || named.Bank != request.Bank || !string.Equals(named.Cue, request.Cue, StringComparison.OrdinalIgnoreCase))
        {
            named?.Playing?.Stop(immediate: false);
            global[request.Id] = named = new Named(request.Bank, request.Cue, request.Volume, null, 0, false);
        }
        named.Volume = request.Volume ?? named.Volume;
        Start(named);
    }

    private void PlayLocated(PlaySoundRequest request)
    {
        if (!Here.Located.ContainsKey(request.Id))
        {
            var named = new Named(request.Bank, request.Cue, request.Volume, request.At, request.Radius, request.Apply3D);
            Here.Located[request.Id] = named;
            Start(named);
        }
    }

    /// <summary>A named sound plays: made anew when it ended, resumed when paused, placed when 3D.</summary>
    private void Start(Named named)
    {
        if (named.Playing is null or { IsStopped: true } or { IsStopping: true })
        {
            named.Playing = Sounds.Get(named.Bank, named.Cue, Volume(named.Volume));
            if (named.Playing is not null && named is { Apply3D: true, At: { } at })
            {
                Sounds.Place(named.Playing, SoundPlace.At(at, -Vector3.UnitZ));
            }
        }
        else if (named.Volume is { } volume)
        {
            named.Playing.SetVariable("Volume", volume);
        }
        if (named.Playing is { IsPaused: true } paused)
        {
            paused.Resume();
        }
        else if (named.Playing is { IsPlaying: false } ready)
        {
            ready.Play();
        }
    }

    private void Stop(int id, bool instant)
    {
        if (Here.Located.Remove(id, out var located))
        {
            located.Playing?.Stop(instant);
        }
        else if (Here.Global.TryGetValue(id, out var global))
        {
            global.Playing?.Stop(instant);                                    // it stays: asked again, it plays again
        }
    }

    private void Tune(int id, float volume)
    {
        if (Here.Located.TryGetValue(id, out var located))
        {
            located.Playing?.SetVariable("Volume", volume);
        }
        else if (Here.Global.TryGetValue(id, out var global))
        {
            global.Volume = volume;
            global.Playing?.SetVariable("Volume", volume);
        }
    }

    /// <summary>The located sounds of the scene follow the camera's distance (and their place, when 3D); ended ones are let go.</summary>
    private void StepSceneSounds()
    {
        if (!scenes.TryGetValue(level.Current?.Key ?? 0, out var here))
        {
            return;
        }
        var ground = Sounds.Listener.Position - new Vector3(0, SoundSystem.ListenerHeight, 0);
        foreach (var (id, named) in here.Located.ToList())
        {
            if (named.Playing is null or { IsStopped: true } or { IsStopping: true })
            {
                here.Located.Remove(id);
                continue;
            }
            if (named.At is { } at)
            {
                if (named.Radius > 0)
                {
                    named.Playing.SetVariable("NormalizedDistance", Vector3.Distance(at, ground) / named.Radius);
                }
                if (named.Apply3D)
                {
                    Sounds.Place(named.Playing, SoundPlace.At(at, -Vector3.UnitZ));
                }
            }
            named.Playing.SetVariable("Distance", 0);
        }
    }

    private void Leave(int scene, bool kept)
    {
        if (!scenes.TryGetValue(scene, out var left))
        {
            return;
        }
        foreach (var named in left.Global.Values.Concat(left.Located.Values))
        {
            if (kept)
            {
                named.Playing?.Pause();
            }
            else
            {
                named.Playing?.Stop(immediate: false);
            }
        }
        if (!kept)
        {
            scenes.Remove(scene);
        }
    }

    /// <summary>A scene entered again: the sounds it paused when left go on.</summary>
    private void Enter(int scene)
    {
        if (scenes.TryGetValue(scene, out var entered))
        {
            foreach (var named in entered.Global.Values.Concat(entered.Located.Values))
            {
                if (named.Playing is { IsPaused: true } paused)
                {
                    paused.Resume();
                }
            }
        }
    }

    private void StopSceneSounds(bool immediate)
    {
        foreach (var named in scenes.Values.SelectMany(s => s.Global.Values.Concat(s.Located.Values)))
        {
            named.Playing?.Stop(immediate);
        }
        scenes.Clear();
    }

    /// <summary>A checkpoint's sound: the scripts' music, and the scene's named sounds with whether each was playing.</summary>
    private sealed record Saved((SoundBanks Bank, string Cue, float? Focus)? Music, IReadOnlyList<(int Id, Named Sound, bool Playing)> Global,
        IReadOnlyList<(int Id, Named Sound, bool Playing)> Located);

    public object? Save()
    {
        static List<(int, Named, bool)> Copy(Dictionary<int, Named> sounds) =>
            [.. sounds.Select(p => (p.Key, p.Value with { Playing = null }, p.Value.Playing is { IsStopped: false, IsStopping: false }))];
        var here = scenes.GetValueOrDefault(level.Current?.Key ?? 0) ?? new SceneSounds();
        return new Saved(Music, Copy(here.Global), Copy(here.Located));
    }

    /// <summary>Back to a checkpoint: every sound stops at once, the music chosen then plays anew, the scene's named sounds again.</summary>
    public void Restore(object? saved)
    {
        Sounds.StopAll(immediate: true);
        StopSceneSounds(immediate: true);
        foreach (var gun in guns.Values)
        {
            gun.Cue.Stop(immediate: true);
        }
        guns.Clear();
        bySource.Clear();
        cues.Clear();
        ForgetTheWorld();
        foreach (var line in lines.Values)
        {
            line.Stop(immediate: true);
        }
        lines.Clear();
        Sounds.StopMusic();
        var state = saved as Saved;
        Music = state?.Music;
        if (Music is var (bank, cue, focus))
        {
            Sounds.PlayMusic(bank, cue, focus);
        }
        if (state is null)
        {
            return;
        }
        var here = new SceneSounds();
        scenes[level.Checkpoint?.Scene ?? level.Current?.Key ?? 0] = here;
        foreach (var (id, sound, playing) in state.Global)
        {
            here.Global[id] = sound with { Playing = null };
            if (playing)
            {
                Start(here.Global[id]);                                       // a stopped one stays, to play when asked again
            }
        }
        foreach (var (id, sound, playing) in state.Located.Where(s => s.Playing))
        {
            here.Located[id] = sound with { Playing = null };
            Start(here.Located[id]);
        }
    }
}
