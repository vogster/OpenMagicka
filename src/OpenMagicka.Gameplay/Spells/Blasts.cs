using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// A blast for the renderer: an element's burst (or the arcane blast) and its decals, reaching as far
/// as <see cref="Reach"/> says in each of eight directions (a share of the radius).
/// </summary>
public sealed record BlastCue(Elements Element, Vector3 Position, float Radius, float Magnitude, bool Ground, bool Arcane, IReadOnlyList<float> Reach) : AnimationCue;

/// <summary>Blasts and arc strikes of spells in the world (docs/gameplay/SPELLS.md §5.8, §5.2).</summary>
public static class Blasts
{
    /// <summary>The directions a blast's visuals are cut in.</summary>
    public const int Directions = 8;

    /// <summary>
    /// A full blast: everything damageable within the radius, in line of sight, takes the damage with
    /// the falloff (none for Arcane or Life); the camera shakes radius/3 for 0.3 s.
    /// </summary>
    /// <param name="source">What the cues come from when there is no owner (a missile, a barrel); with neither, none are raised.</param>
    public static DamageResult Full(EntityManager manager, PhysicsWorld world, Entity? owner, Entity? ignored, float radius, Vector3 at, IReadOnlyList<DamageEntry> damage,
        Entity? source = null)
    {
        Check(manager, world, damage, radius, at);
        var elements = All(damage);
        var reach = Reach(manager, world, radius, at, cutByLevel: true);
        if ((elements & ElementSets.Beams) != 0)
        {
            Raise(manager, source ?? owner, new BlastCue(elements, at, radius, damage.Max(d => d.Magnitude), Ground: false, Arcane: true, reach));
        }
        else
        {
            foreach (var entry in damage.Where(d => d.Element != Elements.None))
            {
                Raise(manager, source ?? owner, new BlastCue(entry.Element, at, radius, entry.Magnitude, Ground: false, Arcane: false, reach));
            }
        }
        Raise(manager, source ?? owner, new CameraShakeCue(at, BlastRules.FullShake(radius), BlastRules.FullShakeDuration));
        return Damage(manager, world, owner, ignored, at, radius, damage, falloff: true, ground: false);
    }

    /// <summary>A ground blast: no falloff, the levitating spared; the camera shakes 2 for 0.9 s.</summary>
    public static DamageResult Ground(EntityManager manager, PhysicsWorld world, Entity? owner, Entity? ignored, float radius, Vector3 at, IReadOnlyList<DamageEntry> damage,
        Entity? source = null)
    {
        Check(manager, world, damage, radius, at);
        var reach = Reach(manager, world, radius, at, cutByLevel: false);
        foreach (var entry in damage.Where(d => d.Element != Elements.None))
        {
            Raise(manager, source ?? owner, new BlastCue(entry.Element, at, radius, entry.Magnitude, Ground: true, Arcane: false, reach));
        }
        Raise(manager, source ?? owner, new CameraShakeCue(at, BlastRules.GroundShake, BlastRules.GroundShakeDuration));
        return Damage(manager, world, owner, ignored, at, radius, damage, falloff: false, ground: true);
    }

    /// <summary>
    /// An arc strike (a weapon cast): everything damageable within the range and within the spread of
    /// the direction takes each entry × <c>1 − (distance − radius)/range</c>, with no line of sight.
    /// </summary>
    public static DamageResult Arc(EntityManager manager, Entity? owner, Entity? ignored, Vector3 origin, Vector3 direction, float range, float spread, IReadOnlyList<DamageEntry> damage)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(damage);
        if (!(range > 0))
        {
            return DamageResult.None;
        }
        var cos = MathF.Cos(MathF.Min(spread, MathF.PI));
        var result = DamageResult.None;
        foreach (var entity in manager.Query(origin, range, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, ignored) || !entity.IsDamageable || entity is Character { IsEthereal: true })
            {
                continue;
            }
            var flat = (entity.Position - origin) with { Y = 0 };
            var length = flat.Length();
            if (length >= 1e-6f && Vector3.Dot(flat / length, direction) < cos)
            {
                continue;
            }
            var share = 1 - (Vector3.Distance(origin, entity.Position) - entity.Radius) / range;
            if (share > 0)
            {
                result |= entity.Damage([.. damage.Select(d => d with { Magnitude = d.Magnitude * share })], owner, origin);
            }
        }
        return result;
    }

    /// <summary>How far, as a share of the radius, the blast reaches in each direction before the level (unless ignored) or a shield cuts it.</summary>
    public static float[] Reach(EntityManager manager, PhysicsWorld world, float radius, Vector3 at, bool cutByLevel)
    {
        var reach = new float[Directions];
        for (var i = 0; i < Directions; i++)
        {
            var angle = i * MathF.Tau / Directions;
            var end = at + new Vector3(MathF.Cos(angle), 0, -MathF.Sin(angle)) * radius;
            reach[i] = 1;
            if (!cutByLevel)
            {
                continue;
            }
            if (world.CastSegment(at, end, levelOnly: true) is { } wall)
            {
                reach[i] = wall.Fraction;
            }
            foreach (var shield in manager.Shields)
            {
                if (shield.Blocks(at, end, 0, out var hit) && radius > 0)
                {
                    reach[i] = MathF.Min(reach[i], Vector3.Distance(at, hit) / radius);
                }
            }
        }
        return reach;
    }

    private static DamageResult Damage(EntityManager manager, PhysicsWorld world, Entity? owner, Entity? ignored, Vector3 at, float radius, IReadOnlyList<DamageEntry> damage,
        bool falloff, bool ground)
    {
        var beams = (All(damage) & ElementSets.Beams) != 0;
        var result = DamageResult.None;
        foreach (var entity in manager.Query(at, radius, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, ignored) || !entity.IsDamageable
                || entity is Character { IsEthereal: true } || (ground && entity is Character { Body.Conditions.IsLevitating: true }))
            {
                continue;
            }
            var line = new Segment(at, entity.Position - at);
            var surface = entity.Entry(line) is { } entry ? line with { Delta = line.Delta * entry } : line;
            if (surface.Delta.LengthSquared() > 1e-12f && world.CastSegment(at, at + surface.Delta, levelOnly: true) is not null)
            {
                continue;
            }
            var distance = surface.Delta.Length() - entity.Radius;                 // the radius comes off twice, as the original
            if (distance > radius)
            {
                continue;
            }
            var scale = falloff && !beams ? BlastRules.Falloff(MathF.Max(distance, 0), radius, arcaneOrLife: false) : 1;
            result |= entity.Damage([.. damage.Select(d => d with { Magnitude = d.Magnitude * scale })], owner, at);
        }
        return result;
    }

    private static Elements All(IReadOnlyList<DamageEntry> damage) => damage.Aggregate(Elements.None, (all, d) => all | d.Element);

    private static void Raise(EntityManager manager, Entity? source, AnimationCue cue)
    {
        if (source is Character character)
        {
            character.RaiseCue(cue);
        }
        else if (source is not null)
        {
            manager.RaiseCue(source, cue);
        }
    }

    private static void Check(EntityManager manager, PhysicsWorld world, IReadOnlyList<DamageEntry> damage, float radius, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(damage);
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "a blast needs a finite radius and place");
        }
    }
}
