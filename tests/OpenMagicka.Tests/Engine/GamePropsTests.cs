using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every physics prop of the game as a template, standing in a world (ENTITIES_AI.md §8.4), skipped without MAGICKA_DIR.</summary>
public class GamePropsTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryGameProp_BecomesATemplate_AndStands()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var (count, damageable, movable) = (0, 0, 0);
        using var world = Gameplay.CharacterTests.World();
        foreach (var path in files.ListFiles("Data/PhysicsEntities", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            var asset = path[..^4];
            var template = PropTemplates.From((PhysicsEntityTemplateContent)loader.LoadUncached(asset).Root!, asset);
            var prop = new PhysicsProp(world.Physics, template, Numerics.Matrix4x4.CreateTranslation(count * 10, 0, 0));
            world.Entities.Add(prop);
            (count, damageable, movable) = (count + 1, damageable + (template.IsDamageable ? 1 : 0), movable + (template.Movable ? 1 : 0));
        }
        Gameplay.CharacterTests.Run(world, 0.5f);
        output.WriteLine($"{count} props, {damageable} damageable, {movable} moving");
        Assert.Equal((64, 63, 2), (count, damageable, movable));
        Assert.Equal(count, world.Entities.Entities.Count(e => e is PhysicsProp));
    }
}
