using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Engine.Scripting;

/// <summary>Where scripts find what they put in the world, beyond the characters (the bestiary's).</summary>
public interface IScriptAssets
{
    /// <summary>The item of an asset ("data/items/wizard/…"); null when there is none.</summary>
    ItemTemplate? Item(string asset);

    /// <summary>The physics prop of an asset ("data/physicsentities/…"); null when there is none.</summary>
    Gameplay.Props.PropTemplate? Prop(string asset) => null;

    /// <summary>A named boss of the game's own code (grimnir, fafnir, vlad…, OM-42); null for one not yet made.</summary>
    IFightBoss? Boss(string name) => null;

    /// <summary>The asset an item or prop it gave was read from (what a saved checkpoint names it by); null for one it did not give.</summary>
    string? AssetOf(object template) => null;
}

/// <summary>The game's content as script assets: items and props from the template library, each read once.</summary>
public sealed class ContentScriptAssets(TemplateLibrary library) : IScriptAssets
{
    private readonly TemplateLibrary library = library ?? throw new ArgumentNullException(nameof(library));
    private readonly Dictionary<string, ItemTemplate?> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Gameplay.Props.PropTemplate?> props = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, string> propAssets = new(ReferenceEqualityComparer.Instance);

    public string? AssetOf(object template) => template switch
    {
        ItemTemplate item => library.AssetOf(item),
        Gameplay.Props.PropTemplate prop => propAssets.GetValueOrDefault(prop),
        _ => null,
    };

    public Gameplay.Props.PropTemplate? Prop(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!props.TryGetValue(asset, out var prop))
        {
            prop = library.Loader.Files.Exists(asset + ".xnb") ? LoadProp(asset) : null;
            props[asset] = prop;
            if (prop is not null)
            {
                propAssets[prop] = asset;
            }
        }
        return prop;
    }

    private Gameplay.Props.PropTemplate? LoadProp(string asset)
    {
        try
        {
            if (library.Loader.Load(asset).Root is not Content.Magicka.PhysicsEntityTemplateContent content)
            {
                return null;
            }
            var prop = PropTemplates.From(content, asset);
            if (content.Model is not null)
            {
                library.Remember(prop, new Rendering.ModelLook(asset, System.Numerics.Vector3.One));   // the model is in the prop's own file
            }
            return prop;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    public ItemTemplate? Item(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!items.TryGetValue(asset, out var item))
        {
            item = library.Loader.Files.Exists(asset + ".xnb") ? Load(asset) : null;
            items[asset] = item;
        }
        return item;
    }

    private ItemTemplate? Load(string asset)
    {
        try
        {
            return library.Item(asset);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or NotSupportedException or FormatException)
        {
            return null;
        }
    }
}
