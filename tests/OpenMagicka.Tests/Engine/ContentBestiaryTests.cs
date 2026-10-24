using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Spells.Magicks;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Engine;

/// <summary>The bestiary the summons find their characters in, from the game files (MAGICKS.md §7).</summary>
public class ContentBestiaryTests
{
    private static ContentBestiary Bestiary() =>
        new(new TemplateLibrary(new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka)));

    [GameContentFact]
    public void EverySummon_IsInTheGameFiles_WithItsRisingClip()
    {
        var bestiary = Bestiary();

        var zombie = bestiary.Find(UndeadRising.Asset);
        Assert.NotNull(zombie);
        Assert.NotNull(zombie.Value.Animations);
        foreach (var element in new[] { "arcane", "cold", "fire", "life", "lightning", "poison", "steam", "water" })
        {
            var elemental = bestiary.Find($"data/characters/elemental_{element}");
            Assert.True(elemental is not null, element);
        }
    }

    [GameContentFact]
    public void AMissingOrForeignAsset_IsNone_AndRemembered()
    {
        var bestiary = Bestiary();

        Assert.Null(bestiary.Find("data/characters/no_such_thing"));
        Assert.Null(bestiary.Find("data/characters/no_such_thing"));
        Assert.Same(bestiary.Find(UndeadRising.Asset)!.Value.Template, bestiary.Find("Data/Characters/Zombie")!.Value.Template);
    }
}
