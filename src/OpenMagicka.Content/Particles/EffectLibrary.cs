using System.Collections.Frozen;

namespace OpenMagicka.Content.Particles;

/// <summary>
/// Every visual effect under <c>Effects/</c>, keyed by the legacy hash of the lower-case file
/// name without extension, which is how the game names effects (docs/engine/ARCHITECTURE.md §5.2).
/// Two files with the same name are an error, as in the original. Immutable.
/// </summary>
public sealed class EffectLibrary
{
    private const string EffectsDirectory = "Effects";
    private const string Extension = ".xml";

    private readonly FrozenDictionary<int, VisualEffectContent> effects;

    private EffectLibrary(FrozenDictionary<int, VisualEffectContent> effects) => this.effects = effects;

    public int Count => effects.Count;

    /// <summary>Every effect, in no particular order.</summary>
    public IEnumerable<VisualEffectContent> Effects => effects.Values;

    public static int Id(string name) => LegacyStringHash.Lower(name);

    public static EffectLibrary Load(ContentFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var effects = new Dictionary<int, VisualEffectContent>();
        var sources = new Dictionary<int, string>();
        foreach (var path in files.ListFiles(EffectsDirectory, recursive: true)
            .Where(p => p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)))
        {
            var id = Id(Path.GetFileNameWithoutExtension(path));
            if (sources.TryGetValue(id, out var first))
            {
                throw new InvalidDataException($"{path}: an effect with the same name was already loaded from {first}");
            }
            sources[id] = path;
            effects[id] = Read(files, path);
        }
        return new EffectLibrary(effects.ToFrozenDictionary());
    }

    public VisualEffectContent Get(int id) =>
        effects.TryGetValue(id, out var effect) ? effect : throw new KeyNotFoundException($"no visual effect with ID {id}");

    public bool TryGet(int id, out VisualEffectContent effect) => effects.TryGetValue(id, out effect!);

    public bool TryGet(string name, out VisualEffectContent effect) => TryGet(Id(name), out effect);

    private static VisualEffectContent Read(ContentFileSystem files, string path)
    {
        try
        {
            using var stream = new MemoryStream(files.ReadAllBytes(path));
            return VisualEffectReader.Read(stream);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }
}
