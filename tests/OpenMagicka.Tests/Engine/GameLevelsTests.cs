using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every game level played (LEVELS_TRIGGERS.md §6): started, through each of its scenes, and back at a checkpoint; skipped without MAGICKA_DIR.</summary>
public class GameLevelsTests(ITestOutputHelper output)
{
    private const float Tick = 1f / 60;

    [GameContentFact]
    public void EveryLevel_Starts_GoesThroughItsScenes_AndRestartsAtACheckpoint()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var services = new LevelServices(new ContentBestiary(library), new ContentScriptAssets(library), ScriptRegistry.Engine());
        var wizard = library.Character("data/characters/wizard");
        var (levels, entered, restarts, changes) = (0, 0, 0, 0);
        foreach (var path in files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)))
        {
            var definition = LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);
            var session = new GameSession();
            using var world = new GameWorld(session: session);
            var player = session.AddPlayer();
            player.Avatar = new Character(wizard, world.Physics, new Numerics.Vector3(0, 50, 0)) { IsPlayer = true, Player = player };
            var level = new PlayedLevel(definition, new ContentLevel(files, loader), world, services);
            level.Start();
            Run(level, 1);
            changes += level.TakeRequests().Count(r => r is SceneChangedRequest);
            level.TakeCheckpoint([System.Numerics.Matrix4x4.Identity], []);
            foreach (var scene in level.Scenes.ToList())
            {
                level.ChangeScene(new SpawnPoint(scene.Key, [], SpawnPlayers: true), ScriptTransition.None, 0, keep: true);
                Run(level, 0.25f);
                entered += level.Current == scene ? 1 : 0;
            }
            restarts += level.RestartAtCheckpoint() ? 1 : 0;
            Run(level, 0.25f);
            level.End();
            levels++;
        }
        output.WriteLine($"{levels} levels, {entered} scenes entered, {changes} changes the scripts asked, {restarts} restarts");
        Assert.Equal(48, levels);
        Assert.Equal(levels, restarts);
        Assert.True(entered >= 141);
    }

    [Fact]
    public void TheCampaignsChapters_FollowEachOther()
    {
        Assert.Equal(12, Campaign.Chapters.Count);
        Assert.Equal("Levels/Mountaindale.lvl", Campaign.Next("levels/wizard_castle.lvl"));
        Assert.Null(Campaign.Next("Levels/EndOfWorld2.lvl"));
        Assert.Equal("Levels/Tsar/Tsar_Rlyeh.lvl", Campaign.Next("Levels/Tsar/Tsar_Mountaindale.lvl"));
        Assert.Null(Campaign.Next("Levels/Challenges/ch_arena.lvl"));
    }

    private static void Run(PlayedLevel level, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            level.Tick(Tick);
        }
    }
}
