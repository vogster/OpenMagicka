using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Time Warp: for 15 s the whole level runs at half speed, easing in and out (MAGICKS.md §5.4). One at a time.</summary>
public sealed class TimeWarp() : Magick(MagickType.TimeWarp, SelfClip)
{
    public const string WarpSound = "magick_timewarp";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity || manager.Entities.OfType<TimeWarpEffect>().Any(t => !t.IsDead))
        {
            return false;                                                     // as the original, silently
        }
        manager.Add(new TimeWarpEffect(caster));
        caster.RaiseCue(new SoundCue(WarpSound, MagickEffects.SpellsBank, caster.Position, caster.Body.Facing));
        return true;
    }
}

/// <summary>
/// The warp: the level's time multiplier eases toward 0.5 (its gap closing at a rate of 1 per real
/// second), and back toward 1 over the last second; its own time is real time. A simulation object.
/// </summary>
public sealed class TimeWarpEffect : Entity, IMagickEffect
{
    public const float Duration = 15, Slow = 0.5f, Fade = 1;

    private float ttl = Duration;
    private EntityManager? level;

    internal TimeWarpEffect(Character caster) => Caster = caster;

    public Character Caster { get; }

    public override Vector3 Position => Caster.Position;

    public override float Radius => 0;

    public override bool IsDead => ttl <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Nullify ends it.</summary>
    public void End() => ttl = 0;

    public override void Update(float dt)
    {
        if (Manager is not { } manager)
        {
            return;
        }
        if (IsDead)
        {
            manager.TimeMultiplier = 1;
            return;
        }
        var real = dt / (manager.TimeMultiplier * manager.TimeModifier);
        var target = ttl <= Fade ? 1 : Slow;
        ttl -= real;
        manager.TimeMultiplier = Math.Clamp(manager.TimeMultiplier + (target - manager.TimeMultiplier) * real, 0.01f, 1);
        if (IsDead)
        {
            manager.TimeMultiplier = 1;
        }
    }

    protected internal override void OnAdded() => level = Manager;

    /// <summary>However it goes (a level cleared too), the level's time is given back.</summary>
    protected internal override void OnRemoved()
    {
        if (level is not null)
        {
            level.TimeMultiplier = 1;
        }
    }
}

/// <summary>
/// Tractor Pull: everything within 15 is pulled to the caster (MAGICKS.md §5.5): damageable things
/// by an Earth pull of their own mass, the rest by a throw. Shields hide; the ethereal are spared, and
/// items a script put down to stay.
/// </summary>
public sealed class TractorPull() : Magick(MagickType.TractorPull, "cast_area_ground")
{
    public const float Range = 15, Elevation = 0.17453292f * (1 + MathF.PI / 8);

    public const string PullSound = "spell_push_area", Effect = "magick_tractorpull";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return false;
        }
        var at = caster.Position;
        foreach (var entity in manager.Query(at, Range, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, caster) || entity is Character { IsEthereal: true } or WorldItem { IgnoresTractorPull: true })
            {
                continue;
            }
            var distance = Vector3.Distance(at, entity.Position);
            if (!(distance > float.Epsilon))
            {
                continue;
            }
            var toward = (at - entity.Position) with { Y = 0 };
            var strength = 2.5f * MathF.Sqrt(distance / Range);
            var beyond = entity.Position - toward;                            // the pull comes from the far side
            if (entity.IsDamageable && entity is not Missile)
            {
                entity.Damage(new DamageEntry(AttackProperties.Pushed, Elements.Earth, Mass(entity), strength), caster, beyond);
            }
            else
            {
                entity.Impulse(beyond, Elevation, Mass(entity), strength);
            }
        }
        caster.RaiseCue(new CameraShakeCue(at, 1, 0.5f));
        caster.RaiseCue(new SoundCue(PullSound, MagickEffects.SpellsBank, at, caster.Body.Facing, 1));
        return true;
    }

    private static float Mass(Entity entity) => entity switch
    {
        Character c => c.Body.Body.Mass,
        Missile => Missile.Mass,
        WorldItem => WorldItem.Mass,
        Props.PhysicsProp prop => prop.Body.Mass,
        _ => 1,
    };
}

/// <summary>
/// Propp's Magick: a ball of plasma lobbed ahead, spinning, that blasts on what it meets (MAGICKS.md
/// §5.6). As the original, the cast reports no success, and a hit blasts twice.
/// </summary>
public sealed class ProppMagick() : Magick(MagickType.ProppMagick, "cast_spell1")
{
    public const float Radius = 0.6f, BlastRadius = 4, Lifetime = 8, Danger = 30;

    public const string ExplosionEffect = "magick_propp_explosion", ExplosionSound = "magick_propps_plasma_explosion", LoopSound = "magick_propps_plasma_loop";

    public static readonly IReadOnlyList<DamageEntry> Blast =
    [
        new(AttackProperties.Damage, Elements.Arcane, 6000, 1),
        new(AttackProperties.Damage, Elements.Fire, 2000, 1),
        new(AttackProperties.Knockback, Elements.Earth, 200, 3),
    ];

    private static readonly ItemEvent[] Burst =
        [new EffectEvent(ExplosionEffect, Follow: false, WorldAlign: true), new SoundEvent(ExplosionSound, MagickEffects.AdditionalBank, 1), new RemoveEvent(0)];

    public static readonly IReadOnlyList<EventGroup> Events =
    [
        new(new EventCondition(EventTrigger.Collision), [.. Burst, new BlastEvent(Blast, BlastRadius)]),
        new(new EventCondition(EventTrigger.Hit), [.. Burst, new BlastEvent(Blast, BlastRadius), new BlastEvent(Blast, BlastRadius)]),
        new(new EventCondition(EventTrigger.Timer, Time: Lifetime), Burst),
        new(new EventCondition(EventTrigger.Default, Repeat: true), [new SoundEvent(LoopSound, MagickEffects.AdditionalBank, 1)]),
    ];

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        var turn = ((float)caster.NextRandom() - 0.5f) * MathF.PI / 2;
        var way = Vector3.TransformNormal(caster.Body.Facing, Matrix4x4.CreateRotationY(turn));
        var speed = MathF.Sqrt(10 + (float)caster.NextRandom() * 18);
        var velocity = new Vector3(way.X * speed, 18, way.Z * speed);
        var ball = new Missile(caster.World, caster, caster.CastSource + Vector3.UnitY * 0.5f, velocity, Radius, Events, danger: Danger);
        try
        {
            manager.Add(ball);
        }
        catch
        {
            caster.World.Remove(ball.Body);
            throw;
        }
        ball.Start();
        return false;
    }
}
