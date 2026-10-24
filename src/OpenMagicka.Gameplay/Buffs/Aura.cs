using System.Numerics;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Buffs;

/// <summary>Whom an aura works on (docs/engine/ENTITIES_AI.md §8.3.2).</summary>
public enum AuraTarget : byte
{
    Friendly,
    FriendlyButSelf,
    Enemy,
    All,
    AllButSelf,
    Self,
    Type,
    TypeButSelf,
    Faction,
    FactionButSelf,
}

/// <summary>
/// An area around its owner that works on what is in it. A character's aura goes once its time runs
/// out (none left from the start: after its first frame); a held item's never does. Vanilla times are
/// millions of seconds.
/// </summary>
/// <param name="TargetTypes">The template ids a Type aura works on.</param>
/// <param name="TargetFaction">The factions a Faction aura works on.</param>
public abstract record Aura(AuraTarget Target, float Ttl, float Radius, IReadOnlyList<string>? TargetTypes = null, Factions TargetFaction = Factions.None,
    BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
{
    /// <summary>How often the auras that work in pulses pulse.</summary>
    public const float Period = 0.25f;

    public virtual void Validate()
    {
        if (!float.IsFinite(Ttl))
        {
            throw new ArgumentOutOfRangeException(nameof(Ttl), Ttl, "must be finite");
        }
        if (!(Radius >= 0) || !float.IsFinite(Radius))
        {
            throw new ArgumentOutOfRangeException(nameof(Radius), Radius, "must be finite and not negative");
        }
        if (!Enum.IsDefined(Target) || !Enum.IsDefined(Visual))
        {
            throw new ArgumentOutOfRangeException(nameof(Target), Target, "unknown target or look");
        }
        ArgumentNullException.ThrowIfNull(Effect);
    }
}

/// <summary>Gives its buff to the characters it targets.</summary>
public sealed record BuffAura(AuraTarget Target, float Ttl, float Radius, Buff Buff, IReadOnlyList<string>? TargetTypes = null,
    Factions TargetFaction = Factions.None, BuffVisual Visual = BuffVisual.None, Vector3 Color = default, string Effect = "")
    : Aura(Target, Ttl, Radius, TargetTypes, TargetFaction, Visual, Color, Effect)
{
    public override void Validate()
    {
        base.Validate();
        ArgumentNullException.ThrowIfNull(Buff);
        Buff.Validate();
    }
}

/// <summary>Turns missiles within its strength away (with projectiles).</summary>
public sealed record DeflectAura(AuraTarget Target, float Ttl, float Radius, float Strength, BuffVisual Visual = BuffVisual.None, Vector3 Color = default,
    string Effect = "") : Aura(Target, Ttl, Radius, Visual: Visual, Color: Color, Effect: Effect);

/// <summary>Multiplies an avatar's boosts (cut to a whole number).</summary>
public sealed record BoostAura(AuraTarget Target, float Ttl, float Radius, float Magnitude, BuffVisual Visual = BuffVisual.None, Vector3 Color = default,
    string Effect = "") : Aura(Target, Ttl, Radius, Visual: Visual, Color: Color, Effect: Effect);

/// <summary>Does nothing, as in the original.</summary>
public sealed record LifeStealAura(AuraTarget Target, float Ttl, float Radius, float Amount, BuffVisual Visual = BuffVisual.None, Vector3 Color = default,
    string Effect = "") : Aura(Target, Ttl, Radius, Visual: Visual, Color: Color, Effect: Effect);

/// <summary>Charms the enemies around (with charms).</summary>
public sealed record LoveAura(AuraTarget Target, float Ttl, float Radius, float LoveRadius, float LoveTtl, BuffVisual Visual = BuffVisual.None,
    Vector3 Color = default, string Effect = "") : Aura(Target, Ttl, Radius, Visual: Visual, Color: Color, Effect: Effect);

/// <summary>An aura at work: its time left and its pulse. Changes in place.</summary>
public sealed class ActiveAura
{
    public ActiveAura(Aura aura)
    {
        ArgumentNullException.ThrowIfNull(aura);
        aura.Validate();
        (Aura, Ttl) = (aura, aura.Ttl);
    }

    public Aura Aura { get; }

    public float Ttl { get; internal set; }

    /// <summary>Time to the next pulse; the first pulse comes on the first frame.</summary>
    internal float Pulse { get; set; }
}

/// <summary>A buff on a character: its time left, and for damage its clock and who gave it. Changes in place.</summary>
public sealed class ActiveBuff(Buff buff, Character? giver)
{
    public Buff Buff { get; } = buff ?? throw new ArgumentNullException(nameof(buff));

    public float Ttl { get; internal set; } = buff.Ttl;

    /// <summary>The aura's owner for a damage buff (its attacker), or none.</summary>
    public Character? Giver { get; internal set; } = giver;

    internal float Interval { get; set; } = DealDamageBuff.Interval;
}
