using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics.Characters;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

public sealed partial class Character
{
    /// <summary>
    /// One entry of damage, docs/engine/ENTITIES_AI.md §2.2: steps 1–5 here (immunity,
    /// entanglement, self-shield, pushes and knockdowns, blocking), steps 6–10 in
    /// <see cref="DamageRules"/>, then death and the last attacker.
    /// </summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        damage = Boosted(damage, attacker);
        Finite(damage.Amount, nameof(damage));
        Finite(damage.Magnitude, nameof(damage));
        if (!float.IsFinite(attackPosition.X) || !float.IsFinite(attackPosition.Y) || !float.IsFinite(attackPosition.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(attackPosition), attackPosition, "must be finite");
        }
        if (attacker is Character striker && Mind is { } mind && !IsPlayer)
        {
            mind.Grudge(striker, damage.Amount * damage.Magnitude);              // as the original: before the rest, even when dead
        }
        if (Dead || IsImmortal)
        {
            return DamageResult.None;
        }
        RemoveInvisibility();
        if (damage.Attack.HasFlag(AttackProperties.Entanglement) && !Statuses.Has(StatusEffects.Burning))
        {
            if (ReferenceEquals(attacker, this))
            {
                return DamageResult.None;
            }
            Entangle(damage.Magnitude);
            return DamageResult.Hit;
        }
        if (IsEntangled && (damage.Attack.HasFlag(AttackProperties.Damage)
            || (damage.Attack.HasFlag(AttackProperties.Status) && (damage.Element & Elements.Fire) != 0)))
        {
            DecreaseEntanglement(damage.Magnitude, damage.Element);
        }
        var result = DamageResult.None;
        if (SelfShield is { IsActive: true } shield)
        {
            result |= shield.Absorb(this, ref damage, attacker, attackPosition);
        }
        result |= Pushes(damage, attackPosition);
        var blocked = Block(ref damage, attackPosition);
        var outcome = DamageRules.Hit(blocked ? damage with { Attack = damage.Attack & AttackProperties.Status } : damage, AsDamageTarget);
        result |= outcome.Result | (blocked ? DamageResult.Deflected : DamageResult.None);
        Statuses = outcome.Statuses;
        CastInterrupted |= outcome.InterruptsCast;
        if (blocked || !outcome.Landed)
        {
            return result;                                                    // deflected: statuses only
        }
        HitPoints = outcome.HitPoints;
        Consequences(damage, outcome, attacker);
        if (outcome.Result.HasFlag(DamageResult.Killed))
        {
            if (outcome.Result.HasFlag(DamageResult.OverKilled) && (damage.Element & ~Elements.Arcane) != 0)
            {
                OverKill();
            }
            DieUnlessDying();
        }
        return result;
    }

    private DamageTarget AsDamageTarget => new(HitPoints, MaxHitPoints, Statuses, Defences, Template.HitTolerance, Template.BleedRate,
        CanHaveStatus: !IsEthereal, BloodAndGore: BloodAndGore);

    /// <summary>Step 4: a knockback (40°, falls), or a push (10°) and a knockdown by tolerance.</summary>
    private DamageResult Pushes(DamageEntry damage, Vector3 attackPosition)
    {
        if ((damage.Attack & AttackProperties.Knockback) == AttackProperties.Knockback)
        {
            if (!Push(attackPosition, DamageRules.KnockbackElevation, damage))
            {
                return DamageResult.None;
            }
            if (CanBeKnockedDown)
            {
                IsKnockedDown = true;
            }
            return DamageResult.KnockedBack;
        }
        var result = DamageResult.None;
        if (damage.Attack.HasFlag(AttackProperties.Pushed) && Push(attackPosition, DamageRules.PushElevation, damage))
        {
            result |= DamageResult.Pushed;
        }
        if (damage.Attack.HasFlag(AttackProperties.Knockdown))
        {
            KnockDown(damage.Magnitude);
        }
        return result;
    }

    /// <summary>Pushes with the entry's amount as power and magnitude as distance, unless something holds the character.</summary>
    private bool Push(Vector3 attackPosition, float elevation, DamageEntry damage)
    {
        if (IsImmortal || IsEthereal || SelfShield is { IsActive: true, IsSolid: true } || IsEntangled || Dead)
        {
            return false;
        }
        var gravity = MathF.Abs(world.Settings.Gravity.Y);
        var velocity = DamageRules.PushVelocity(attackPosition, Position, elevation, damage.Amount, damage.Magnitude, Body.Body.Mass, gravity);
        if (velocity.LengthSquared() <= 1e-6f)
        {
            return false;
        }
        Body.PushVelocity(velocity);
        return true;
    }

    /// <summary>
    /// Step 5: Earth or Lightning damage from in front, in a block window, against every item's block
    /// value (§8.1.1); true when fully blocked. As in the original, "fully" compares the block with
    /// amount × magnitude, while a partial block comes off the amount.
    /// </summary>
    private bool Block(ref DamageEntry damage, Vector3 attackPosition)
    {
        if (BlockSlot < 0 || !damage.Attack.HasFlag(AttackProperties.Damage) || (damage.Element & (Elements.Earth | Elements.Lightning)) == 0
            || !FacesForBlock(attackPosition))
        {
            return false;
        }
        float blocked = BlockValue;
        if (blocked >= damage.Amount * damage.Magnitude)
        {
            return true;
        }
        damage = damage with { Amount = MathF.Max(0, damage.Amount - blocked) };
        return false;
    }

    private void Consequences(DamageEntry damage, DamageOutcome outcome, Entity? attacker)
    {
        if (outcome.Stunned)
        {
            stunTimer = Template.StunTime;
        }
        if (damage.Attack.HasFlag(AttackProperties.Damage) && (damage.Element & Elements.Lightning) != 0 && zapTimer <= 0)
        {
            zapTimer = ZapTime;
        }
        IsHit |= outcome.Result.HasFlag(DamageResult.Hit);
        if (attacker is not null)
        {
            (LastAttacker, lastAttackerTimer) = (attacker, AttackerMemory);
        }
    }

    /// <summary>
    /// Damage over time from a status (§2.2): no resistances (the DPS has them), fire burns grease
    /// twice as hard, the self-shield takes its share first. Death follows at the end of the update.
    /// </summary>
    private void DamageOverTime(float amount, Elements element)
    {
        if (IsImmortal || Dead)
        {
            return;
        }
        RemoveInvisibility();
        if (SelfShield is { IsActive: true } shield)
        {
            amount = shield.AbsorbOverTime(amount, element);
        }
        HitPoints -= Statuses.Amplify(new StatusDamage(amount, element)).Amount;
    }

    /// <summary>
    /// What the body met during the physics step: an impact hurts as Earth (and fells above 100),
    /// the characters it knocked down fall, and so does the character when pushed into a wall.
    /// </summary>
    public void ApplyPhysics(CharacterReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ImpactSpeed is { } speed && ImpactDamage.For(speed) is { } impact)
        {
            var attack = AttackProperties.Damage | (impact.KnocksDown ? AttackProperties.Knockdown : AttackProperties.None);
            Damage(new DamageEntry(attack, ImpactDamage.Element, impact.Amount, ImpactDamage.Magnitude), LastAttacker, Position);
        }
        foreach (var body in report.KnockedDown)
        {
            (body.Body.Tag as Character)?.KnockDown();
        }
        if (report.KnockedDownSelf)
        {
            KnockDown();
        }
    }
}
