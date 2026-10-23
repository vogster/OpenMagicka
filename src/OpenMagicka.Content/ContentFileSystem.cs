using System.Collections.Frozen;

namespace OpenMagicka.Content;

/// <summary>
/// Read-only view of the game's Content folder that finds files regardless of letter case and
/// separator style. The original runs on Windows, and its code and files disagree on case
/// (Additionalmusic vs AdditionalMusic), so on Linux and macOS every lookup goes through an index
/// built once from the directory tree. Paths that leave the root are never resolved, and
/// symbolic links and junctions are skipped so that no file outside the root is reachable.
/// The index is a snapshot taken on first use: files added later are not seen.
/// </summary>
public sealed class ContentFileSystem
{
    private readonly Lazy<FrozenDictionary<string, string>> index;

    public ContentFileSystem(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"content directory not found: {full}");
        }
        Root = full;
        index = new Lazy<FrozenDictionary<string, string>>(BuildIndex);
    }

    public string Root { get; }

    /// <summary>The real path of a file inside the root, or null when there is none.</summary>
    public string? Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var key = Normalize(path)?.ToLowerInvariant();
        return key is not null && index.Value.TryGetValue(key, out var real) ? real : null;
    }

    public bool Exists(string path) => Resolve(path) is not null;

    public byte[] ReadAllBytes(string path) =>
        File.ReadAllBytes(Resolve(path) ?? throw new FileNotFoundException($"content file not found: {path}", path));

    /// <summary>
    /// Resolves an external reference written relative to the directory of the referring asset
    /// (both without extension). Keeps letter case; returns null if the result leaves the root.
    /// </summary>
    public static string? Combine(string referringAsset, string reference)
    {
        ArgumentNullException.ThrowIfNull(referringAsset);
        ArgumentNullException.ThrowIfNull(reference);
        var asset = Normalize(referringAsset);
        if (asset is null)
        {
            return null;
        }
        var slash = asset.LastIndexOf('/');
        var directory = slash < 0 ? "" : asset[..slash];
        return Normalize(directory.Length == 0 ? reference : directory + "/" + reference);
    }

    /// <summary>
    /// Forward slashes, no empty or "." segments, ".." applied; null when empty or when ".."
    /// climbs above the root.
    /// </summary>
    public static string? Normalize(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private FrozenDictionary<string, string> BuildIndex()
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var files = new Dictionary<string, string>();
        // Sorted so that, on case-sensitive systems, the same spelling wins on every machine.
        foreach (var file in Directory.EnumerateFiles(Root, "*", options).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/').ToLowerInvariant();
            files.TryAdd(relative, file);
        }
        return files.ToFrozenDictionary();
    }
}
