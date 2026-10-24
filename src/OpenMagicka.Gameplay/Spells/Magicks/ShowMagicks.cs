using System.Numerics;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Performance Enchantment: 10 s of speed, half damage taken and doubled spells; then a slowing
/// withdrawal over the last 4 s (MAGICKS.md §4.7).
/// </summary>
public sealed class PerformanceEnchantment() : Magick(MagickType.PerformanceEnchantment, SelfClip)
{
    public const float Duration = 10, Withdrawal = 4;

    public const string BuffSound = "woot_magick_enchantment_buff", NerfSound = "woot_magick_enchantment_debuff", Effect = "special_woot_performance";

    public override bool Execute(Character caster)
    {
        caster.RaiseCue(new Animation.SoundCue(BuffSound, MagickEffects.AdditionalBank, caster.Position, caster.Body.Facing));
        caster.RaiseCue(new Animation.ItemEffectEndCue(caster.EnchantmentLook));
        caster.RaiseCue(new Animation.ItemEffectCue(Effect, caster.Position, caster.Body.Facing, Follow: true, caster.EnchantmentLook));
        if (caster.IsEnchanted)
        {
            caster.Enchant(Duration);                                         // as the original, the auras are not renewed
            return true;
        }
        caster.Enchant(Duration);
        caster.AddSelfCastAura(new BuffAura(AuraTarget.Self, Duration, 0, new ResistanceBuff(1, new Resistance(Elements.All, Multiplier: 0.5f))));
        caster.AddSelfCastAura(new BuffAura(AuraTarget.Self, Duration, 0, new BoostDamageBuff(1, new DamageEntry(AttackProperties.Damage, Elements.All, 2, 2))));
        return true;
    }
}

/// <summary>
/// Amalgameddon: the first two characters of one kind within 20 rise, are pushed apart and smashed
/// together; one is overkilled and the other gains its hit points (MAGICKS.md §4.8). One at a time;
/// it fails without a pair.
/// </summary>
public sealed class Amalgameddon() : Magick(MagickType.Amalgameddon, GlobalClip)
{
    public const float Range = 20;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity
            || manager.Entities.OfType<Amalgamation>().Any(a => !a.IsDead))
        {
            return Fail(caster);
        }
        var near = manager.Query(caster.Position, Range).OfType<Character>().Where(c => !ReferenceEquals(c, caster) && !c.IsDead).ToList();
        foreach (var first in near)
        {
            if (near.FirstOrDefault(c => !ReferenceEquals(c, first) && c.Template.Id == first.Template.Id) is { } second)
            {
                manager.Add(new Amalgamation(first, second));
                return true;
            }
        }
        return Fail(caster);
    }
}

/// <summary>
/// The pair's fate, step by step as the original: they rise 0.015 a frame (stunned) until the first
/// is higher than 5, are pushed apart 0.25 a frame for 0.2 s, then smashed together 0.6 a frame until
/// they touch. After 10 s it ends whatever happened. A simulation object.
/// </summary>
public sealed class Amalgamation : Entity, IMagickEffect
{
    public const float Lifetime = 10, Rise = 0.015f, Top = 5, Apart = 0.25f, ApartTime = 0.2f, Together = 0.6f, StunTime = 10;

    private enum Stage { Rising, PushingApart, Smashing, Done }

    private readonly Character first;
    private readonly Character second;
    private Stage stage;
    private float ttl = Lifetime;
    private float timer;
    private float life;

    internal Amalgamation(Character first, Character second)
    {
        (this.first, this.second) = (first, second);
        first.Body.Body.AffectedByGravity = false;
        second.Body.Body.AffectedByGravity = false;
    }

    public override Vector3 Position => first.Position;

    public override float Radius => first.Radius;

    public override bool IsDead => stage == Stage.Done;

    public override bool IsRemovable => IsDead;

    public override void Update(float dt)
    {
        ttl -= dt;
        if (ttl < 0 || first.Manager is null || second.Manager is null)
        {
            End();
            return;
        }
        first.Body.Body.AffectedByGravity = false;                            // held, whatever a levitation's end says
        second.Body.Body.AffectedByGravity = false;
        switch (stage)
        {
            case Stage.Rising:
                Move(first, Vector3.UnitY * Rise);
                Move(second, Vector3.UnitY * Rise);
                first.Stun(StunTime);
                second.Stun(StunTime);
                if (first.Position.Y > Top)
                {
                    (stage, timer, life) = (Stage.PushingApart, ApartTime, second.HitPoints);
                }
                break;
            case Stage.PushingApart:
                timer -= dt;
                var apart = Away() * Apart;
                Move(first, apart);
                Move(second, -apart);
                if (timer < 0)
                {
                    stage = Stage.Smashing;
                }
                break;
            case Stage.Smashing:
                var together = Away() * -Together;
                Move(first, together);
                Move(second, -together);
                if (Vector3.Distance(first.Position, second.Position) < first.Radius + second.Radius)
                {
                    second.OverKill();
                    first.Heal(life);
                    first.Unstun();
                    End();
                }
                break;
        }
    }

    private Vector3 Away()
    {
        var away = first.Position - second.Position;
        return away.LengthSquared() > 1e-12f ? Vector3.Normalize(away) : Vector3.UnitX;
    }

