using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>What adding a status did.</summary>
public enum StatusOutcome
{
    /// <summary>Nothing: no magnitude, resisted, or forbidden.</summary>
    None,

    /// <summary>The status took (or stacked).</summary>
    Added,

    /// <summary>It met an opposite and put it out (or was blocked by drying) instead of taking.</summary>
    Removed,
}

/// <summary>The result of <see cref="StatusSet.Add"/>.</summary>
/// <param name="InterruptsCast">The character froze: its cast stops.</param>
public sealed record StatusChange(StatusSet Set, StatusOutcome Outcome, bool InterruptsCast = false);

/// <summary>The result of <see cref="StatusSet.Update"/>: the new set and the damage dealt this tick.</summary>
public sealed record StatusTick(StatusSet Set, IReadOnlyList<StatusDamage> Damage);

/// <summary>
/// A character's statuses, one slot each (docs/gameplay/SPELLS.md §9), and the drying timer that keeps
/// water and cold off right after a fire went out. Immutable: every change returns a new set.
/// </summary>
public sealed partial class StatusSet
{
    /// <summary>How long new Wet and Cold are kept off after evaporation.</summary>
    public const float DryingTime = 0.9f;

    public const float MaxMagnitude = 3;

    private readonly StatusEffect[] slots;

    private StatusSet(StatusEffect[] slots, float dryTimer)
    {
        this.slots = slots;
        DryTimer = dryTimer;
        foreach (var slot in slots)
        {
            Active |= slot.IsActive ? slot.Type : StatusEffects.None;
        }
    }

    public static StatusSet Empty { get; } = new(new StatusEffect[StatusEffect.Count], 0);

    /// <summary>The statuses alive now.</summary>
    public StatusEffects Active { get; }

    /// <summary>Drying runs while this is above zero.</summary>
    public float DryTimer { get; }

    public bool IsDrying => DryTimer > 0;

    /// <summary>The slot of a status; its magnitude is 0 when the status is not active.</summary>
    public StatusEffect this[StatusEffects type] => slots[StatusEffect.Index(type)];

    public bool Has(StatusEffects type) => (Active & type) == type && type != StatusEffects.None;

    /// <summary>Burning and poison stop regeneration.</summary>
    public bool BlocksRegeneration => (Active & (StatusEffects.Burning | StatusEffects.Poisoned)) != 0;

    /// <summary>Cold's slowdown, <c>0.333 + 0.667·0.001^m</c>; 1 when not cold.</summary>
    public float ColdSlowdown => Has(StatusEffects.Cold) ? 0.333f + 0.667f * MathF.Pow(0.001f, this[StatusEffects.Cold].Magnitude) : 1;

    /// <summary>The cap cold and poison put on the speed multiplier: cold's slowdown, poison 0.5.</summary>
    public float SpeedFactor(bool inCutscene = false)
    {
        var factor = ColdSlowdown;
        if (Has(StatusEffects.Poisoned) && !inCutscene)
        {
            factor = MathF.Min(factor, 0.5f);
        }
        return factor;
    }

    /// <summary>Status damage as it lands: fire burns a greased character twice as hard.</summary>
    public StatusDamage Amplify(StatusDamage damage) =>
        (damage.Element & Elements.Fire) != 0 && Has(StatusEffects.Greased) ? damage with { Amount = damage.Amount * 2 } : damage;

    /// <summary>Life damage thins poison by its magnitude, curing it at zero.</summary>
    public StatusSet ThinPoison(float magnitude)
    {
        var copy = Copy();
        ref var poison = ref copy[StatusEffect.Index(StatusEffects.Poisoned)];
        poison = poison with { Magnitude = poison.Magnitude - magnitude };
        if (!poison.IsActive)
        {
            poison = default;
        }
        return new StatusSet(copy, DryTimer);
    }

    /// <summary>The set with the given statuses ended.</summary>
    public StatusSet Without(StatusEffects types)
    {
        var copy = Copy();
        for (var i = 0; i < copy.Length; i++)
        {
            if ((copy[i].Type & types) != StatusEffects.None)
            {
                copy[i] = default;
            }
        }
        return new StatusSet(copy, DryTimer);
    }

    /// <summary>A dead character keeps no status.</summary>
    public StatusSet Cleared() => new(new StatusEffect[StatusEffect.Count], DryTimer);

    /// <summary>One tick: the drying timer runs down, every status caps, fades and deals its damage.</summary>
    public StatusTick Update(float dt, float hitPoints, float maxHitPoints)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        var copy = Copy();
        var damage = new List<StatusDamage>();
        for (var i = 0; i < copy.Length; i++)
        {
            if (!copy[i].IsActive)
            {
                continue;
            }
            var (next, dealt) = Tick(copy[i], dt, hitPoints >= maxHitPoints);
            copy[i] = next.IsActive ? next : default;
            if (dealt is { } d)
            {
                damage.Add(d);
            }
        }
        return new StatusTick(new StatusSet(copy, MathF.Max(0, DryTimer - dt)), damage);
    }

    private static (StatusEffect Next, StatusDamage? Damage) Tick(StatusEffect s, float dt, bool fullHealth)
    {
        var (magnitude, dps, rest) = (s.Magnitude, s.Dps, s.DamageRest);
        switch (s.Type)
        {
            case StatusEffects.Burning:
                magnitude = MathF.Min(magnitude, MaxMagnitude) - 0.2f * dt;
                dps = MathF.Min(dps + 0.25f * dps * dt, 500 * magnitude);
                rest += dps * dt;
                break;
            case StatusEffects.Cold:
                magnitude = MathF.Min(magnitude - 0.1f * dt, MaxMagnitude);
                break;
            case StatusEffects.Wet:
                magnitude = MathF.Min(magnitude, 1);
                break;
            case StatusEffects.Frozen:
                magnitude = MathF.Min(magnitude, MaxMagnitude) - 0.25f * dt;
                break;
            case StatusEffects.Poisoned:
                magnitude = s.Expires ? magnitude - dt : MathF.Min(magnitude, 1);
                rest += dps * magnitude * dt;
                break;
            case StatusEffects.Healing:
                rest += dps * dt;
                magnitude = (fullHealth ? dt : MathF.Min(magnitude, MaxMagnitude)) - dt;
                break;
            case StatusEffects.Steamed:
                magnitude = MathF.Min(1, magnitude) - dt;
                break;
            case StatusEffects.Bleeding:
                magnitude -= s.Expires ? dt : 0;
                rest += dps * dt;
                break;
        }
        var whole = MathF.Truncate(rest);
        var next = s with { Magnitude = magnitude, Dps = dps, DamageRest = rest - whole };
        return (next, whole != 0 ? new StatusDamage(whole, DamageElement(s.Type)) : null);
    }

    /// <summary>Fire burns as Fire, poison as Poison, healing as Life; the rest hurts as Earth.</summary>
    private static Elements DamageElement(StatusEffects type) => type switch
    {
        StatusEffects.Burning => Elements.Fire,
        StatusEffects.Poisoned => Elements.Poison,
        StatusEffects.Healing => Elements.Life,
        _ => Elements.Earth,
    };

    private StatusEffect[] Copy() => (StatusEffect[])slots.Clone();
}
