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
    private readonly Dictionary<object, Rendering.ModelLook> looks = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// What a template this library (or the scripts' assets over it) made looks like: a character's first
    /// model variant, an item's model, a prop's own; null for one it did not make, or one with no model.
    /// </summary>
    public Rendering.ModelLook? LookOf(object template) => looks.GetValueOrDefault(template);

    internal void Remember(object template, Rendering.ModelLook look) => looks[template] = look;

    private void Remember(object template, string asset, ExternalReference? model, System.Numerics.Vector3 tint)
    {
        if (model is not null && Loader.ResolveReference(asset, model) is { } path)
        {
            looks[template] = new Rendering.ModelLook(path, tint);
        }
    }

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
                ? ItemTemplates.From(content, ItemTemplates.Model(Loader, asset, content), ItemTemplates.Projectile(Loader, asset, content))
                : throw new InvalidDataException($"'{asset}' is not an item");
            Remember(item, asset, ((ItemContent)Loader.Load(asset).Root!).Model, System.Numerics.Vector3.One);
            items[asset] = item;
        }
        return item;
    }

    /// <summary>A character template with its slots filled.</summary>
    public Gameplay.Characters.CharacterTemplate Character(string asset)
    {
        if (!characters.TryGetValue(asset, out var character))
        {
            var content = Loader.Load(asset).Root as CharacterTemplateContent ?? throw new InvalidDataException($"'{asset}' is not a character template");
            character = CharacterTemplates.From(this, asset, content);
            if (content.Models is [var first, ..])
            {
                Remember(character, asset, first.Model, new System.Numerics.Vector3(first.Tint.X, first.Tint.Y, first.Tint.Z));
            }
            characters[asset] = character;
        }
        return character;
    }
}
