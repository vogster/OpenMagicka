using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Items;

/// <summary>An item's weapon class: it picks the animation set (docs/engine/ENTITIES_AI.md §8.1.1).</summary>
public enum WeaponClass : byte
{
    Default,
    ThrustFast,
    ThrustMedium,
    ThrustSlow,
    CrushFast,
    CrushMedium,
    CrushSlow,
    SlashFast,
    SlashMedium,
    SlashSlow,
    ThrowFast,
    ThrowMedium,
    ThrowSlow,
    Unarmed,
    Handgun,
    Rifle,
    Machinegun,
    Heavy,
    Staff,
    NewAnimationSet0,
    NewAnimationSet1,
    NewAnimationSet2,
    NewAnimationSet3,
    NewAnimationSet4,
    NewAnimationSet5,
    NewAnimationSet6,
    NewAnimationSet7,
}

/// <summary>What an equipped item does by just being held; each takes effect with its system.</summary>
public enum PassiveAbility : byte
{
    None,
    ShieldBoost,
    AreaLifeDrain,
    ZombieDeterrent,
    ReduceAggro,
    EnhanceAllyMelee,
    AreaRegeneration,
    InverseArcaneLife,
    Zap,
    BirchSteam,
    WetLightning,
    MoveSpeed,
    Glow,
    Mjolnr,
    Gungner,
    MasterSword,
    DragonSlayer,
}

/// <summary>What an item type brings to the rules (the subset of the item file they use so far).</summary>
/// <param name="Resistances">For an avatar's slot-1 item, joined to the template's on every hit.</param>
/// <param name="Attach0">Its tip or muzzle in the item's own frame (§4.3); null at the item's origin.</param>
/// <param name="Attach1">Its second point (a gun's shells); null at the origin.</param>
/// <param name="Melee">The groups a melee strike fires (§8.1.2, §8.2).</param>
/// <param name="Auras">What the item spreads around its holder while held (§8.3.2).</param>
public sealed record ItemTemplate(
    string Id,
    WeaponClass WeaponClass = WeaponClass.Default,
    int BlockValue = 0,
    ResistanceTable? Resistances = null,
    PassiveAbility PassiveAbility = PassiveAbility.None,
    float PassiveValue = 0,
    float CooldownTime = 0,
    float MeleeRange = 0,
    bool MeleeMultiHit = false,
    bool Pickable = false,
    bool Bound = false,
    System.Numerics.Matrix4x4? Attach0 = null,
    System.Numerics.Matrix4x4? Attach1 = null,
    IReadOnlyList<EventGroup>? Melee = null,
    IReadOnlyList<Buffs.Aura>? Auras = null)
{
    /// <summary>Throws on a value the rules cannot use; loaders call it as they read.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Id);
        if (BlockValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BlockValue), BlockValue, "must not be negative");
        }
        foreach (var (value, name) in new[] { (PassiveValue, nameof(PassiveValue)), (CooldownTime, nameof(CooldownTime)), (MeleeRange, nameof(MeleeRange)) })
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");
            }
        }
        if (!Enum.IsDefined(WeaponClass))
        {
            throw new ArgumentOutOfRangeException(nameof(WeaponClass), WeaponClass, "unknown weapon class");
        }
        if (!Enum.IsDefined(PassiveAbility))
        {
            throw new ArgumentOutOfRangeException(nameof(PassiveAbility), PassiveAbility, "unknown passive ability");
        }
        foreach (var group in Melee ?? [])
        {
            ArgumentNullException.ThrowIfNull(group, nameof(Melee));
            group.Validate();
        }
        foreach (var aura in Auras ?? [])
        {
            ArgumentNullException.ThrowIfNull(aura, nameof(Auras));
            aura.Validate();
        }
    }
}
