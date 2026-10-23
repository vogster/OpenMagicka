using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Magicka;

/// <summary>An item a character wears in an equipment slot, at a bone.</summary>
public sealed record EquipmentContent(int Slot, string Bone, Vector3 Rotation, ExternalReference? Item);

/// <summary>Movement flags and the animations used for that kind of movement.</summary>
public sealed record MoveAnimationsContent(MovementProperties Movement, IReadOnlyList<string> Animations);

/// <summary>
/// A creature or wizard (Data/Characters/*.xnb). Names are kept as strings; the original hashes
/// most of them on load. Layout: docs/formats/XNB_READERS.md §5.1.
/// </summary>
public sealed record CharacterTemplateContent(
    string Id,
    string DisplayId,
    Factions Factions,
    int BloodType,
    bool IsEthereal,
    bool LooksEthereal,
    bool Fearless,
    bool Uncharmable,
    bool Nonslippery,
    bool HasFairy,
    bool CanSeeInvisible,
    IReadOnlyList<SoundContent> Sounds,
    IReadOnlyList<GibContent> Gibs,
    IReadOnlyList<AttachedLightContent> Lights,
    CharacterStats Stats,
    SoundBanks SummonElementBank,
    string SummonElementCue,
    IReadOnlyList<ResistanceContent> Resistances,
    IReadOnlyList<ModelVariantContent> Models,
    ExternalReference? Skeleton,
    IReadOnlyList<AttachedEffectContent> AttachedEffects,
    IReadOnlyList<AnimationSetContent> AnimationSets,
    IReadOnlyList<EquipmentContent> Equipment,
    IReadOnlyList<EventCollectionContent> Events,
    CharacterAiWeights Ai,
    IReadOnlyList<AbilityContent> Abilities,
    IReadOnlyList<MoveAnimationsContent> MoveAnimations,
    IReadOnlyList<BuffContent> Buffs,
    IReadOnlyList<AuraContent> Auras);

public sealed record CharacterStats(
    float MaxHitpoints,
    int NumberOfHealthBars,
    bool Undying,
    float UndieTime,
    float UndieHitPoints,
    int HitTolerance,
    float KnockdownTolerance,
    int ScoreValue,
    int ExperienceValue,
    bool RewardOnKill,
    bool RewardOnOverkill,
    int Regeneration,
    float MaxPanic,
    float ZapModifier,
    float Length,
    float Radius,
    float Mass,
    float Speed,
    float TurnSpeed,
    float BleedRate,
    float StunTime);

/// <summary>Fuzzy AI weights and steering (flocking) parameters.</summary>
public sealed record CharacterAiWeights(
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
    float BreakFreeStrength);

/// <summary>A weapon, staff or other item (Data/Items/**). Layout: docs/formats/XNB_READERS.md §5.2.</summary>
public sealed record ItemContent(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<SoundContent> Sounds,
    bool Pickable,
    bool Bound,
    int BlockValue,
    byte WeaponClass,
    float CooldownTime,
    bool HideModel,
    bool HideEffect,
    bool PauseSounds,
    IReadOnlyList<ResistanceContent> Resistances,
    byte PassiveAbility,
    float PassiveAbilityValue,
    IReadOnlyList<string> Effects,
    IReadOnlyList<PointLightContent> Lights,
    float? SpecialRechargeTime,
    SpecialAbilityContent? SpecialAbility,
    ItemMelee Melee,
    ItemRanged Ranged,
    float Scale,
    ExternalReference? Model,
    IReadOnlyList<AuraContent> Auras);

public sealed record ItemMelee(float Range, bool MultiHit, IReadOnlyList<EventCollectionContent> Conditions);

/// <summary>
/// Ranged and gun parameters. Sounds are "", "cue" (Weapons bank) or "Bank/cue"; sprites are
/// "" or a number optionally prefixed by a sheet letter, as in the original.
/// </summary>
public sealed record ItemRanged(
    float Range,
    bool Facing,
    float Homing,
    float Elevation,
    float Danger,
    float GunRange,
    int GunClip,
    int GunRate,
    float GunAccuracy,
    string GunSound,
    string GunMuzzleEffect,
    string GunShellsEffect,
    float TracerVelocity,
    string NonTracerSprite,
    string TracerSprite,
    IReadOnlyList<EventCollectionContent> GunConditions,
    ExternalReference? ProjectileModel,
    IReadOnlyList<EventCollectionContent> RangedConditions);

/// <summary>An oriented box of a physics object; position is a corner, as for trigger areas.</summary>
public sealed record CollisionBoxContent(string Name, Vector3 Corner, Vector3 SideLengths, Quaternion Orientation);

public sealed record PlacedEffectContent(string Effect, Matrix Transform);

/// <summary>Animated appearance of a physics object (present in every file of the Steam release).</summary>
public sealed record PhysicsEntityAnimationContent(
    float Radius,
    IReadOnlyList<ModelVariantContent> Models,
    ExternalReference? Skeleton,
    IReadOnlyList<AttachedEffectContent> AttachedEffects,
    IReadOnlyList<AnimationSetContent> AnimationSets);

/// <summary>A prop: barrel, crate, door (Data/PhysicsEntities/**). Layout: docs/formats/XNB_READERS.md §5.3.</summary>
public sealed record PhysicsEntityTemplateContent(
    bool Movable,
    bool Pushable,
    bool Solid,
    float Mass,
    int MaxHitPoints,
    bool CanHaveStatus,
    IReadOnlyList<ResistanceContent> Resistances,
    IReadOnlyList<GibContent> Gibs,
    string HitEffect,
    string HitSound,
    string GibTrailEffect,
    ModelContent? Model,
    CollisionMeshContent? CollisionMesh,
    IReadOnlyList<CollisionBoxContent> Boxes,
    IReadOnlyList<PlacedEffectContent> Effects,
    IReadOnlyList<EventCollectionContent> Events,
    string Id,
    PhysicsEntityAnimationContent? Animation);
