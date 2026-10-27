using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Engine.PlayedLevelTests;
using ScriptAction = OpenMagicka.Engine.Scripting.ScriptAction;

namespace OpenMagicka.Tests.Engine;

/// <summary>A checkpoint kept in a campaign slot across runs (OM-85): written, read back, and taken by a fresh level of the same files.</summary>
public class SavedCheckpointTests
{
    private const string Guards = """
        <Trigger id="guards"><Then><spawn type="orc" nr="2" area="door0" order="idle"/></Then></Trigger>
        <Trigger id="save" autorun="false"><Then><counter name="n" action="set" value="3"/><triggerCheckpoint spawnPoint="door"/></Then></Trigger>
        <Trigger id="later" autorun="false"><Then><counter name="n" action="set" value="9"/></Then></Trigger>
        """;

    /// <summary>A level played to its checkpoint (one orc hurt), the checkpoint written and read back as text.</summary>
    private static (SavedCheckpoint Saved, float Hurt) Played(string first = Guards, string second = "", Action<PlayedLevel>? before = null)
    {
        var (level, world, _) = Level(first, second);
        using var _ = world;
        level.Start();
        Run(level, 0.3f);
        before?.Invoke(level);
        var orc = world.Characters.FirstOrDefault(c => !c.IsPlayer);
        orc?.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, orc.Position);
        level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("save"), null);
        Run(level, 0.1f);
        var saved = SavedCheckpoint.From(level.Checkpoint!, "level", "dialogs", level.Services.Assets);
        return (SavedCheckpoint.FromJson(saved.ToJson())!, orc?.HitPoints ?? 0);
    }

    [Fact]
    public void AFreshLevel_ResumesAtTheSavedCheckpoint_ItsCountersSurvivorsPropAndSpawnPoint()
    {
        var (saved, hurt) = Played();
        var (level, world, first) = Level(Guards);
        using var _ = world;
        level.Start();

        Assert.True(level.Resume(saved));
        Run(level, 0.1f);

        Assert.Equal(3, level.Level.Counter(ScriptScene.Hash("n")));
        var orcs = world.Characters.Where(c => !c.IsPlayer).ToArray();
        Assert.Equal(2, orcs.Length);                                                             // replayed, not spawned twice
        Assert.Contains(orcs, o => MathF.Abs(o.HitPoints - hurt) < 0.01f);
        Assert.Equal(8, first.Avatar!.Position.X, 1);                                             // at the checkpoint's spawn point
        Assert.Single(world.Entities.Entities.OfType<PhysicsProp>());
        Assert.False(level.Game.PlayerDiedInLevel);                                               // a load is no death
    }

    [Fact]
    public void ACheckpointInAnotherScene_PreparesTheScenesItSaved_AndTheFirstComesBackOnTheWayBack()
    {
        const string first = """
            <Trigger id="guards"><Then><spawn type="orc" nr="2" area="door0" order="idle"/></Then></Trigger>
            <Trigger id="go" autorun="false"><Then><changeScene scene="town_s2" spawnPoint="gate" saveNPCs="true" spawnPlayers="true" transition="none"/></Then></Trigger>
            """;
        const string second = """
            <Trigger id="save" autorun="false"><Then><triggerCheckpoint spawnPoint="gate"/></Then></Trigger>
            <Trigger id="back" autorun="false"><Then><changeScene scene="town_s1" spawnPoint="start" spawnPlayers="true" transition="none"/></Then></Trigger>
            """;
        var (saved, _) = Played(first, second, level =>
        {
            level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
            Run(level, 0.3f);
        });
        var (fresh, world, _) = Level(first, second);
        using var _ = world;
        fresh.Start();

        Assert.True(fresh.Resume(saved));
        Assert.Equal("town_s2", fresh.Current!.Definition.Name);
        fresh.Current.Scripts!.ExecuteTrigger(ScriptScene.Hash("back"), null);
        Run(fresh, 0.3f);

        Assert.Equal("town_s1", fresh.Current!.Definition.Name);
        Assert.Equal(2, world.Characters.Count(c => !c.IsPlayer));                                // s1's log replayed as it is entered
    }

    [Fact]
    public void ACheckpointOfOtherScripts_IsPassedOver_TheLevelLeftAsItWas()
    {
        var (saved, _) = Played();
        var (level, world, _) = Level("""<Trigger id="save" autorun="false"><Then><triggerCheckpoint spawnPoint="door"/></Then></Trigger>""");
        using var _ = world;
        level.Start();

        Assert.False(level.Resume(saved));                                                        // its log names "guards", not here
        Assert.Null(level.Checkpoint);
        Assert.Equal("town_s1", level.Current!.Definition.Name);
    }

    [Fact]
    public void AKeptCheckpoint_IsNotGoneBackTo_TillTheRestart()
    {
        var (saved, _) = Played();
        var (level, world, _) = Level(Guards);
        using var _ = world;
        level.Start();
        Run(level, 0.3f);

        Assert.True(level.Resume(saved, apply: false));
        Assert.Equal(0, level.Level.Counter(ScriptScene.Hash("n")));                              // a restart from the pause menu: the level's start
        Assert.True(level.RestartAtCheckpoint());
        Assert.Equal(3, level.Level.Counter(ScriptScene.Hash("n")));
    }

    [Fact]
    public void WhatAnActionKeeps_IsWrittenByValue_ASpawnsHitPointsAndTheSendersCount()
    {
        var spawn = SavedAction.Of(new ScriptAction.State(0.5f, 2, new List<float> { 40, 12 }));
        var sends = SavedAction.Of(new ScriptAction.State(0, 1, new List<Character?> { null, null, null }));

        Assert.Equal([40f, 12f], Assert.IsType<List<float>>(spawn.Resolve().Tag));
        Assert.Equal((0.5f, 2), (spawn.Resolve().Countdown, spawn.Resolve().Queued));
        Assert.Equal(3, Assert.IsType<List<Character?>>(sends.Resolve().Tag).Count);              // no one, as the original's from disk
        Assert.Null(SavedAction.Of(new ScriptAction.State(0, 0, null)).Resolve().Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"Version": 99}""")]
    [InlineData("""{"Version": 1, "Counters": null}""")]
    [InlineData("""{"Version": 1, "Scenes": {"5": {"FirstEntry": false, "Entities": null}}}""")]
    public void TextThatIsNoCheckpointOfThisFormat_ReadsAsNone(string text) => Assert.Null(SavedCheckpoint.FromJson(text));

    [Fact]
    public void ANullTheSerializerLetBy_IsNoCheckpoint_TheLevelLeftAsItWas()
    {
        var (saved, _) = Played();
        var (level, world, _) = Level(Guards);
        using var _ = world;
        level.Start();

        Assert.False(level.Resume(saved with { SpawnPoints = [null!] }));
        Assert.False(level.Resume(saved with { Scenes = saved.Scenes.ToDictionary(p => p.Key, p => p.Value.Scripts is { } scripts ? p.Value with { Scripts = scripts with { Log = [null!] } } : p.Value) }));
        Assert.Null(level.Checkpoint);
    }

    [Fact]
    public void ASceneThatDoesNotFit_IsLeftAsNeverGoneTo()
    {
        const string first = """
            <Trigger id="go" autorun="false"><Then><changeScene scene="town_s2" spawnPoint="gate" spawnPlayers="true" transition="none"/></Then></Trigger>
            <Trigger id="later" autorun="false"><Then><counter name="n" action="set" value="9"/></Then></Trigger>
            """;
        var (saved, _) = Played(first, """<Trigger id="save" autorun="false"><Then><triggerCheckpoint spawnPoint="gate"/></Then></Trigger>""", level =>
        {
            level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
            Run(level, 0.3f);
        });
        var (fresh, world, _) = Level(first, """<Trigger id="other" autorun="false"><Then><triggerCheckpoint spawnPoint="gate"/></Then></Trigger>""");
        using var _ = world;
        fresh.Start();

        Assert.False(fresh.Resume(saved));
        Assert.Null(fresh.Find("town_s2")!.Scripts);                                              // made for the try, and unmade
        Assert.Equal("town_s1", fresh.Current!.Definition.Name);
    }

    private sealed class Dialogs : IScriptDialogs
    {
        public bool MessageBoxActive => false;

        public bool IsActive(int dialog) => false;

        public bool IsDone(int dialog, int interactIndex) => false;
    }

    [Fact]
    public void DialogsSavedThatDoNotRead_AreNoCheckpoint()
    {
        var (saved, _) = Played();
        var (level, world, _) = Level(Guards);
        using var _ = world;
        level.Start();
        var game = new ScriptGame { Dialogs = new Dialogs() };                                    // it reads none

        Assert.Null((saved with { Dialogs = System.Text.Json.Nodes.JsonValue.Create(1) }).Resolve(key => level.Find(key)?.Scripts, level.Services.Assets, game));
        Assert.NotNull(saved.Resolve(key => level.Find(key)?.Scripts, level.Services.Assets));     // none to read them
    }

    [Fact]
    public void TheHashes_AreTheOriginals_TheLevelsFileXoredWithItsScenes_AndTheDialogsOrZeros()
    {
        var root = Directory.CreateTempSubdirectory("om85");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Levels"));
            File.WriteAllText(Path.Combine(root.FullName, "Levels", "a.lvl"), "level");
            File.WriteAllText(Path.Combine(root.FullName, "Levels", "s1.xml"), "one");
            File.WriteAllText(Path.Combine(root.FullName, "Levels", "s2.xml"), "two");
            File.WriteAllText(Path.Combine(root.FullName, "Levels", "d.xml"), "talk");
            var files = new ContentFileSystem(root.FullName);
            LevelDefinition Of(string? dialogs, params string[] scenes) =>
                new("#c", "#d", "#d", "loading", new LevelStart(null, "start", true, false, null, true), dialogs, scenes, LevelDefinition.DefaultAvatar, [], []);

            var hash = SavedCheckpoint.LevelHashOf(files, "Levels/a.lvl", Of(null, "Levels/s1.xml", "Levels/s2.xml"));
            Assert.Equal(hash, SavedCheckpoint.LevelHashOf(files, "Levels/a.lvl", Of(null, "Levels/s2.xml", "Levels/s1.xml")));
            var sha = (string text) => System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
            var xor = sha("level").Zip(sha("one"), sha("two")).Select(t => (byte)(t.First ^ t.Second ^ t.Third)).ToArray();
            Assert.Equal(Convert.ToHexString(xor), hash);
            Assert.Equal(new string('0', 64), SavedCheckpoint.DialogHashOf(files, Of(null)));
            Assert.Equal(Convert.ToHexString(sha("talk")), SavedCheckpoint.DialogHashOf(files, Of("Levels/d.xml")));
            Assert.Equal(Convert.ToHexString(sha("level")), SavedCheckpoint.LevelHashOf(files, "Levels/a.lvl", Of(null, "Levels/gone.xml")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
