using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xna;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>
/// The rules' character and item templates, one per content asset: every holder of an item shares its
/// auras' buffs, so they renew rather than stack, as the original's one copy per item type
/// (docs/engine/ENTITIES_AI.md §8.3.1).
/// </summary>
public sealed class TemplateLibrary(ContentLoader loader)
{
    private readonly Dictionary<string, Gameplay.Items.ItemTemplate> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Gameplay.Characters.CharacterTemplate> characters = new(StringComparer.OrdinalIgnoreCase);

    public ContentLoader Loader { get; } = loader ?? throw new ArgumentNullException(nameof(loader));

    /// <summary>The item an asset refers to.</summary>
    public Gameplay.Items.ItemTemplate Item(string referringAsset, ExternalReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var asset = Loader.ResolveReference(referringAsset, reference)
            ?? throw new InvalidDataException($"reference '{reference.Path}' from '{referringAsset}' leaves the content root");
        return Item(asset);
    }

    public Gameplay.Items.ItemTemplate Item(string asset)
    {
        if (!items.TryGetValue(asset, out var item))
        {
            item = Loader.Load(asset).Root is ItemContent content
                ? ItemTemplates.From(content, ItemTemplates.Model(Loader, asset, content))
                : throw new InvalidDataException($"'{asset}' is not an item");
            items[asset] = item;
        }
        return item;
    }

    /// <summary>A character template with its slots filled.</summary>
    public Gameplay.Characters.CharacterTemplate Character(string asset)
    {
        if (!characters.TryGetValue(asset, out var character))
        {
            character = Loader.Load(asset).Root is CharacterTemplateContent content
                ? CharacterTemplates.From(this, asset, content)
                : throw new InvalidDataException($"'{asset}' is not a character template");
            characters[asset] = character;
        }
        return character;
    }
}
