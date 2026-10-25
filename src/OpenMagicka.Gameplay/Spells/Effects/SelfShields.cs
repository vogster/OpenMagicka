using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>A self shield struck by a blow that hurts: where, for its flash and its burst.</summary>
public sealed record SelfShieldHitCue(Character Owner, SelfShield Shield, Vector3 At) : Animation.AnimationCue;

/// <summary>
/// A self-cast shield: the bubble (pure Shield) or armour (with Earth or Ice) (docs/gameplay/SPELLS.md
/// §5.6). It changes in place as it takes damage.
/// </summary>
public sealed class SelfShield : ISelfShield
{
    public const float BubbleHitPoints = 500, BubbleDecayPerSecond = 100;

    public const float EarthArmourHitPoints = 1500, IceArmourHitPoints = 900;

    /// <summary>Armour makes its wearer this heavy (never pushed).</summary>
    public const float ArmourMass = 1001;

    /// <summary>A physical blow the bubble stops turns into a knockback of this magnitude.</summary>
    public const float BubbleKnockbackMagnitude = 0.6980619f;

    private readonly Spell spell;

    public SelfShield(Spell spell)
    {
        this.spell = spell;
        IsArmour = spell.Earth > 0 || spell.Ice > 0;
        HitPoints = !IsArmour ? BubbleHitPoints : spell.Earth > 0 ? EarthArmourHitPoints : IceArmourHitPoints;
        MaxHitPoints = HitPoints;
    }

    public bool IsArmour { get; }

    /// <summary>The spell it was cast of: its armour's Earth and Ice.</summary>
    public Spell Spell => spell;

    public float HitPoints { get; private set; }

    public float MaxHitPoints { get; }

    /// <summary>How long since a blow last took from it (its HUD bar flashes; none yet: infinite).</summary>
    public float TimeSinceDamage { get; private set; } = float.PositiveInfinity;

    public bool IsActive => HitPoints > 0;

    public bool IsSolid => IsArmour;

    public bool IsBubble => !IsArmour && IsActive;

    public DamageResult Absorb(Character owner, ref DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        var physical = (damage.Element & ElementSets.Physical) != 0;
        var hurts = damage.Attack.HasFlag(AttackProperties.Damage);
        if (hurts && damage.Amount * damage.Magnitude > 0)
        {
            owner.RaiseCue(new SelfShieldHitCue(owner, this, attackPosition));
        }
        if (!IsArmour)
        {
            if (hurts || damage.Attack.HasFlag(AttackProperties.Status))
            {
                if (damage.Amount * damage.Magnitude >= 0)
                {
                    Take(damage.Amount * damage.Magnitude);
                }
                damage = physical ? damage with { Attack = AttackProperties.Knockback, Magnitude = BubbleKnockbackMagnitude } : damage with { Attack = 0 };
            }
            return DamageResult.Deflected;
        }
        if (hurts)
        {
            if (physical)
            {
                var flat = spell.Earth > 0 && spell.Ice <= 0 ? 10 : 70;
                damage = damage with { Amount = (damage.Amount - flat) * (1 - (0.05f * spell.Ice + 0.15f * spell.Earth)) };
            }
            var through = damage.Amount * damage.Magnitude - HitPoints;
            if (damage.Amount * damage.Magnitude >= 0)
            {
                Take(damage.Amount * damage.Magnitude);
            }
            if (through > 0)
            {
                damage = damage with { Amount = through };
                return DamageResult.Hit;
            }
            damage = damage with { Amount = 0, Attack = 0 };
            return DamageResult.Deflected;
        }
        if ((damage.Attack & AttackProperties.Knockback) != 0)
        {
            damage = damage with { Amount = 0, Magnitude = 0 };
        }
        return DamageResult.None;
    }

    /// <summary>Damage over time comes off the shield and, as the original, still reaches the wearer; armour lets no healing through and feels fire more with Ice.</summary>
    public float AbsorbOverTime(float amount, Elements element)
    {
        if (!IsArmour)
        {
            if (amount >= 0 || (element & Elements.Shield) != 0)
            {
                Take(amount);
            }
            return amount;
        }
        if (amount < 0)
        {
            return 0;
        }
        if ((element & Elements.Fire) != 0)
        {
            amount *= 1 + spell.Ice;
        }
        Take(amount);
        return amount;
    }

    private void Take(float amount)
    {
        HitPoints -= amount;
        if (amount > 0)
        {
            TimeSinceDamage = 0;
        }
    }

    public void Update(Character owner, float dt)
    {
        TimeSinceDamage += dt;
        HitPoints = MathF.Min(HitPoints, MaxHitPoints);
        if (IsArmour)
        {
            owner.Body.SpeedMultiplier = 0.9f - (spell.Earth * 0.075f + spell.Ice * 0.025f);
        }
        else
        {
            HitPoints -= dt * BubbleDecayPerSecond;
        }
        if (HitPoints <= 0 && ReferenceEquals(owner.SelfShield, this))
        {
            owner.RemoveSelfShield();
        }
    }
}
