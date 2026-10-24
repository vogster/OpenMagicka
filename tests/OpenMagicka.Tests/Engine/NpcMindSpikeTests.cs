using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Tests.Content;
using OpenMagicka.Tests.Gameplay;
using Xunit.Abstractions;
using Factions = OpenMagicka.Gameplay.Characters.Factions;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every game template's mind against a foe in front (ENTITIES_AI.md §7), skipped without MAGICKA_DIR.</summary>
public class NpcMindSpikeTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryTemplatesMind_GoesForAFoe_WithoutFailing()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var (minds, targeting, acting) = (0, 0, 0);
        var idle = new List<string>();
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not CharacterTemplateContent content || content.Abilities.Count == 0)
            {
                continue;
            }
            using var world = CharacterTests.World();
            var template = library.Character(path[..^4]);
            var npc = new Character(template, world.Physics, new Vector3(0, 0.5f + content.Stats.Radius + content.Stats.Length / 2, 0),
                random: new Random(1), animations: CharacterAnimationSets.From(loader, path[..^4], content));
            var foe = new Character(CharacterTests.Goblin with { Factions = (template.Factions & Factions.Human) != 0 ? Factions.Undead : Factions.Human, MaxHitPoints = 1e6f },
                world.Physics, new Vector3(3 + content.Stats.Radius, 0.9f, 0), random: new Random(2));
            world.Entities.Add(npc);
            world.Entities.Add(foe);
            minds++;
            var (targeted, acted) = (false, false);
            for (var t = 0f; t < 4; t += CharacterTests.Tick)
            {
                world.Tick(CharacterTests.Tick);
                targeted |= npc.Mind?.CurrentTarget is not null;
                acted |= npc.State == CharacterStates.Attack || npc.State == CharacterStates.Cast || npc.State == CharacterStates.Dash
                    || npc.State == CharacterStates.Jump || npc.State == CharacterStates.Charge || npc.State == CharacterStates.Block;
            }
            targeting += targeted ? 1 : 0;
            acting += acted ? 1 : 0;
            if (!acted)
            {
                idle.Add(content.Id);
            }
        }
        output.WriteLine($"{minds} templates with abilities; {targeting} took a target, {acting} acted; idle: {string.Join(", ", idle)}");
        Assert.True(minds > 250);
        Assert.True(acting > minds / 2, $"{acting} of {minds} acted");
    }
}
