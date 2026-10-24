using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// A character template's mind (ENTITIES_AI.md §7): how far it notices, how it weighs targets, how
/// it steers in a crowd, and what it can do.
/// </summary>
public sealed record AiProfile(
    float AlertRadius,
    float GroupChase,
    float GroupSeparation,
    float GroupCohesion,
    float GroupAlignment,
    float GroupWander,
    float FriendlyAvoidance,
    float EnemyAvoidance,
    float SightAvoidance,
    float DangerAvoidance,
    float AngerWeight,
    float DistanceWeight,
    float HealthWeight,
    bool Flocking,
    float BreakFreeStrength,
    IReadOnlyList<AbilityTemplate> Abilities)
{
    public static AiProfile None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, []);
}

/// <summary>Whom an ability is for.</summary>
public enum AbilityTarget : byte { Self = 1, Enemy = 2, Friendly = 3 }

/// <summary>
/// An NPC ability: its cooldown, whom it is for, how much it is wanted (a fuzzy expression; none
/// for its own built-in measure) and the clips it may play. One record per kind (§7.8).
/// </summary>
public abstract record AbilityTemplate(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations);

public sealed record MeleeAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, IReadOnlyList<int> Weapons, bool Rotate) : AbilityTemplate(Cooldown, Target, Desire, Animations);

/// <summary>Its arc is always π, as the original overwrites what is stored.</summary>
public sealed record RangedAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Elevation, float Arc, float Accuracy, IReadOnlyList<int> Weapons) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record CastSpellAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, float ChantSpeed, float Power, CastType CastType, IReadOnlyList<Elements> Elements)
    : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record BlockAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float Arc, int Shield) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record DashAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Arc, Vector3 Vector) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record JumpAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Angle, float Elevation) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record ElementStealAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float Range, float Angle) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record SpecialAbilityAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Angle, int Weapon) : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record RemoveStatusAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations)
    : AbilityTemplate(Cooldown, Target, Desire, Animations);

/// <summary>A grip from behind, a pick-up, or (<see cref="Zombie"/>) a zombie's grip; grips come with OM-33.</summary>
public sealed record GripAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Angle, float MaxWeight, string? DropAnimation, bool FromBehind, bool Zombie)
    : AbilityTemplate(Cooldown, Target, Desire, Animations);

public sealed record ThrowGripAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations,
    float MinRange, float MaxRange, float Elevation, IReadOnlyList<DamageEntry> Damages) : AbilityTemplate(Cooldown, Target, Desire, Animations);

/// <summary>What is done to one in its grip: hurt (<see cref="Confuse"/> false) or confuse it.</summary>
public sealed record GripActionAbility(float Cooldown, AbilityTarget Target, FuzzyExpression? Desire, IReadOnlyList<string> Animations, bool Confuse)
    : AbilityTemplate(Cooldown, Target, Desire, Animations);
