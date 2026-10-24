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
    System.Numerics.Matrix4x4? Attach1 = null)
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
    }
}

/// <summary>One item in play, a copy of its template's; it changes in place (shown or hidden).</summary>
public sealed class Item
{
    public Item(ItemTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        Template = template;
    }

    public ItemTemplate Template { get; }

    public WeaponClass WeaponClass => Template.WeaponClass;

    /// <summary>Drawn on its bone; an animation can hide it.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Where the item is, as of the end of its holder's last frame.</summary>
    public System.Numerics.Matrix4x4 Transform { get; private set; } = System.Numerics.Matrix4x4.Identity;

    /// <summary>The tip (<c>attach0</c>) in the world as of the holder's last frame, and the frame before.</summary>
    public System.Numerics.Vector3 Tip { get; private set; }

    public System.Numerics.Vector3 LastTip { get; private set; }

    private bool placed;

    /// <summary>Moves the item (its holder's call, at the end of its frame); the first place after a new hold has no sweep.</summary>
    internal void Place(System.Numerics.Matrix4x4 transform)
    {
        Transform = transform;
        var tip = ((Template.Attach0 ?? System.Numerics.Matrix4x4.Identity) * transform).Translation;
        (LastTip, Tip, placed) = (placed ? Tip : tip, tip, true);
    }

    /// <summary>Taken into a hand: the next place starts afresh.</summary>
    internal void Held() => placed = false;
}
