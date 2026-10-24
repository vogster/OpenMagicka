using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's item as the rules need it (docs/engine/ENTITIES_AI.md §8.1.1).</summary>
public static class ItemTemplates
{
    /// <param name="model">The item's model, whose bones give its points (§4.3); null puts them at its origin.</param>
    public static Gameplay.Items.ItemTemplate From(ItemContent content, Content.Xna.ModelContent? model = null)
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
            content.Bound,
            Point(model, "attach0"),
            Point(model, "attach1"),
            Events(content.Melee.Conditions));
        template.Validate();
        return template;
    }

    /// <summary>An item referred to from another asset (a template's slot).</summary>
    public static Gameplay.Items.ItemTemplate Load(ContentLoader loader, string referringAsset, Content.Xna.ExternalReference reference)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var asset = loader.ResolveReference(referringAsset, reference)
            ?? throw new InvalidDataException($"reference '{reference.Path}' from '{referringAsset}' leaves the content root");
        return loader.Load(asset).Root is ItemContent item
            ? From(item, Model(loader, asset, item))
            : throw new InvalidDataException($"'{reference.Path}' from '{referringAsset}' is not an item");
    }

    /// <summary>The item's model, from the item's own asset.</summary>
    private static Content.Xna.ModelContent? Model(ContentLoader loader, string itemAsset, ItemContent item) =>
        item.Model is { } model ? loader.LoadReference(itemAsset, model).Root as Content.Xna.ModelContent : null;

    /// <summary>
    /// A named bone's transform with each parent's multiplied in before it, as the original (the
    /// last bone of the name wins); null without one.
    /// </summary>
    private static System.Numerics.Matrix4x4? Point(Content.Xna.ModelContent? model, string name)
    {
        if (model is null)
        {
            return null;
        }
        System.Numerics.Matrix4x4? point = null;
        for (var i = 0; i < model.Bones.Count; i++)
        {
            if (!string.Equals(model.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var matrix = XnaConversions.Matrix(model.Bones[i].Transform);
            var steps = 0;
            for (var parent = model.Bones[i].Parent; parent is { } p; parent = model.Bones[p].Parent)
            {
                if (++steps > model.Bones.Count)
                {
                    throw new InvalidDataException($"the bones above {name} go round in a circle");
                }
                matrix = XnaConversions.Matrix(model.Bones[p].Transform) * matrix;
            }
            point = matrix;
        }
        return point;
    }

    /// <summary>A source's event groups; the events that wait for later systems are left out (§8.2).</summary>
    internal static IReadOnlyList<Gameplay.Items.EventGroup> Events(IReadOnlyList<EventCollectionContent> groups) =>
        [.. groups.Select(g => new Gameplay.Items.EventGroup(
            new Gameplay.Items.EventCondition((Gameplay.Items.EventTrigger)g.Condition, g.Hitpoints, (Gameplay.Spells.Elements)(int)g.Elements, g.Threshold, g.Time, g.Repeat),
            [.. g.Events.Select(Event).OfType<Gameplay.Items.ItemEvent>()]))];

    private static Gameplay.Items.ItemEvent? Event(EventContent e) => e switch
    {
        DamageEvent d => new Gameplay.Items.DamageEvent(Damage(d.Damage), d.VelocityBased),
        DamageOwnerEvent d => new Gameplay.Items.DamageOwnerEvent(Damage(d.Damage), d.VelocityBased),
        SoundEvent s => new Gameplay.Items.SoundEvent(s.Cue, (int)s.Banks, s.Magnitude),
        EffectEvent f => new Gameplay.Items.EffectEvent(f.Effect, f.Follow, f.WorldAlign),
        _ => null,
    };

    internal static Gameplay.Combat.DamageEntry Damage(DamageContent d) =>
        new((Gameplay.Combat.AttackProperties)(int)d.Attack, (Gameplay.Spells.Elements)(int)d.Elements, d.Amount, d.Magnitude);

    /// <summary>Overrides on top of the neutral table, as a template's.</summary>
    internal static ResistanceTable Resistances(IReadOnlyList<ResistanceContent> overrides) =>
        overrides.Aggregate(ResistanceTable.Neutral, (table, r) =>
            table.With((Gameplay.Spells.Elements)(int)r.Elements, r.Multiplier, r.Modifier, r.StatusResistance));
}
