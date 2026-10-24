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
}

/// <summary>The game's content as script assets: items and props from the template library, each read once.</summary>
public sealed class ContentScriptAssets(TemplateLibrary library) : IScriptAssets
{
    private readonly TemplateLibrary library = library ?? throw new ArgumentNullException(nameof(library));
    private readonly Dictionary<string, ItemTemplate?> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Gameplay.Props.PropTemplate?> props = new(StringComparer.OrdinalIgnoreCase);

    public Gameplay.Props.PropTemplate? Prop(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!props.TryGetValue(asset, out var prop))
        {
            prop = library.Loader.Files.Exists(asset + ".xnb") ? LoadProp(asset) : null;
            props[asset] = prop;
        }
        return prop;
    }

    private Gameplay.Props.PropTemplate? LoadProp(string asset)
    {
        try
        {
            return library.Loader.Load(asset).Root is Content.Magicka.PhysicsEntityTemplateContent content ? PropTemplates.From(content, asset) : null;
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
