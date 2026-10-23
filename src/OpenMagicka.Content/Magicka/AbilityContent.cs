using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Magicka;

public enum AbilityTarget : byte
{
    Self = 1,
    Enemy = 2,
    Friendly = 3,
}

/// <summary>
/// An AI ability of a character. The common part: cooldown, target, an optional fuzzy-logic
/// expression that rates its usefulness, and the animations it plays. One subtype per class.
/// </summary>
public abstract record AbilityContent(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations);

public sealed record BlockAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float Arc, int Shield) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record CastSpellAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, float ChantSpeed, float Power, int CastType, IReadOnlyList<Elements> Elements)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record ConfuseGripAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record DamageGripAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record RemoveStatusAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record DashAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, Vector3 Vector) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record ElementStealAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float Range, float Angle) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record GripCharacterFromBehindAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MaxRange, float MinRange, float Angle, float MaxWeight) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record JumpAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MaxRange, float MinRange, float Angle, float Elevation) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record MeleeAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, IReadOnlyList<int> Weapons, bool Rotate) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

/// <summary>PickUpCharacter and ZombieGrip share this layout; <see cref="TypeName"/> tells them apart.</summary>
public sealed record PickUpCharacterAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    string TypeName, float MaxRange, float MinRange, float Angle, float MaxWeight, string DropAnimation)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations)
{
    public bool IsZombieGrip => TypeName.Equals("ZombieGrip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The stored arc is overwritten with pi by the original.</summary>
public sealed record RangedAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Elevation, float Arc, float Accuracy, IReadOnlyList<int> Weapons)
    : AbilityContent(Cooldown, Target, Fuzzy, Animations);

public sealed record SpecialAbilityAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MaxRange, float MinRange, float Angle, int Weapon) : AbilityContent(Cooldown, Target, Fuzzy, Animations);

/// <summary>Damage amounts are stored as integers.</summary>
public sealed record ThrowGripAbility(float Cooldown, AbilityTarget Target, string? Fuzzy, IReadOnlyList<string> Animations,
    float MaxRange, float MinRange, float Elevation, IReadOnlyList<DamageContent> Damages) : AbilityContent(Cooldown, Target, Fuzzy, Animations);
