using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Characters in a full game world on the game's levels (skipped without MAGICKA_DIR).</summary>
public class GameWorldSpikeTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryCharacterTemplate_MakesACharacter()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        using var world = CharacterTests.World();
        var templates = loader.Files.ListFiles("Data/Characters", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => loader.LoadUncached(p[..^4]).Root)
            .OfType<CharacterTemplateContent>()
            .Select(CharacterTemplates.From)
            .ToList();
        var failures = new List<string>();
        foreach (var template in templates)
        {
            try
            {
                world.Physics.Remove(new Character(template, world.Physics, new System.Numerics.Vector3(0, 10, 0)).Body.Body);
            }
            catch (ArgumentException e)
            {
                failures.Add($"{template.Id}: {e.Message}");
            }
        }
        output.WriteLine($"{templates.Count} templates: {templates.Count(t => t.Undying)} undying ({templates.Count(t => t.Undying && t.UndieHitPoints <= 0)} with no revival HP), {templates.Count(t => t.Flies)} flying, {templates.Count(t => !t.CanBeKnockedDown)} that never fall");
        Assert.Equal(338, templates.Count);
        Assert.Empty(failures);
    }

    [GameContentFact]
    public void CharactersSetOnEveryLevel_StandUnhurt()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        int levels = 0, characters = 0, hurt = 0, standing = 0;
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not LevelModelContent level)
            {
                continue;
            }
            using var world = new GameWorld();
            if (LevelCollision.AddTo(world.Physics, level) == 0)
            {
                continue;
            }
            levels++;
            var nav = level.NavMesh;
            var stride = Math.Max(1, nav.Triangles.Count / 12);
            for (var i = 0; i < nav.Triangles.Count; i += stride)
            {
                var t = nav.Triangles[i];
                var a = nav.Vertices[t.A] + nav.Vertices[t.B] + nav.Vertices[t.C];
                var centre = new Vector3(a.X, a.Y, a.Z) / 3;
                if (world.Physics.CastSegment(centre + new Vector3(0, 2, 0), centre - new Vector3(0, 10, 0)) is { Material: not null } ground)
                {
                    world.Entities.Add(new Character(CharacterTests.Goblin, world.Physics, ground.Position + new Vector3(0, 1.4f, 0), random: new Random(i)));
                }
            }
            CharacterTests.Run(world, 2);
            foreach (var character in world.Characters)
            {
                characters++;
                hurt += character.HitPoints < character.MaxHitPoints ? 1 : 0;
                standing += character.Body.IsTouchingGround && !character.IsDead ? 1 : 0;
            }
        }
        output.WriteLine($"{levels} levels, {characters} characters set 0.5 m above the ground: {standing} standing after 2 s, {hurt} hurt");
        Assert.True(levels > 100);
        Assert.Equal(0, hurt);
        Assert.True(standing > characters * 95 / 100, $"only {standing} of {characters} standing");
    }
}
