using System.Numerics;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Buffs;

/// <summary>How a buff shows on the ground under its bearer.</summary>
public enum BuffVisual : byte
{
    None,
    Offensive,
    Defensive,
    Special,
}

/// <summary>
/// A timed effect on a character (docs/engine/ENTITIES_AI.md §8.3.1). The same buff object given
/// again only renews its time, as each loaded buff of the original has its own id: loaders must give
/// one object per buff of a loaded asset (the engine caches templates for that).
/// </summary>
public abstract record Buff(float Ttl, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
{
    /// <summary>Throws on what the rules could not use (loaders call it as they read).</summary>
    public virtual void Validate()
    {
        Check(Ttl, nameof(Ttl));
        ArgumentNullException.ThrowIfNull(Effect);
        if (!Enum.IsDefined(Visual))
        {
            throw new ArgumentOutOfRangeException(nameof(Visual), Visual, "unknown look");
        }
    }

    protected static void Check(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "must be finite");
        }
    }

    protected static void Check(DamageEntry damage, string name)
    {
        Check(damage.Amount, name);
        Check(damage.Magnitude, name);
    }

    // Records compare by reference here: two buffs alike in value are still two buffs.
    public virtual bool Equals(Buff? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>Spells of the damage's elements add its amount and multiply by its magnitude.</summary>
public sealed record BoostDamageBuff(float Ttl, DamageEntry Damage, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        Check(Damage, nameof(Damage));
    }
}

/// <summary>The damage hits the bearer every second.</summary>
public sealed record DealDamageBuff(float Ttl, DamageEntry Damage, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect)
{
    public const float Interval = 1;

    public override void Validate()
    {
        base.Validate();
        Check(Damage, nameof(Damage));
    }
}

public sealed record ResistanceBuff(float Ttl, Resistance Resistance, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        Check(Resistance.Multiplier, nameof(Resistance));
        Check(Resistance.Modifier, nameof(Resistance));
    }
}

public sealed record UndyingBuff(float Ttl, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect);

/// <summary>Multiplies an avatar's boosts (cut to a whole number).</summary>
public sealed record BoostBuff(float Ttl, float Amount, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        Check(Amount, nameof(Amount));
    }
}

/// <summary>Multiplies the aggro the AI feels for the bearer.</summary>
public sealed record ReduceAggroBuff(float Ttl, float Amount, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Buff(Ttl, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        Check(Amount, nameof(Amount));
    }
}

/// <summary>What a modifier buff changes: value × multiplier + modifier.</summary>
public enum Modified : byte
{
    HitPoints,
    SpellTtl,
    SpellRange,
}

public sealed record ModifierBuff(float Ttl, Modified Kind, float Multiplier, float Modifier, BuffVisual Visual = BuffVisual.None, Vector3 Color = default,
    string Effect = "") : Buff(Ttl, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        Check(Multiplier, nameof(Multiplier));
        Check(Modifier, nameof(Modifier));
    }
}
