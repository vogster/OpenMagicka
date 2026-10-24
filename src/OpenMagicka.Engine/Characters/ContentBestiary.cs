using OpenMagicka.Content.Magicka;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>
/// The level's bestiary from the game files: a template and its animation sets by asset name, loaded
/// once; an asset that is missing, broken or not a character template is none (the summon then
/// makes nothing). It loads on the logic thread, as the level's other content: one bestiary per
/// level, not shared across threads.
/// </summary>
public sealed class ContentBestiary(TemplateLibrary library) : Gameplay.Characters.IBestiary
{
    private readonly Dictionary<string, (Gameplay.Characters.CharacterTemplate, Gameplay.Animation.CharacterAnimations?)?> known =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TemplateLibrary library = library ?? throw new ArgumentNullException(nameof(library));

    public (Gameplay.Characters.CharacterTemplate Template, Gameplay.Animation.CharacterAnimations? Animations)? Find(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!known.TryGetValue(asset, out var found))
        {
            found = Load(asset);
            known[asset] = found;
        }
        return found;
    }

    private (Gameplay.Characters.CharacterTemplate, Gameplay.Animation.CharacterAnimations?)? Load(string asset)
    {
        if (!library.Loader.Files.Exists(asset + ".xnb"))
        {
            return null;
        }
        try
        {
            if (library.Loader.Load(asset).Root is not CharacterTemplateContent content)
            {
                return null;
            }
            return (library.Character(asset), CharacterAnimationSets.From(library.Loader, asset, content));
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;
        }
    }
}
