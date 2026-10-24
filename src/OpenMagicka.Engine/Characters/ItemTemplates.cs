using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's item as the rules need it (docs/engine/ENTITIES_AI.md §8.1.1).</summary>
public static class ItemTemplates
{
    public static Gameplay.Items.ItemTemplate From(ItemContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var template = new Gameplay.Items.ItemTemplate(
            content.Name,
            (Gameplay.Items.WeaponClass)content.WeaponClass,
            content.BlockValue,
            Resistances(content.Resistances),
            (Gameplay.Items.PassiveAbility)content.PassiveAbility,
            content.PassiveAbilityValue,
            content.CooldownTime,
            content.Melee.Range,
            content.Melee.MultiHit,
            content.Pickable,
            content.Bound);
        template.Validate();
        return template;
    }

    /// <summary>An item referred to from another asset (a template's slot).</summary>
    public static Gameplay.Items.ItemTemplate Load(ContentLoader loader, string referringAsset, Content.Xna.ExternalReference reference)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return loader.LoadReference(referringAsset, reference).Root is ItemContent item
            ? From(item)
            : throw new InvalidDataException($"'{reference.Path}' from '{referringAsset}' is not an item");
    }

    /// <summary>Overrides on top of the neutral table, as a template's.</summary>
    internal static ResistanceTable Resistances(IReadOnlyList<ResistanceContent> overrides) =>
        overrides.Aggregate(ResistanceTable.Neutral, (table, r) =>
            table.With((Gameplay.Spells.Elements)(int)r.Elements, r.Multiplier, r.Modifier, r.StatusResistance));
}
