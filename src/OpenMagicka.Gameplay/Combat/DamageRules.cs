using System.Numerics;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>What a hit did, as the original reports it.</summary>
[Flags]
public enum DamageResult
{
    None = 0,
    Damaged = 1,
    Hit = 2,
    KnockedDown = 4,
    KnockedBack = 8,
    Pushed = 0x10,
    StatusAdded = 0x20,
    StatusRemoved = 0x40,
    Healed = 0x80,
    Deflected = 0x100,
    Killed = 0x200,
    Pierced = 0x400,
    OverKilled = 0x800,
}

/// <summary>The state of a character that one entry of damage needs.</summary>
/// <param name="HitTolerance">A hit is reported only above this much damage.</param>
/// <param name="BleedRate">The DPS of the bleeding a Bleed attack causes (0: a bleed that hurts nothing, as in the original).</param>
/// <param name="CanHaveStatus">False for the immortal, the ethereal and templates without statuses.</param>
/// <param name="BloodAndGore">The overkill by damage needs gore on (a frozen target shatters anyway).</param>
public sealed record DamageTarget(
    float HitPoints,
    float MaxHitPoints,
    StatusSet Statuses,
    Defences Defences,
    float HitTolerance = 0,
    float BleedRate = 0,
    bool CanHaveStatus = true,
    bool IsImmortal = false,
    bool BloodAndGore = true);

/// <summary>The character after one entry of damage.</summary>
/// <param name="Dealt">Amount × magnitude taken off the hit points (negative healed); 0 when deflected.</param>
public sealed record DamageOutcome(float HitPoints, StatusSet Statuses, DamageResult Result, float Dealt = 0, bool Stunned = false, bool InterruptsCast = false);

/// <summary>
/// How one entry of damage lands on a character, steps 6–10 of docs/gameplay/SPELLS.md §7.2: statuses
/// before resistances, then the resistances, stun and bleeding, the special multipliers, the hit
/// points and death. Pushes, blocks, entanglement and the self-shield (steps 1–5) are the
/// character's; <see cref="PushVelocity"/> gives a push's velocity.
/// </summary>
public static class DamageRules
{
    /// <summary>The elevation of a knockback (40°) and of a push (10°).</summary>
    public const float KnockbackElevation = 0.6980619f;
    public const float PushElevation = 0.17453292f;

    /// <summary>Bodies heavier than this are not pushed.</summary>
    public const float UnpushableMass = 1000;

