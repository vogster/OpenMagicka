using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Props;

/// <summary>
/// What a damageable prop takes (docs/engine/ENTITIES_AI.md §8.4), as the original's: a knockback or a
/// push throws one that moves; its resistances count inclusively; a status attack of fire, cold, water
/// or poison gives that status (whatever the template's status flag says, as the original); lightning
/// on a wet prop hurts threefold, life thins its poison, and a frozen one shatters (physical blows
/// lose 200, then count threefold). The damage is the whole part of amount × magnitude. At no hit
/// points it breaks: its death trigger, its death events, its pieces.
/// </summary>
public sealed partial class PhysicsProp
{
    private const float KnockbackElevation = 0.6980619f, PushElevation = 0.17453292f;

    private bool broken;

    public float HitPoints { get; private set; }

    public StatusSet Statuses { get; private set; } = StatusSet.Empty;

    /// <summary>The script trigger its breaking sets off (with no sender, as the original); 0 for none.</summary>
    public int OnDeathTrigger { get; set; }

    /// <summary>The script trigger each hit that hurts it sets off (with no sender); 0 for none.</summary>
    public int OnDamageTrigger { get; set; }

    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if (!Template.IsDamageable || IsDead)
        {
            return DamageResult.None;
        }
        var result = Throw(damage, attackPosition);
        damage = Resistances.ApplyInclusive(damage, Template.Resistances);
        if (damage.Attack.HasFlag(AttackProperties.Status))
        {
            result |= Status(damage, Elements.Fire, StatusEffects.Burning) | Status(damage, Elements.Cold, StatusEffects.Cold)
                | Status(damage, Elements.Water, StatusEffects.Wet) | Status(damage, Elements.Poison, StatusEffects.Poisoned);
        }
        var amount = damage.Amount;
        if (damage.Attack.HasFlag(AttackProperties.Damage))
        {
            amount = Hurt(damage);
            result |= amount > 0 ? DamageResult.Damaged : amount < 0 ? DamageResult.Healed : DamageResult.Deflected;
            if (damage.Attack.HasFlag(AttackProperties.Piercing) && damage.Magnitude > 0 && amount > 0)
            {
                result |= DamageResult.Pierced;
            }
        }
        HitPoints = MathF.Min(HitPoints, Template.MaxHitPoints);
        if (amount == 0)
        {
            result |= DamageResult.Deflected;                                // as the original: a blow of no amount (a status alone) too
        }
        if (HitPoints <= 0)
        {
            result |= DamageResult.Killed;
            Break();
        }
        if (result.HasFlag(DamageResult.Damaged) && OnDamageTrigger != 0)
        {
            Manager?.RaiseScriptTrigger(this, OnDamageTrigger);
        }
        return result;
    }

    /// <summary>A knockback (40°) or else a push (10°) throws a prop that moves; either is reported if it would.</summary>
    private DamageResult Throw(DamageEntry damage, Vector3 attackPosition)
    {
        var (knock, push) = (damage.Attack.HasFlag(AttackProperties.Knockback), damage.Attack.HasFlag(AttackProperties.Pushed));
        if ((!knock && !push) || Body.IsRemoved || world.IsDisposed)
        {
            return DamageResult.None;
        }
        var velocity = DamageRules.PushVelocity(attackPosition, Position, knock ? KnockbackElevation : PushElevation, damage.Amount, damage.Magnitude,
            Body.Mass, MathF.Abs(world.Settings.Gravity.Y));
        if (velocity.LengthSquared() <= 1e-6f)
        {
            return DamageResult.None;
        }
        if (Template.Movable)
        {
            Body.Velocity += velocity;
        }
        return knock ? DamageResult.KnockedBack : DamageResult.Pushed;
    }

    private DamageResult Status(DamageEntry damage, Elements element, StatusEffects type)
    {
        if ((damage.Element & element) != element)
        {
            return DamageResult.None;
        }
        var change = Statuses.Add(StatusEffect.Create(type, damage.Amount, damage.Magnitude), new Defences(Template.Resistances));
        Statuses = change.Set;
        return change.Outcome switch
        {
            StatusOutcome.Added => DamageResult.StatusAdded,
            StatusOutcome.Removed => DamageResult.StatusRemoved,
            _ => DamageResult.None,
        };
    }

    /// <summary>The points a blow takes, after the original's special rules, its hit points down by them.</summary>
    private float Hurt(DamageEntry damage)
    {
        var (amount, magnitude) = (damage.Amount, damage.Magnitude);
        if (damage.Element == Elements.Lightning && Statuses.Has(StatusEffects.Wet))
        {
            amount *= 3;
        }
        if ((damage.Element & Elements.Life) == Elements.Life)
        {
            Statuses = Statuses.ThinPoison(magnitude);
        }
        if ((damage.Element & ElementSets.Physical) != 0 && Statuses.Has(StatusEffects.Frozen))
        {
            (amount, magnitude) = (MathF.Max(amount - 200, 0) * 3, MathF.Max(1, magnitude));
        }
        amount = (int)(amount * magnitude);
        HitPoints -= amount;
        return amount;
    }

    /// <summary>Its statuses wear on and hurt it; a broken one has none.</summary>
    private void UpdateStatuses(float dt)
    {
        if (!Template.IsDamageable)
        {
            return;
        }
        if (IsDead)
        {
            Statuses = Statuses.Cleared();
            return;
        }
        var tick = Statuses.Update(dt, HitPoints, Template.MaxHitPoints);
        Statuses = tick.Set;
        foreach (var damage in tick.Damage)
        {
            HitPoints -= damage.Amount;
        }
        if (HitPoints <= 0)
        {
            Break();
        }
    }

    /// <summary>It breaks, once: its death trigger, its death events, then its pieces fly apart.</summary>
    private void Break()
    {
        if (broken)
        {
            return;
        }
        broken = true;
        if (OnDeathTrigger != 0)
        {
            Manager?.RaiseScriptTrigger(this, OnDeathTrigger);
        }
        Fire(new EventContext(EventTrigger.Death), null);
        if (Template.Gibs.Count > 0)
        {
            Manager?.RaiseCue(this, new GibsCue(Template.Gibs, Position, MathF.Max(Radius, 0.2f), Template.GibTrailEffect, Statuses.Has(StatusEffects.Frozen)));
        }
    }
}
