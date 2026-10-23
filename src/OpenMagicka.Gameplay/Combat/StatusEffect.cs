using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>The status effects as flags (docs/gameplay/SPELLS.md §9). Index = log2 of the flag.</summary>
[Flags]
public enum StatusEffects : short
{
    None = 0,
    Burning = 1,
    Wet = 2,
    Frozen = 4,
    Cold = 8,
    Poisoned = 0x10,
    Healing = 0x20,
    Greased = 0x40,
    Steamed = 0x80,
    Bleeding = 0x100,
}

/// <summary>Damage a status deals on a tick, in whole points; negative heals.</summary>
public readonly record struct StatusDamage(float Amount, Elements Element);

/// <summary>
/// One status on a character: alive while its magnitude is above zero. <see cref="DamageRest"/> carries
/// the fraction of a point not dealt yet.
/// </summary>
/// <param name="Expires">An "expiring" poison or bleed fades by one magnitude a second.</param>
public readonly record struct StatusEffect(StatusEffects Type, float Dps, float Magnitude, bool Expires = false, float DamageRest = 0)
{
    public const int Count = 9;

    public bool IsActive => Magnitude > 0;

    /// <summary>A new status as an attack sets it: Healing starts at 0.5 + 0.5·m, Steamed at 1, a lasting bleed at 1.</summary>
    public static StatusEffect Create(StatusEffects type, float dps, float magnitude, bool expires = false)
    {
        Index(type);
        Finite(dps, nameof(dps));
        Finite(magnitude, nameof(magnitude));
        var start = type switch
        {
            StatusEffects.Healing => 0.5f + 0.5f * magnitude,
            StatusEffects.Steamed => 1,
            StatusEffects.Bleeding when !expires => 1,
            _ => magnitude,
        };
        return new StatusEffect(type, dps, start, expires);
    }

    internal static float Finite(float value, string name) =>
        float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must be finite");

    /// <summary>The slot of a single status.</summary>
    public static int Index(StatusEffects type) =>
        type > StatusEffects.None && type <= StatusEffects.Bleeding && ((int)type & ((int)type - 1)) == 0
            ? System.Numerics.BitOperations.Log2((uint)type)
            : throw new ArgumentException($"{type} is not a single status", nameof(type));

    /// <summary>
    /// Two of the same status stacked: magnitudes add (Healing keeps the larger); Burning keeps the
    /// larger DPS, the others average it by magnitude. Two expiring ones: the newer replaces the older;
    /// an expiring one stacked with a lasting one lasts.
    /// </summary>
    public static StatusEffect operator +(StatusEffect a, StatusEffect b)
    {
        if (!a.IsActive || a.Type == StatusEffects.None)
        {
            return b;
        }
        if (!b.IsActive || b.Type == StatusEffects.None)
        {
            return a;
        }
        if (a.Type != b.Type)
        {
            throw new ArgumentException($"cannot stack {a.Type} with {b.Type}", nameof(b));
        }
        var rest = a.DamageRest + b.DamageRest;
        if (a.Expires && b.Expires)
        {
            return b with { DamageRest = rest };
        }
        var magnitude = a.Type == StatusEffects.Healing ? MathF.Max(a.Magnitude, b.Magnitude) : a.Magnitude + b.Magnitude;
        var dps = a.Type == StatusEffects.Burning
            ? MathF.Max(a.Dps, b.Dps)
            : (a.Magnitude * a.Dps + b.Magnitude * b.Dps) / magnitude;      // for Healing the weights need not add up to 1
        return new StatusEffect(a.Type, dps, magnitude, false, rest);
    }
}
