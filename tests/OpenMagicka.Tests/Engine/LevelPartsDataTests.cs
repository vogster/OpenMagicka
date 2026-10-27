using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Physics;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every level's moving parts, from the game's files (OM-74): their collision loads, and they play through.</summary>
public class LevelPartsDataTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryLevelsParts_LoadTheirCollision_AndPlayThroughToTheirEnd()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        int levels = 0, parts = 0, solid = 0, shields = 0, lights = 0, riding = 0, suns = 0;
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not LevelModelContent level)
            {
                continue;
            }
            levels++;
            var order = OpenMagicka.Engine.Rendering.LevelParts.Order(level.AnimatedParts);
            lights += order.Sum(p => p.Lights.Count);
            riding += OpenMagicka.Engine.Rendering.PartLight.Of(level).Count;
            suns += order.SelectMany(p => p.Lights).Count(a => level.Lights.Any(l => l.Type == LevelLightType.Directional
                && string.Equals(l.Name, a.LightName, StringComparison.OrdinalIgnoreCase)));
            using var world = new PhysicsWorld();
            var places = new ScriptPlaces(level);
            places.UpdateParts(0);
            solid += LevelCollision.LoadParts(world, places).Count;
            foreach (var part in places.AllParts)
            {
                parts++;
                shields += part.Content.AffectShields ? 1 : 0;
                part.Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
            }
            var longest = places.AllParts.Select(p => p.Content.AnimationDuration).DefaultIfEmpty(0).Max();
            for (var t = 0f; t <= longest + 1; t += 0.25f)
            {
                world.Step(1f / 60);
                places.UpdateParts(0.25f);
            }
            Assert.All(places.AllParts, p => Assert.False(p.IsPlaying, $"{path}: {p.Name} still plays at {p.Time} of {p.Content.AnimationDuration}"));
            Assert.All(places.AllParts.Where(p => p.Collision is not null), p => Assert.Empty(p.Collision!.Carried));
        }
        output.WriteLine($"{levels} levels: {parts} parts, {solid} with a collision, {shields} that move shields; {lights} lights on parts, {riding} found, {suns} directional");
        Assert.Equal((132, 1060, 270, 1060), (levels, parts, solid, shields));                  // 265 meshes and 5 liquids with collision (OM-88); every part would carry shields, ours stay put (§9.7)
        Assert.Equal((14, 14, 1), (lights, riding, suns));                                        // hl_s2's land carries the sun
    }
}
