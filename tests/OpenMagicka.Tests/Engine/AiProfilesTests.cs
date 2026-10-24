using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Engine;

/// <summary>The templates' minds from the game files (ENTITIES_AI.md §7), skipped without MAGICKA_DIR.</summary>
public class AiProfilesTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryTemplatesAbilities_AreRead_AndTheirExpressionsWeighed()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var world = World();
        using var _ = world;
        var self = Animated(world, Goblin, at: new Vector3(0, 0.9f, 0));
        var target = Animated(world, Goblin with { Factions = OpenMagicka.Gameplay.Characters.Factions.Human }, at: new Vector3(5, 0.9f, 2));
        Run(world, Tick);
        var args = new FuzzyArguments(self, target, 12);
        var (templates, abilities, expressions, differing) = (0, 0, new HashSet<string>(), new List<string>());
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.Load(path[..^4]).Root is not CharacterTemplateContent content)
            {
                continue;
            }
            var profile = AiProfiles.From(content);
            templates++;
            abilities += profile.Abilities.Count;
            foreach (var (ability, read) in profile.Abilities.Zip(content.Abilities))
            {
                if (read.Fuzzy is not { } text || !expressions.Add(text))
                {
                    continue;
                }
                var value = ability.Desire!.Value(args);
                if (float.IsInfinity(value))
                {
                    output.WriteLine($"infinite, as the original: {path}: {text}");          // distExp(0,0): a range of nothing
                }
                var plain = FuzzyExpression.Parse(text, leftToRight: true);
                if (plain.ToString() != ability.Desire.ToString() && plain.Value(args) is var other)
                {
                    Assert.True(MathF.Abs(other - value) < 1e-4f || (float.IsInfinity(value) && value == other), $"{text}: the order of one rank changes the value");   // only + and * chains regroup
                    differing.Add($"{text.Replace("\n", " ").Replace("\r", "")}  →  {ability.Desire} = {value}; left to right {plain} = {other}");
                }
            }
        }
        output.WriteLine($"{templates} templates, {abilities} abilities, {expressions.Count} distinct expressions; {differing.Count} read otherwise left to right:");
        differing.ForEach(output.WriteLine);
        Assert.True(templates > 300 && expressions.Count > 250);
    }
}