    private static void Move(Character character, Vector3 by)
    {
        if (!character.World.IsDisposed && !character.World.IsStepping)
        {
            character.Body.Body.Position = character.Position + by;
        }
    }

    /// <summary>Ends it (Nullify too): their gravity comes back.</summary>
    public void End()
    {
        if (stage == Stage.Done)
        {
            return;
        }
        stage = Stage.Done;
        foreach (var unit in new[] { first, second })
        {
            if (!unit.World.IsDisposed && !unit.Body.Body.IsRemoved)
            {
                unit.Body.Body.AffectedByGravity = !unit.Template.Flies && !unit.IsLevitating;
            }
        }
    }
}

/// <summary>
/// Judgement Spray: for a second, 20 cold missiles fly out from the staff, left and right in turn,
/// homing on the character in front nearest to 12 away (MAGICKS.md §4.9).
/// </summary>
public sealed class JudgementSpray() : Magick(MagickType.JudgementSpray, GlobalClip)
{
    public const string FireSound = "woot_magick_judgment_fire";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        manager.Add(new JudgementVolley(caster));
        caster.RaiseCue(new Animation.SoundCue(FireSound, MagickEffects.AdditionalBank, caster.Position, caster.Body.Facing));
        return true;
    }
}

/// <summary>The volley under way. A simulation object.</summary>
public sealed class JudgementVolley : Entity, IMagickEffect
{
    public const float Lifetime = 1, Range = 20, Optimal = 12, Speed = 30, Homing = 0.55f, MissileTime = 2, MissileRadius = 0.25f, MissileDanger = 30;

    public const int Count = 20;

    public const string HitEffect = "magick_judgementspray_hit", HitSound = "wep_handgrenade_explode", Trail = "magick_judgementspray_trail", Look = "magick_judgementspray_missile";

    /// <summary>What a missile does when it ends, however it ends: cold, then a cold status.</summary>
    public static readonly IReadOnlyList<ItemEvent> Burst =
    [
        new EffectEvent(HitEffect, Follow: false, WorldAlign: true),
        new SoundEvent(HitSound, MagickEffects.AdditionalBank, 1),
        new DamageEvent(new DamageEntry(AttackProperties.Damage, Elements.Cold, 273, 1)),
        new DamageEvent(new DamageEntry(AttackProperties.Status, Elements.Cold, 100, 1)),
        new RemoveEvent(0),
    ];

    public static readonly IReadOnlyList<EventGroup> Events =
    [
        new(new EventCondition(EventTrigger.Damaged, HitPoints: 20, Elements: Elements.Fire | Elements.Lightning | Elements.Arcane), Burst),
        new(new EventCondition(EventTrigger.Hit), Burst),
        new(new EventCondition(EventTrigger.Default, Repeat: true), [new EffectEvent(Trail, Follow: true, WorldAlign: false), new EffectEvent(Look, Follow: true, WorldAlign: false)]),
        new(new EventCondition(EventTrigger.Collision), Burst),
        new(new EventCondition(EventTrigger.Timer, Time: MissileTime), Burst),
    ];

    private float ttl = Lifetime;
    private float interval;
    private bool left;

    internal JudgementVolley(Character caster) => Caster = caster;

    public Character Caster { get; }

    public override Vector3 Position => Caster.Position;

    public override float Radius => Range;

    public override bool IsDead => ttl <= 0 || Caster.Manager is null;

    public override bool IsRemovable => IsDead;

    /// <summary>Nullify ends it.</summary>
    public void End() => ttl = 0;

    public override void Update(float dt)
    {
        ttl -= dt;
        interval -= dt;
        if (IsDead || interval >= 0 || Manager is not { } manager || Caster.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity)
        {
            return;
        }
        interval = Lifetime / Count;
        var up = Vector3.Normalize(Caster.Body.Facing with { Y = 1 });
        left = !left;
        var side = Vector3.Cross(left ? Vector3.UnitY : -Vector3.UnitY, up);
        var velocity = side * (Speed / 2 + (0.5f + (float)Caster.NextRandom()) * Speed / 2);
        var missile = new Missile(Caster.World, Caster, Caster.CastSource, velocity, MissileRadius, Events, Target(manager), Homing, MissileDanger, facesVelocity: false);
        try
        {
            manager.Add(missile);
        }
        catch
        {
            Caster.World.Remove(missile.Body);
            throw;
        }
        missile.Start();
    }

    /// <summary>
    /// The character in front (its way to the caster within (π/2, 5π/4) of the facing) whose distance
    /// is nearest to 12; as the original, the best is kept signed, so one farther than 12 sets a bar
    /// the nearer ones cannot beat.
    /// </summary>
    private Entity? Target(EntityManager manager)
    {
        Entity? target = null;
        var best = float.MaxValue;
        foreach (var entity in manager.Query(Caster.Position, Range))
        {
            if (entity is not Character || ReferenceEquals(entity, Caster))
            {
                continue;
            }
            var back = Caster.Position - entity.Position;
            if (back.LengthSquared() <= 1e-12f)
            {
                continue;
            }
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(back), Caster.Body.Facing), -1, 1));
            var off = Optimal - Vector3.Distance(entity.Position, Caster.Position);
            if (angle is > MathF.PI / 2 and < 1.25f * MathF.PI && MathF.Abs(off) < best)
            {
                (target, best) = (entity, off);
            }
        }
        return target;
    }
}
