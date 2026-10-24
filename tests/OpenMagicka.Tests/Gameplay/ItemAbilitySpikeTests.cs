using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The game's items with passive and special abilities (ENTITIES_AI.md §8.1.6, §8.1.7; skipped without MAGICKA_DIR).</summary>
public class ItemAbilitySpikeTests(ITestOutputHelper output)
{
    private static List<ItemTemplate> Items()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        return [.. loader.Files.ListFiles("Data/Items", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => library.Item(p[..^4]))];
    }

    [GameContentFact]
    public void TheGamesPassives_AreTheOnesRecorded()
    {
        var passives = Items().Where(i => i.PassiveAbility != PassiveAbility.None).ToList();
        var kinds = passives.GroupBy(i => i.PassiveAbility).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));

        Assert.Equal(11, passives.Count);                                            // as ENTITIES_AI §8.1.6 records
        Assert.Equal((3, 2, 2, 1), (kinds[PassiveAbility.MoveSpeed], kinds[PassiveAbility.Gungner], kinds[PassiveAbility.MasterSword], kinds[PassiveAbility.Glow]));
        Assert.Equal([0.4f, 1.5f, 2f], passives.Where(i => i.PassiveAbility == PassiveAbility.MoveSpeed).Select(i => i.PassiveValue).Order());
        Assert.All(passives.Where(i => i.PassiveAbility == PassiveAbility.Gungner), i => Assert.NotNull(i.Ranged));
    }

    [GameContentFact]
    public void TheGamesSpecialAbilities_AreTheOnesRecorded()
    {
        var specials = Items().Where(i => i.Special is not null).ToList();
        var working = specials.Count(i => SpecialAbilities.Works(i.Special!.Type));
        output.WriteLine($"{specials.Count} items with a special ability, {working} working now; types: {string.Join(", ", specials.Select(i => i.Special!.Type).Distinct(StringComparer.OrdinalIgnoreCase).Order())}");

        Assert.Equal(64, specials.Count);                                            // as ENTITIES_AI §8.1.7 records
        Assert.Equal((61, 2, 1), (specials.Count(i => i.WeaponClass == WeaponClass.Staff), specials.Count(i => i.WeaponClass == WeaponClass.Default),
            specials.Count(i => i.WeaponClass == WeaponClass.Handgun)));
        Assert.Equal(7, specials.Count(i => string.Equals(i.Special!.Type, "SpecialAnimation", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(8, working);                                                    // the seven animations and one invisibility
        Assert.All(specials, i => Assert.False(string.IsNullOrEmpty(i.Special!.Animation)));
    }
}
