using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Conflagration: a wall of fire sweeps 20 ahead in a second, burning everything it passes once (MAGICKS.md §4.4).</summary>
public sealed class Conflagration() : Magick(MagickType.Conflagration, SweepClip)
{
    public const string CastSound = "magick_conflagration";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        var sweep = new ConflagrationSweep(caster, caster.Position, caster.Body.Facing);
        manager.Add(sweep);
        manager.RaiseCue(sweep, new SoundCue(CastSound, MagickEffects.SpellsBank, sweep.Position, Vector3.UnitX));
        manager.RaiseCue(sweep, new WorldEffectCue(ConflagrationSweep.Effect, sweep.Position, caster.Body.Facing));
        return true;
    }
}

/// <summary>
/// The sweeping wall: it starts 3 ahead of where it was cast and runs on by 20·t² over its second,
/// a box 11 wide and 2 deep and high, along its way. A simulation object.
/// </summary>
public sealed class ConflagrationSweep : Entity, IMagickEffect
{
    public const float Lifetime = 1, Reach = 20, Lead = 3, Query = 10;

    /// <summary>Half the box: across, up, along.</summary>
    public static readonly Vector3 HalfBox = new(5.5f, 1, 1);

    public const string Effect = "magick_conflagration";

    public static readonly IReadOnlyList<DamageEntry> Burn =
        [new(AttackProperties.Damage, Elements.Fire, 600, 1), new(AttackProperties.Status, Elements.Fire, 75, 2)];

    private readonly HashSet<Entity> hit = new(ReferenceEqualityComparer.Instance);
    private readonly Vector3 start;
    private readonly Vector3 origin;
    private readonly Vector3 way;
    private float time;

    public ConflagrationSweep(Character? owner, Vector3 from, Vector3 facing)
    {
        var flat = facing with { Y = 0 };
        way = flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
        (Owner, start, origin) = (owner, from, from + way * Lead);
    }

    public Character? Owner { get; }

    /// <summary>Where the wall is now.</summary>
    public override Vector3 Position => origin + way * (time * time * Reach);

    public override float Radius => HalfBox.X;

    public override bool IsDead => time >= Lifetime;

    public override bool IsRemovable => IsDead;

    /// <summary>Nullify ends it.</summary>
    public void End() => time = Lifetime;

    public override void Update(float dt)
    {
        time += dt;
        if (Manager is not { } manager)
        {
            return;
        }
        var front = Position;
        var across = Vector3.Normalize(Vector3.Cross(way, Vector3.UnitY));
        foreach (var entity in manager.Query(front, Query))
        {
            if (!entity.IsDamageable || ReferenceEquals(entity, Owner) || hit.Contains(entity) || entity is Character { IsEthereal: true })
            {
                continue;
            }
            var off = entity.Position - front;
            var local = new Vector3(Vector3.Dot(off, across), off.Y, Vector3.Dot(off, way));
            var nearest = Vector3.Clamp(local, -HalfBox, HalfBox);
            var path = new Segment(start, entity.Position - start);
            if (Vector3.DistanceSquared(local, nearest) > entity.Radius * entity.Radius
                || manager.Shields.OfType<Entity>().Any(s => s.SegmentIntersect(path, 1)))
            {
                continue;
            }
            foreach (var burn in Burn)
            {
                entity.Damage(burn, Owner, origin);
            }
            hit.Add(entity);
        }
    }
}
