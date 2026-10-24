using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every game scene's scripts in the trigger engine (LEVELS_TRIGGERS.md §5), skipped without MAGICKA_DIR.</summary>
public class ScriptGameScenesTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryScene_LoadsIntoTheEngine_AndRuns()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var levelPaths = files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase));
        var scenePaths = levelPaths.SelectMany(p => LevelReader.Read(new MemoryStream(files.ReadAllBytes(p)), p).Scenes)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var registry = ScriptRegistry.Engine();
        var library = new OpenMagicka.Engine.Characters.TemplateLibrary(loader);
        var (bestiary, assets) = (new OpenMagicka.Engine.Characters.ContentBestiary(library), new ContentScriptAssets(library));
        var (scenes, triggers, areas, fired, spawned, items, effects, parts, props) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
        var missing = new SortedSet<string>();
        foreach (var path in scenePaths)
        {
            var definition = SceneReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);
            var model = loader.LoadUncached(definition.Model).Root as LevelModelContent;
            using var world = Gameplay.CharacterTests.World();
            var level = new RunningLevel(world, model, definition, bestiary, registry: registry, assets: assets);
            var (scene, places) = (level.Scene, level.Scene.Places);
            scene.TriggerFired += (_, _, _) => fired++;
            for (var i = 0; i < 120; i++)
            {
                level.Tick(Gameplay.CharacterTests.Tick);
            }
            spawned += world.Characters.Count();
            items += world.Entities.Entities.Count(e => e is OpenMagicka.Gameplay.Items.WorldItem);
            props += world.Entities.Entities.Count(e => e is OpenMagicka.Gameplay.Props.PhysicsProp);
            effects += scene.Effects.All.Count(e => e.IsActive);
            parts += places.Parts.Count(p => p.IsPlaying || p.Time > 0);
            level.Leave();
            foreach (var condition in scene.Triggers.SelectMany(t => t.Conditions).SelectMany(b => b).OfType<AreaCondition>())
            {
                if (places.Area(ScriptScene.Hash(condition.Area)) is null)
                {
                    missing.Add($"{Path.GetFileNameWithoutExtension(path)}:{condition.Area}");
                }
            }
            (scenes, triggers, areas) = (scenes + 1, triggers + scene.Triggers.Count(), areas + places.Areas.Count() - 1);
        }
        var unknown = registry.Unknown.ToList();
        output.WriteLine($"{scenes} scenes, {triggers} triggers and points, {areas} areas; {fired} firings, {spawned} characters, {items} items, {props} props, {effects} effects playing and {parts} parts moved in 2 s each; "
            + $"{unknown.Count} kinds still unknown: {string.Join(", ", unknown)}; areas missing: {string.Join(", ", missing)}");
        Assert.True(scenes > 100 && triggers > 2500);
        Assert.True(fired > 0);
        Assert.True(spawned > 100);
        Assert.True(items > 0 && props > 0 && effects > 0 && parts > 0);
        Assert.DoesNotContain(unknown, u => u.StartsWith("if:", StringComparison.Ordinal));      // every condition is known
        Assert.Empty(missing);                                                                     // no area condition looks for an area not there
    }
}