    public static DamageOutcome Hit(DamageEntry damage, DamageTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.HitPoints <= 0 || target.IsImmortal)
        {
            return new DamageOutcome(target.HitPoints, target.Statuses, DamageResult.None);
        }
        var (statuses, result, interrupts) = Statuses(damage, target);
        damage = target.Defences.Resist(damage);
        if (MathF.Abs(damage.Magnitude) <= float.Epsilon)
        {
            return new DamageOutcome(target.HitPoints, statuses, result | DamageResult.Deflected, InterruptsCast: interrupts);
        }
        var stunned = damage.Attack.HasFlag(AttackProperties.Stun) && damage.Amount > 0;
        if (damage.Attack.HasFlag(AttackProperties.Bleed) && damage.Amount > 0 && target.CanHaveStatus)
        {
            var bleeding = statuses.Add(StatusEffect.Create(StatusEffects.Bleeding, target.BleedRate, 1), target.Defences);
            (statuses, result) = (bleeding.Set, result | Reported(bleeding.Outcome));
        }
        if (!damage.Attack.HasFlag(AttackProperties.Damage))
        {
            return new DamageOutcome(target.HitPoints, statuses, result, Stunned: stunned, InterruptsCast: interrupts);
        }
        var (amount, thinned) = Special(damage, statuses);
        var dealt = amount * damage.Magnitude;
        result |= Results(damage, dealt, target.HitTolerance);
        var after = MathF.Min(target.HitPoints - dealt, target.MaxHitPoints);
        if (target.HitPoints - dealt <= 0)
        {
            var shatters = statuses.Has(StatusEffects.Frozen) || (target.HitPoints - dealt <= -0.5f * target.MaxHitPoints - 500 && target.BloodAndGore);
            result |= DamageResult.Killed | (shatters ? DamageResult.OverKilled : DamageResult.None);
            if (shatters && (damage.Element & ~Elements.Arcane) != 0)
            {
                after = -target.MaxHitPoints - 1000;                              // shattered: no corpse to revive
            }
        }
        return new DamageOutcome(after, thinned, result, dealt, stunned, interrupts);
    }

    /// <summary>A whole attack's result: every entry's flags, but Deflected only if all of them were.</summary>
    public static DamageResult Combine(IReadOnlyList<DamageResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return DamageResult.None;
        }
        var any = results.Aggregate(DamageResult.None, (a, r) => a | r);
        var all = results.All(r => r.HasFlag(DamageResult.Deflected));
        return all ? any : any & ~DamageResult.Deflected;
    }

    /// <summary>
    /// The velocity of a push from <paramref name="attack"/>: it carries the target
    /// <c>distance · max(0, 2·√(1 − (mass − power/4)/power))</c> across flat ground at the elevation, ×1.4.
    /// Zero when the reach is nothing, the direction is undefined or the target is too heavy (or, unlike
    /// the original, the power is not positive).
    /// </summary>
    public static Vector3 PushVelocity(Vector3 attack, Vector3 target, float elevation, float power, float distance, float mass, float gravity)
    {
        var reach = distance * MathF.Max(0, 2 * MathF.Sqrt(1 - (mass - power / 4) / power));
        var flat = new Vector3(target.X - attack.X, 0, target.Z - attack.Z);
        if (!(power > 0) || !(reach >= 1e-6f) || mass > UnpushableMass || flat.LengthSquared() <= float.Epsilon)
        {
            return Vector3.Zero;
        }
        var direction = Vector3.Normalize(flat);
        var speed = 1.4f * MathF.Sqrt(gravity * reach / MathF.Sin(2 * elevation));
        var velocity = new Vector3(direction.X * MathF.Cos(elevation), MathF.Sin(elevation), direction.Z * MathF.Cos(elevation)) * speed;
        return float.IsFinite(velocity.LengthSquared()) ? velocity : Vector3.Zero;
    }

    /// <summary>Regeneration: only alive, at most full, and not while burning or poisoned; capped at full health.</summary>
    public static float Regenerate(float hitPoints, float maxHitPoints, float rate, float dt, StatusSet statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return hitPoints > 0 && hitPoints <= maxHitPoints && !statuses.BlocksRegeneration
            ? MathF.Min(hitPoints + rate * dt, maxHitPoints)
            : hitPoints;
    }

    /// <summary>The status each element sets, in the order they are applied.</summary>
    private static readonly (Elements Element, StatusEffects Status)[] StatusSources =
    [
        (Elements.Fire, StatusEffects.Burning), (Elements.Cold, StatusEffects.Cold), (Elements.Water, StatusEffects.Wet),
        (Elements.Poison, StatusEffects.Poisoned), (Elements.Life, StatusEffects.Healing), (Elements.Steam, StatusEffects.Wet),
    ];

    /// <summary>Step 6: each status element of a Status entry sets its status, before any resistance.</summary>
    private static (StatusSet, DamageResult, bool) Statuses(DamageEntry damage, DamageTarget target)
    {
        var (statuses, result, interrupts) = (target.Statuses, DamageResult.None, false);
        if (!damage.Attack.HasFlag(AttackProperties.Status) || !target.CanHaveStatus)
        {
            return (statuses, result, interrupts);
        }
        foreach (var (element, status) in StatusSources)
        {
            if ((damage.Element & element) != 0)
            {
                var change = statuses.Add(StatusEffect.Create(status, damage.Amount, damage.Magnitude), target.Defences);
                (statuses, result, interrupts) = (change.Set, result | Reported(change.Outcome), interrupts | change.InterruptsCast);
            }
        }
        return (statuses, result, interrupts);
    }

    /// <summary>Step 9: lightning hurts the wet twice, Earth, Ice and Arcane the frozen thrice; Life thins poison.</summary>
    private static (float Amount, StatusSet Statuses) Special(DamageEntry damage, StatusSet statuses)
    {
        var amount = damage.Amount;
        if ((damage.Element & Elements.Lightning) != 0 && statuses.Has(StatusEffects.Wet))
        {
            amount *= 2;
        }
        var after = (damage.Element & Elements.Life) != 0 ? statuses.ThinPoison(damage.Magnitude) : statuses;
        if ((damage.Element & (ElementSets.Physical | Elements.Arcane)) != 0 && statuses.Has(StatusEffects.Frozen))
        {
            amount *= 3;
        }
        return (amount, after);
    }

    private static DamageResult Results(DamageEntry damage, float dealt, float hitTolerance)
    {
        var result = DamageResult.None;
        result |= damage.Attack.HasFlag(AttackProperties.Piercing) && damage.Magnitude > 0 && dealt > 0 ? DamageResult.Pierced : 0;
        result |= dealt > 0 ? DamageResult.Damaged : dealt < 0 ? DamageResult.Healed : DamageResult.Deflected;
        result |= dealt > hitTolerance ? DamageResult.Hit : 0;
        return result;
    }

    private static DamageResult Reported(StatusOutcome outcome) => outcome switch
    {
        StatusOutcome.Added => DamageResult.StatusAdded,
        StatusOutcome.Removed => DamageResult.StatusRemoved,
        _ => DamageResult.None,
    };
}
