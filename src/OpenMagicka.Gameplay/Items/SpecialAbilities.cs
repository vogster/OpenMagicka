using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Items;

/// <summary>A special ability as an item or an action names it (docs/engine/ENTITIES_AI.md §8.1.7, §9).</summary>
/// <param name="Type">The ability's type, found by name ignoring case.</param>
/// <param name="Animation">What its holder plays when an item's is used.</param>
/// <param name="DisplayName">A text key for the HUD; null without one.</param>
public sealed record SpecialAbilityTemplate(string Type, string Animation = SpecialAbilities.DefaultAnimation, string? DisplayName = null,
    IReadOnlyList<Elements>? Elements = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrEmpty(Type);
        ArgumentException.ThrowIfNullOrEmpty(Animation);
    }
}

/// <summary>Running special abilities, and using an item's (docs/engine/ENTITIES_AI.md §8.1.7).</summary>
public static class SpecialAbilities
{
    public const string DefaultAnimation = "cast_self";

    public const string ReadyEffect = "special_ready";

    public const string RechargeSound = "staff_recharge01";

    public const string InvisibilitySound = "magick_invisibility";

    public const float InvisibilityTime = 15;

    /// <summary>The original's sound bank flags.</summary>
    public const int WeaponsBank = 0x80, SpellsBank = 0x10;

    /// <summary>The abilities that work now; each says whether it did anything.</summary>
    private static readonly Dictionary<string, Func<Character, SpecialAbilityTemplate, bool>> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SpecialAnimation"] = (_, _) => true,                                 // the animation is the whole ability
        ["Invisibility"] = (owner, _) =>
        {
            owner.RaiseCue(new SoundCue(InvisibilitySound, SpellsBank, owner.Position, owner.Body.Facing));
            owner.SetInvisible(InvisibilityTime);
            return true;
        },
    };

    /// <summary>Whether the ability does something yet (the others come with spells and magicks).</summary>
    public static bool Works(string type) => Known.ContainsKey(type);

    /// <summary>Runs an ability for a character; false when it did nothing (or does nothing yet).</summary>
    public static bool Execute(Character owner, SpecialAbilityTemplate ability)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ability);
        return Known.TryGetValue(ability.Type, out var run) && run(owner, ability);
    }

    /// <summary>
    /// Uses a ready item's ability: its holder plays the animation as an attack, the ability runs and the
    /// recharge starts, whatever the ability did. False when the item is not ready.
    /// </summary>
    public static bool Use(Character owner, Item item)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsSpecialReady)
        {
            return false;                                                     // IceCacoon's breaking comes with that ability
        }
        var ability = item.Template.Special!;
        owner.PlayAttack(ability.Animation, allowRotate: false);
        Execute(owner, ability);
        if (string.Equals(ability.Type, "Invisibility", StringComparison.OrdinalIgnoreCase))
        {
            owner.JustCastInvisible = true;
        }
        item.SpecialCooldown = item.Template.SpecialRechargeTime;
        return true;
    }
}
