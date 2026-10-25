using OpenMagicka.Content;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// Every cue's name by the hash of its lowered name, from <c>Audio/SoundList</c> (docs/engine/ARCHITECTURE.md §8):
/// the data names sounds by hash, XACT by name. The original hashes some names as written, so an upper-case letter
/// there finds nothing; ours lowers them all.
/// </summary>
public sealed class SoundNames
{
    public const string Asset = "Audio/SoundList";

    private readonly Dictionary<int, string> names = [];

    public SoundNames(IEnumerable<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            if (!string.IsNullOrEmpty(name))
            {
                this.names[LegacyStringHash.Lower(name)] = name;
            }
        }
    }

    public int Count => names.Count;

    /// <summary>The list in the game's content; none without it.</summary>
    public static SoundNames Load(ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new SoundNames(loader.Load(Asset).Root as IReadOnlyList<string?> ?? throw new InvalidDataException($"{Asset} is not a list of names"));
    }

    /// <summary>A cue's name by its hash; none for one the game lacks.</summary>
    public string? Find(int hash) => names.GetValueOrDefault(hash);

    /// <summary>A cue's name as the game has it, by a name in any case; none for one the game lacks.</summary>
    public string? Find(string name) => string.IsNullOrEmpty(name) ? null : Find(LegacyStringHash.Lower(name));
}
