using System.Collections.Concurrent;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content;

/// <summary>
/// Loads XNB assets by their asset path (relative to Content, without extension, any case) and
/// resolves the external references between them. Loaded content is immutable, so one cached
/// instance is shared by every caller and thread.
/// </summary>
public sealed class ContentLoader(ContentFileSystem files, XnbReaderRegistry registry)
{
    private const string Extension = ".xnb";

    private readonly ConcurrentDictionary<string, Lazy<XnbContent>> cache = new();

    public ContentFileSystem Files { get; } = files;

    /// <summary>
    /// Loads (once) and returns an asset. A failed load is not cached, so a later call retries.
    /// Readers must not load other assets themselves: references are resolved by the caller.
    /// </summary>
    public XnbContent Load(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        var normalized = ContentFileSystem.Normalize(assetPath)
            ?? throw new FileNotFoundException($"invalid asset path '{assetPath}'", assetPath);
        var key = normalized.ToLowerInvariant();
        var entry = cache.GetOrAdd(key, _ => new Lazy<XnbContent>(() => Read(normalized)));
        try
        {
            return entry.Value;
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string, Lazy<XnbContent>>(key, entry));
            throw;
        }
    }

    /// <summary>
    /// Reads an asset without caching it: for large assets used once, such as the viewer's level
    /// models and their textures, which would otherwise stay in memory for the whole session.
    /// </summary>
    public XnbContent LoadUncached(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        return Read(ContentFileSystem.Normalize(assetPath) ?? throw new FileNotFoundException($"invalid asset path '{assetPath}'", assetPath));
    }

    /// <summary>Asset path of an external reference found in <paramref name="referringAsset"/>, or null if it leaves Content.</summary>
    public string? ResolveReference(string referringAsset, ExternalReference reference) =>
        ContentFileSystem.Combine(referringAsset, reference.Path);

    public XnbContent LoadReference(string referringAsset, ExternalReference reference) =>
        Load(ResolveReference(referringAsset, reference)
            ?? throw new FileNotFoundException($"reference '{reference.Path}' from '{referringAsset}' leaves the content root"));

    private XnbContent Read(string assetPath)
    {
        var bytes = Files.ReadAllBytes(assetPath + Extension);
        try
        {
            return XnbContent.Load(XnbFile.Read(bytes), registry);
        }
        catch (Exception e) when (e is InvalidDataException or IOException and not FileNotFoundException)
        {
            throw new InvalidDataException($"{assetPath}: {e.Message}", e);
        }
    }
}
