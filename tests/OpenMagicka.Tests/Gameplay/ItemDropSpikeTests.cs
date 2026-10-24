using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The game's items lying in the world (ENTITIES_AI.md §8.1.5; skipped without MAGICKA_DIR).</summary>
public class ItemDropSpikeTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryItemModel_GivesBounds()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var items = loader.Files.ListFiles("Data/Items", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Content: (ItemContent)loader.LoadUncached(p[..^4]).Root!, Template: library.Item(p[..^4])))
            .ToList();
        var sizes = items.Where(i => i.Template.Bounds is not null).Select(i => i.Template.Bounds!.Value.Size).ToList();

        output.WriteLine($"{items.Count} items, {items.Count(i => i.Content.Model is not null)} with a model, {sizes.Count} with bounds; " +
            $"longest side {sizes.Max(s => MathF.Max(s.X, MathF.Max(s.Y, s.Z))):0.00} m, {sizes.Count(s => MathF.Min(s.X, MathF.Min(s.Y, s.Z)) < 1e-3f)} flat");
        Assert.Equal((454, 379, 379), (items.Count, items.Count(i => i.Content.Model is not null), sizes.Count));   // every model has a mesh to read
        Assert.All(sizes, s => Assert.True(s.X < 20 && s.Y < 20 && s.Z < 20, $"{s}"));
    }

    [GameContentFact]
    public void EveryArmedTemplate_DropsWhatItHolds_WhenItDies()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var (armed, dropping, dropped, pickable, lost) = (0, 0, 0, 0, new List<string>());
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not CharacterTemplateContent content)
            {
                continue;
            }
            var template = library.Character(path[..^4]);
            if (template.Equipment is not { Count: > 0 } || template.Undying)
            {
                continue;
            }
            using var world = CharacterTests.World();
            var character = new Character(template, world.Physics, new Vector3(20, 0.5f + content.Stats.Radius + content.Stats.Length / 2, -30),
                random: new Random(1), animations: CharacterAnimationSets.From(loader, path[..^4], content));
            world.Entities.Add(character);
            CharacterTests.Run(world, 0.2f);
            armed++;
            character.Kill();
            CharacterTests.Run(world, 2);
            var lying = world.Entities.Entities.OfType<WorldItem>().ToList();
            (dropping, dropped, pickable) = (dropping + (lying.Count > 0 ? 1 : 0), dropped + lying.Count, pickable + lying.Count(w => w.IsPickable));
            lost.AddRange(lying.Where(w => !(w.Position.Y > -0.05f) || Vector3.Distance(w.Position, character.Position) > 10)
                .Select(w => $"{content.Id}: {w.Item.Template.Id} at {w.Position} (body {character.Position})"));
            Assert.True(character.State == CharacterStates.Dead || character.IsDead, $"{content.Id} is {character.State}");
            Assert.All(character.Equipment, s => Assert.False(s.Item?.Template.Bounds is not null && s.Item.IsAttached, $"{content.Id} kept {s.Item?.Template.Id}"));
        }
        output.WriteLine($"{armed} armed mortal templates; {dropping} dropped {dropped} items ({pickable} pickable); {lost.Count} lost");
        lost.Take(20).ToList().ForEach(output.WriteLine);
        Assert.Empty(lost);                                                          // every dropped item lies by its holder, on the floor
        Assert.Equal((286, 224, 389, 121), (armed, dropping, dropped, pickable));    // as ENTITIES_AI §8.1.5 records
    }
}
