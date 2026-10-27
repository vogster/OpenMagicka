using System.Numerics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Game.Play;

/// <summary>
/// A level's sounds (docs/engine/ARCHITECTURE.md §8), on the logic thread after the level's tick: the entities'
/// sound cues are played where they rose (an entity's are stopped by name when it says so), a step's by its ground,
/// a gun's while it fires (following its holder); the listener goes over the camera's ground point, the music's
/// threat rises while an AI has a target, and a new scene sets the reverb. The scripts' requests are
/// <see cref="Take"/>n here.
/// </summary>
public sealed partial class SoundDirector : IDisposable
{
    /// <summary>A step's cue by its ground's material (Lava's makes none).</summary>
    public static readonly string?[] FootstepCues =
    [
        "footstep_generic", "footstep_gravel", "footstep_grass", "footstep_wood", "footstep_snow", "footstep_stone", "footstep_mud",
        "footstep_generic", "footstep_water", null,
    ];

    private readonly GameWorld world;
    private readonly PlayedLevel level;
    private readonly List<(Entity Source, AnimationCue Cue)> cues = [];
    private readonly Dictionary<Entity, List<ISoundCue>> bySource = new(ReferenceEqualityComparer.Instance);

    /// <summary>The sounds that go with their source (a spell's from its caster), placed with it each step.</summary>
    private readonly Dictionary<Entity, List<ISoundCue>> following = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Item, (Entity Holder, ISoundCue Cue)> guns = new(ReferenceEqualityComparer.Instance);
    private int scene = -1;
    private bool fadingOut;
    private bool disposed;

    public SoundDirector(SoundSystem sounds, GameWorld world, PlayedLevel level)
    {
        Sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.level = level ?? throw new ArgumentNullException(nameof(level));
        world.Entities.Cue += OnCue;
    }

    public SoundSystem Sounds { get; }

    private void OnCue(Entity source, AnimationCue cue)
    {
        if (cue is SoundCue or SoundStopCue or FootstepCue or GunfireCue or ConjureCue)
        {
            cues.Add((source, cue));
        }
    }

    /// <summary>A tick, after the level's: the cues played, the followers placed, the scene's sounds tuned, the sound stepped.</summary>
    public void Update(float dt, GameCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (disposed)
        {
            return;
        }
        Sounds.Listen(new Vector3(camera.Ground.X, camera.Ground.Y, camera.Ground.Z));
        StepScene();
        foreach (var (source, cue) in cues)
        {
            Play(source, cue);
        }
        cues.Clear();
        FollowGuns();
        Forget();
        StepWorld();
        StepSceneSounds();
        Sounds.Threat = world.Entities.Entities.Any(e => e is Character { IsDead: false, Mind.CurrentTarget: not null });
        Sounds.Update(dt, world.Session?.Players.Where(p => p.Playing && p.Avatar is { IsDead: false, Manager: not null })
            .Select(p => p.Avatar!.Position) ?? []);
    }

    private void Play(Entity source, AnimationCue cue)
    {
        switch (cue)
        {
            case SoundCue sound:
                var played = Sounds.Play((SoundBanks)sound.Banks, sound.Cue, SoundPlace.At(sound.Position, sound.Facing),
                    sound.Magnitude is { } magnitude ? [("Magnitude", magnitude)] : []);
                if (played is not null)
                {
                    (bySource.TryGetValue(source, out var list) ? list : bySource[source] = []).Add(played);
                    if (sound.Follow)
                    {
                        (following.TryGetValue(source, out var followed) ? followed : following[source] = []).Add(played);
                    }
                }
                break;
            case SoundStopCue stop when bySource.TryGetValue(source, out var playing):
                foreach (var named in playing.Where(c => string.Equals(c.Name, stop.Cue, StringComparison.OrdinalIgnoreCase) && !c.IsStopped))
                {
                    named.Stop(immediate: false);
                }
                break;
            case FootstepCue step when step.Material >= 0 && step.Material < FootstepCues.Length && FootstepCues[step.Material] is { } footstep:
                Sounds.Play(SoundBanks.Footsteps, footstep, SoundPlace.At(step.Feet, step.Facing), ("Weight", step.Mass), ("WaterDepth", step.WaterDepth));
                break;
            case ConjureCue { Taken: true } conjure when source is Character { IsPlayer: true } && ElementCue(conjure.Element) is { } element:
                Sounds.Play(SoundBanks.UI, element);
                break;
            case GunfireCue { Firing: true } fire when !guns.ContainsKey(fire.Source) && GunSound(fire.Source) is var (bank, name)
                && Sounds.Play(bank, name, SoundPlace.At(source.Position, Forward(source))) is { } shooting:
                guns[fire.Source] = (source, shooting);
                break;
            case GunfireCue { Firing: false } ceased when guns.Remove(ceased.Source, out var gun):
                gun.Cue.Stop(immediate: false);
                break;
        }
    }

