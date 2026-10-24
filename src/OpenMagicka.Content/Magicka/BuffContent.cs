using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Magicka;

public enum BuffVisualCategory : byte
{
    None,
    Offensive,
    Defensive,
    Special,
}

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

/// <summary>A temporary effect on an entity. One subtype per buff type.</summary>
public abstract record BuffContent(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect);

public sealed record BoostDamageBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect, DamageContent Damage)
    : BuffContent(Visual, Color, Ttl, Effect);

/// <summary>Deals the damage once per second.</summary>
public sealed record DealDamageBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect, DamageContent Damage)
    : BuffContent(Visual, Color, Ttl, Effect);

/// <summary>Note: in the file the modifier comes before the multiplier, unlike template resistances.</summary>
public sealed record ResistanceBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect, ResistanceContent Resistance)
    : BuffContent(Visual, Color, Ttl, Effect);

public sealed record UndyingBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect)
    : BuffContent(Visual, Color, Ttl, Effect);

public sealed record BoostBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect, float Amount)
    : BuffContent(Visual, Color, Ttl, Effect);

public sealed record ReduceAggroBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect, float Amount)
    : BuffContent(Visual, Color, Ttl, Effect);

public enum ModifierBuffKind : byte
{
    HitPoints = 6,
    SpellTtl = 7,
    SpellRange = 8,
}

/// <summary>ModifyHitPoints, ModifySpellTTL and ModifySpellRange: value * Multiplier + Modifier.</summary>
public sealed record ModifierBuff(BuffVisualCategory Visual, Vector3 Color, float Ttl, string Effect,
    ModifierBuffKind Kind, float Multiplier, float Modifier) : BuffContent(Visual, Color, Ttl, Effect);

/// <summary>
/// An area that applies an effect to entities around its owner. a character's aura goes when its TTL runs out (not forever at 0); a held item's never does.
/// <see cref="TargetTypes"/> is a comma-separated list of character types.
/// </summary>
public abstract record AuraContent(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction);

public sealed record BuffAura(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction, BuffContent Buff)
    : AuraContent(Target, Visual, Color, Effect, Ttl, Radius, TargetTypes, TargetFaction);

public sealed record DeflectAura(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction, float Strength)
    : AuraContent(Target, Visual, Color, Effect, Ttl, Radius, TargetTypes, TargetFaction);

public sealed record BoostAura(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction, float Magnitude)
    : AuraContent(Target, Visual, Color, Effect, Ttl, Radius, TargetTypes, TargetFaction);

public sealed record LifeStealAura(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction, float Amount)
    : AuraContent(Target, Visual, Color, Effect, Ttl, Radius, TargetTypes, TargetFaction);

public sealed record LoveAura(AuraTarget Target, BuffVisualCategory Visual, Vector3 Color, string Effect,
    float Ttl, float Radius, string TargetTypes, int TargetFaction, float LoveRadius, float LoveTtl)
    : AuraContent(Target, Visual, Color, Effect, Ttl, Radius, TargetTypes, TargetFaction);
