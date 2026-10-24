using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The game's items with passive abilities (ENTITIES_AI.md §8.1.6; skipped without MAGICKA_DIR).</summary>
public class PassiveSpikeTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void TheGamesPassives_AreTheOnesRecorded()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var passives = loader.Files.ListFiles("Data/Items", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => library.Item(p[..^4]))
            .Where(i => i.PassiveAbility != PassiveAbility.None)
            .ToList();
        var kinds = passives.GroupBy(i => i.PassiveAbility).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));

        Assert.Equal(11, passives.Count);                                            // as ENTITIES_AI §8.1.6 records
        Assert.Equal((3, 2, 2, 1), (kinds[PassiveAbility.MoveSpeed], kinds[PassiveAbility.Gungner], kinds[PassiveAbility.MasterSword], kinds[PassiveAbility.Glow]));
        Assert.Equal([0.4f, 1.5f, 2f], passives.Where(i => i.PassiveAbility == PassiveAbility.MoveSpeed).Select(i => i.PassiveValue).Order());
        Assert.All(passives.Where(i => i.PassiveAbility == PassiveAbility.Gungner), i => Assert.NotNull(i.Ranged));
    }
}