    /// <summary>A gun's sound, "bank/cue" or a cue of the Weapons bank; none for a gun without one.</summary>
    public static (SoundBanks Bank, string Cue)? GunSound(Item gun)
    {
        ArgumentNullException.ThrowIfNull(gun);
        return gun.Template.Gun?.Sound is { Length: > 0 } sound
            ? sound.Split('/') switch
            {
                [var cue] => (SoundBanks.Weapons, cue),
                [var bank, var cue] when Enum.TryParse<SoundBanks>(bank, ignoreCase: true, out var named) => (named, cue),
                _ => null,
            }
            : null;
    }

    private static Vector3 Forward(Entity entity) => entity is Character character ? character.Body.Facing : -Vector3.UnitZ;

    private void FollowGuns()
    {
        foreach (var (item, (holder, cue)) in guns.ToList())
        {
            if (cue.IsStopped || holder.IsDead || holder.Manager is null || !item.IsAttached)
            {
                cue.Stop(immediate: false);
                guns.Remove(item);
            }
            else
            {
                Sounds.Place(cue, SoundPlace.At(holder.Position, Forward(holder)));
            }
        }
    }

    /// <summary>Stopped cues and gone sources are let go (an entity's cues play out after it), and the dialog's ended lines.</summary>
    private void Forget()
    {
        foreach (var (id, line) in lines)
        {
            if (line.IsStopped)
            {
                lines.Remove(id);
            }
        }
        foreach (var (source, list) in bySource.ToList())
        {
            list.RemoveAll(c => c.IsStopped);
            if (list.Count == 0 || source.Manager is null)
            {
                bySource.Remove(source);
            }
        }
        foreach (var (source, list) in following.ToList())
        {
            list.RemoveAll(c => c.IsStopped);
            if (source.Manager is null)
            {
                list.ForEach(c => c.Stop(immediate: false));                  // its caster gone mid-spell: its spell's sounds with it
                following.Remove(source);
                continue;
            }
            if (list.Count == 0)
            {
                following.Remove(source);
                continue;
            }
            var at = SoundPlace.At(source.Position, Forward(source));         // a caster's spray, its beam: they go with it
            foreach (var cue in list)
            {
                Sounds.Place(cue, at);
            }
        }
    }

    /// <summary>A scene's reverb: out while its transition fades out, then the new scene's room and mix.</summary>
    private void StepScene()
    {
        var stage = level.Level.Stage;
        if (stage.Transition is { Out: true } && !fadingOut)
        {
            fadingOut = true;
            Sounds.FadeReverb();
        }
        if (level.Current is { } current && current.Key != scene)
        {
            (scene, fadingOut) = (current.Key, false);
            Sounds.ClearMusicFocus();
            var settings = current.Definition.Settings;
            Sounds.SetRoom(settings?.RoomType ?? Content.Levels.RoomType.None, settings?.ReverbMix ?? 0);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        world.Entities.Cue -= OnCue;
        Sounds.StopAll(immediate: false);
        StopSceneSounds(immediate: false);
    }
}
