using System.Collections.Concurrent;
using System.Numerics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;

namespace OpenMagicka.Game.Play;

/// <summary>What the renderer is told to do with a visual effect, or a lone particle (a tracer).</summary>
public enum EffectCommandKind : byte { Start, Move, Stop, Particle }

/// <summary>An effect started (its name), moved or stopped, by its number; a start or a move gives its place. A lone particle is given whole.</summary>
public readonly record struct EffectCommand(EffectCommandKind Kind, long Id, string? Effect, Matrix4x4 World, Engine.Particles.ParticleSpawn Particle = default);

/// <summary>
/// The effects passed between the threads: the logic thread's commands to the renderer, in order, and the
/// numbers of the effects that played out, back. Frames may be skipped; these are not.
/// </summary>
public sealed class EffectChannel
{
    public ConcurrentQueue<EffectCommand> Commands { get; } = new();

    public ConcurrentQueue<long> Ended { get; } = new();
}

/// <summary>
/// The level's visual effects on the logic thread (OM-75): each tick the scene's named effects (started,
/// stopped, following their parts, a beam scaled to its reach; one that played out is told to the scene)
/// and the gameplay's effect cues (a one-shot at a point; one following a bone, an item, a missile or a
/// prop until its end cue or its source is gone) become the renderer's commands. A broken prop's pieces
/// fly off as debris (<see cref="Gib"/>) trailing their effect, and a camera shake shakes the stage's
/// camera. Sounds go to OM-51, decals to OM-66; the spells' own pictures are <see cref="SpellVisuals"/>'.
/// </summary>
public sealed class EffectDirector : IDisposable
{
    private readonly GameWorld world;
    private readonly PlayedLevel level;
    private readonly EffectChannel channel;
    private readonly Func<string, (Vector3 Centre, float Radius)?> gibShape;
    private readonly Random random = new(1);
    private readonly List<(Entity Source, AnimationCue Cue)> cues = [];
    private readonly Dictionary<object, (long Id, Func<Matrix4x4?> Where)> following = [];   // by source: an entity, an item, a BoneKey, a PropKey
    private readonly Dictionary<long, object> followingIds = [];
    private readonly Dictionary<SceneEffect, (long Id, int Starts)> scene = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<long, (ScriptScene Scene, int Key)> sceneIds = [];
    private long nextId;
    private ScriptScene? shown;

