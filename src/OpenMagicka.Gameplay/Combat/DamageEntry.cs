using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>What an entry of damage does besides hurting (docs/gameplay/SPELLS.md §6).</summary>
[Flags]
public enum AttackProperties : short
{
    None = 0,
    Damage = 1,
    Knockdown = 2,
    Pushed = 4,
    Knockback = Pushed | Knockdown,
    Piercing = 8,
    ArmourPiercing = 0x10,
    Status = 0x20,
    Entanglement = 0x40,
    Stun = 0x80,
    Bleed = 0x100,
}

/// <summary>One entry of damage: a negative amount heals. The hit is about amount × magnitude.</summary>
public readonly record struct DamageEntry(AttackProperties Attack, Elements Element, float Amount, float Magnitude)
{
    /// <summary>
    /// Two entries as one: the amounts averaged by magnitude, the magnitudes added. Two entries of no
    /// magnitude give an amount of 0 (the original divides by zero).
    /// </summary>
    public static DamageEntry Combine(DamageEntry a, DamageEntry b)
    {
        var magnitude = a.Magnitude + b.Magnitude;
        var amount = magnitude == 0 ? 0 : (a.Amount * a.Magnitude + b.Amount * b.Magnitude) / magnitude;
        return new DamageEntry(a.Attack | b.Attack, a.Element | b.Element, amount, magnitude);
    }
}
