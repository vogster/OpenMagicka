using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Engine.Scripting;

/// <summary>Where scripts find what they put in the world, beyond the characters (the bestiary's).</summary>
public interface IScriptAssets
{
    /// <summary>The item of an asset ("data/items/wizard/…"); null when there is none.</summary>
    ItemTemplate? Item(string asset);
}

/// <summary>The game's content as script assets: items from the template library, each read once.</summary>
public sealed class ContentScriptAssets(TemplateLibrary library) : IScriptAssets
{
    private readonly TemplateLibrary library = library ?? throw new ArgumentNullException(nameof(library));
    private readonly Dictionary<string, ItemTemplate?> items = new(StringComparer.OrdinalIgnoreCase);

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