    /// <param name="gibShape">A gib model's bounding sphere, by its asset; null for one there is none of.</param>
    public EffectDirector(GameWorld world, PlayedLevel level, EffectChannel channel, Func<string, (Vector3 Centre, float Radius)?> gibShape)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.level = level ?? throw new ArgumentNullException(nameof(level));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.gibShape = gibShape ?? throw new ArgumentNullException(nameof(gibShape));
        world.Entities.Cue += OnCue;
    }

    /// <summary>The effects running now that follow something or belong to the scene.</summary>
    public int Running => following.Count + scene.Count;

    private void OnCue(Entity source, AnimationCue cue) => cues.Add((source, cue));

    /// <summary>A tick, after the level's: the ended effects told, the cues played, the followers moved, the scene's effects matched.</summary>
    public void Update()
    {
        while (channel.Ended.TryDequeue(out var ended))
        {
            if (followingIds.Remove(ended, out var key))
            {
                following.Remove(key);                                        // it played out by itself
            }
            if (sceneIds.Remove(ended, out var named) && ReferenceEquals(named.Scene, level.Current?.Scripts))
            {
                named.Scene.Effects.Ended(named.Key);
            }
        }
        foreach (var (source, cue) in cues)
        {
            Play(source, cue);
        }
        cues.Clear();
        foreach (var (key, (id, where)) in following.ToList())
        {
            if (where() is { } at)
            {
                Send(EffectCommandKind.Move, id, null, at);
            }
            else
            {
                Stop(key);
            }
        }
        MatchScene(level.Current?.Scripts);
    }

    private void Play(Entity source, AnimationCue cue)
    {
        switch (cue)
        {
            case WorldEffectCue world:
                Start(null, world.Effect, () => Facing(world.Position, world.Direction));
                break;
            case ItemEffectCue { Follow: false } once:
                Start(null, once.Effect, () => Facing(once.Position, once.Direction));
                break;
            case ItemEffectCue item:
                Start(new ItemKey(item.Source, item.Effect), item.Effect, Follower(item.Source, item.Position, item.Direction));   // a spray of fire and steam shows both
                break;
            case ItemEffectEndCue end:
                foreach (var key in following.Keys.OfType<ItemKey>().Where(k => ReferenceEquals(k.Source, end.Source)).ToList())
                {
                    Stop(key);
                }
                break;
            case EffectCue { Attached: false } bone when source is Character character:
                Start(null, bone.Effect, Bone(character, bone.Bone));
                break;
            case EffectCue bone when source is Character character:
                Start(new BoneKey(character, bone.Source), bone.Effect, Bone(character, bone.Bone));
                break;
            case EffectEndCue end when source is Character character:
                Stop(new BoneKey(character, end.Source));
                break;
            case PropEffectCue prop:
                Start(new PropKey(prop.Prop, prop.Effect), prop.Effect.Effect,
                    () => prop.Prop.IsDead || prop.Prop.Manager is null ? null : prop.Effect.Transform * prop.Prop.Transform);
                break;
            case PropEffectsEndCue end:
                foreach (var key in following.Keys.OfType<PropKey>().Where(k => ReferenceEquals(k.Prop, end.Prop)).ToList())
                {
                    Stop(key);
                }
                break;
            case GibsCue gibs:
                Shatter(gibs);
                break;
            case CameraShakeCue shake:
                var stage = level.Level.Stage;
                stage.Camera = stage.Camera.Shake(shake.Magnitude, shake.Duration);
                break;
        }
    }

    /// <summary>
    /// Where a following item's effect is: on its missile, at its item's tip, on the entity, with its spray (its spot on
    /// the caster, turned as the caster turns); none once the missile, the entity or the spray is gone. Another source
    /// (an item's passive) cannot be followed: its effect stays where it began, until its end cue or the scene's end.
    /// </summary>
    private static Func<Matrix4x4?> Follower(object source, Vector3 position, Vector3 direction) => source switch
    {
        Gameplay.Spells.Effects.SprayEffect spray => () => spray.Place is var (at, facing) ? Facing(at, facing) : null,
        Missile missile => () => missile.IsDead || missile.Manager is null ? null : Facing(missile.Position, missile.Velocity),
        Item item => () => Facing(item.Tip, direction),
        Entity entity => () => entity.IsRemovable || entity.Manager is null ? null : Facing(entity.Position, direction),
        _ => () => Facing(position, direction),
    };

    /// <summary>An effect that follows its place until the place is gone (null), under a key a new one of the same replaces.</summary>
    public void Follow(object key, string effect, Func<Matrix4x4?> where)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(where);
        Start(key, effect, where);
    }

    /// <summary>An effect started at a place and left to play out.</summary>
    public void Once(string effect, Matrix4x4 at) => Start(null, effect, () => at);

    /// <summary>The effect under a key stops (its particles fly on).</summary>
    public void Unfollow(object key) => Stop(key ?? throw new ArgumentNullException(nameof(key)));

    /// <summary>A lone particle into the pool (a tracer).</summary>
    public void Spawn(Engine.Particles.ParticleSpawn particle) => channel.Commands.Enqueue(new EffectCommand(EffectCommandKind.Particle, 0, null, default, particle));

    /// <summary>The level went to a scene, or back to a checkpoint: what followed things of the old world stops.</summary>
    public void Clear()
    {
        foreach (var key in following.Keys.ToList())
        {
            Stop(key);
        }
    }

    private static Func<Matrix4x4?> Bone(Character character, string bone)
    {
        var index = character.Skeleton?.IndexOf(bone) ?? -1;
        return () => character.Manager is null ? null : index >= 0 ? character.BoneWorld(index) : character.RootMatrix();
    }

    /// <summary>A clip's effect on a character's bone, by the clip's action.</summary>
    private sealed record BoneKey(Character Character, AnimationAction Source);

    /// <summary>One of a prop's own effects.</summary>
    private sealed record PropKey(PhysicsProp Prop, PropEffect Effect);

    /// <summary>A following item's effect: each of a source's effects is its own (a new one of the same name replaces it).</summary>
    private sealed record ItemKey(object Source, string Effect);

    private void Start(object? key, string effect, Func<Matrix4x4?> where)
    {
        if (string.IsNullOrEmpty(effect) || where() is not { } at)
        {
            return;
        }
        if (key is not null)
        {
            Stop(key);                                                        // a new one of the same source replaces it
        }
        var id = ++nextId;
        Send(EffectCommandKind.Start, id, effect, at);
        if (key is not null)
        {
            following[key] = (id, where);
            followingIds[id] = key;
        }
    }

    private void Stop(object key)
    {
        if (following.Remove(key, out var running))
        {
            followingIds.Remove(running.Id);
            Send(EffectCommandKind.Stop, running.Id, null, default);
        }
    }

    /// <summary>
    /// The scene's named effects as they are: a new or restarted active one starts, a stopped (or gone) one
    /// stops, a running one follows its place; another scene's all stop when the level leaves it.
    /// </summary>
    private void MatchScene(ScriptScene? current)
    {
        if (!ReferenceEquals(current, shown))
        {
            foreach (var (_, (id, _)) in scene)
            {
                Send(EffectCommandKind.Stop, id, null, default);
            }
            scene.Clear();
            sceneIds.Clear();
            shown = current;
        }
        if (current is null)
        {
            return;
        }
        var live = current.Effects.All.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var (effect, (id, _)) in scene.Where(p => !live.Contains(p.Key) || !p.Key.IsActive).ToList())
        {
            Send(EffectCommandKind.Stop, id, null, default);
            scene.Remove(effect);
            sceneIds.Remove(id);
        }
        foreach (var effect in current.Effects.All.Where(e => e.IsActive))
        {
            var at = Matrix4x4.CreateScale(effect.Reach) * effect.World;
            if (scene.TryGetValue(effect, out var running) && running.Starts == effect.Starts)
            {
                Send(EffectCommandKind.Move, running.Id, null, at);
                continue;
            }
            if (scene.Remove(effect, out var old))
            {
                Send(EffectCommandKind.Stop, old.Id, null, default);
                sceneIds.Remove(old.Id);
            }
            var id = ++nextId;
            scene[effect] = (id, effect.Starts);
            sceneIds[id] = (current, effect.Key);
            Send(EffectCommandKind.Start, id, effect.Effect, at);
        }
    }

    /// <summary>A broken prop's pieces, each a gib flying off (one whose model is unknown is left out; a full level makes none).</summary>
    private void Shatter(GibsCue gibs)
    {
        foreach (var gib in gibs.Gibs)
        {
            if (world.Entities.Entities.Count >= EntityManager.Capacity || world.Physics.IsStepping || gibShape(gib.Model) is not { } shape)
            {
                continue;
            }
            var velocity = Gib.Launch(random, gibs.Radius, gib.Mass, out var spin);
            var piece = new Gib(world.Physics, gib.Model, gib.Mass, gib.Scale, shape.Centre, shape.Radius, gibs.Position, velocity, spin,
                Gib.MinLife + random.NextSingle() * Gib.MinLife, gibs.TrailEffect, gibs.Frozen);
            world.Entities.Add(piece);
            if (piece.Trail is { Length: > 0 } trail)
            {
                Start(piece, trail, () => piece.IsDead || piece.Manager is null ? null : Matrix4x4.CreateTranslation(piece.Position));
            }
        }
    }

    private void Send(EffectCommandKind kind, long id, string? effect, Matrix4x4 at) => channel.Commands.Enqueue(new EffectCommand(kind, id, effect, at));

    /// <summary>An effect at a point facing a way (any way when there is none, or straight up or down).</summary>
    internal static Matrix4x4 Facing(Vector3 position, Vector3 direction)
    {
        var forward = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        var up = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        return Matrix4x4.CreateWorld(position, forward, up);
    }

    public void Dispose() => world.Entities.Cue -= OnCue;
}
