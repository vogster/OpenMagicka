using OpenMagicka.Gameplay.Animation;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>
/// Where a level finds the characters it makes at run time (summons, hatchings): a template and its
/// clips by asset name, as <c>data/characters/zombie</c>. The engine fills it from the content.
/// </summary>
public interface IBestiary
{
    /// <summary>The template and clips of the asset; null when there is none.</summary>
    (CharacterTemplate Template, CharacterAnimations? Animations)? Find(string asset);
}

/// <summary>A bestiary of known templates, by asset name (ignoring case).</summary>
public sealed class Bestiary : IBestiary
{
    private readonly Dictionary<string, (CharacterTemplate, CharacterAnimations?)> known = new(StringComparer.OrdinalIgnoreCase);

    public Bestiary Add(string asset, CharacterTemplate template, CharacterAnimations? animations = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        ArgumentNullException.ThrowIfNull(template);
        known[asset] = (template, animations);
        return this;
    }

    public (CharacterTemplate Template, CharacterAnimations? Animations)? Find(string asset) =>
        known.TryGetValue(asset, out var found) ? found : null;
}
