using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Props;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>A level at play and its scene changes (LEVELS_TRIGGERS.md §6.1).</summary>
public class PlayedLevelTests
{
    /// <summary>A model with a floor at a height, locators and a prop it places.</summary>
    private static LevelModelContent Model(float floor, params (string Name, float X)[] locators) => new(
        null, [], [], [], [new PhysicsEntityPlacement(Xna.Matrix.CreateTranslation(20, floor, 20), "crate")], [], [],
        [new CollisionMeshContent([new(-50, floor, -50), new(50, floor, -50), new(50, floor, 50), new(-50, floor, 50)],
            [new CollisionTriangle(0, 2, 1), new CollisionTriangle(0, 3, 2)])],
        null, [], [.. locators.Select(l => new LocatorContent(l.Name, Xna.Matrix.CreateTranslation(l.X, floor, 0), 1))], new NavMeshContent([], []));

    private sealed class Content(Dictionary<string, string> scenes, Dictionary<string, LevelModelContent> models) : ILevelContent
    {
        public SceneDefinition Scene(string path) => SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(scenes[path])), path);

        public LevelModelContent? Model(string asset) => models.GetValueOrDefault(asset);
    }

    private sealed class Props : IScriptAssets
    {
        public OpenMagicka.Gameplay.Items.ItemTemplate? Item(string asset) => null;

        public PropTemplate? Prop(string asset) => asset == "data/physicsentities/crate"
            ? new PropTemplate("crate", false, false, true, 140, 100, OpenMagicka.Gameplay.Combat.ResistanceTable.Neutral,
                new PropBox(new Numerics.Vector3(-0.5f, 0, -0.5f), Numerics.Vector3.One, Numerics.Quaternion.Identity), [], "", [], [])
            : null;
    }

    private static (PlayedLevel Level, GameWorld World, Player First) Level(string firstScripts = "", string secondScripts = "")
    {
        var definition = new LevelDefinition("#Chapter", "#Desc", "#Desc", "loading", new LevelStart("Town_S1", "start", true, false, null, true), null,
            ["Levels/Town/town_s1.xml", "Levels/Town/town_s2.xml"], LevelDefinition.DefaultAvatar, [], []);
        var content = new Content(
            new()
            {
                ["Levels/Town/town_s1.xml"] = $"<Scene><Model>m1</Model>{firstScripts}</Scene>",
                ["Levels/Town/town_s2.xml"] = $"<Scene><Model>m2</Model>{secondScripts}</Scene>",
            },
            new() { ["Levels/Town/m1"] = Model(0, ("start0", 2), ("door0", 8)), ["Levels/Town/m2"] = Model(5, ("gate0", -3), ("gate1", -1)) });
        var session = new GameSession();
        var world = new GameWorld(session: session);
        var first = session.AddPlayer();
        first.Avatar = new Character(Goblin, world.Physics, new Numerics.Vector3(0, 30, 0)) { IsPlayer = true, Player = first };
        var orcs = new Bestiary().Add("data/characters/orc", Orc);
        return (new PlayedLevel(definition, content, world, new LevelServices(orcs, new Props())), world, first);
    }

    private static void Run(PlayedLevel level, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            level.Tick(Tick);
        }
    }

    [Fact]
    public void TheLevel_StartsAtItsStart_ThePlayersOnTheFloorAtTheLocators_AndTheModelsPropsStanding()
    {
        var (level, world, first) = Level();
        using var _ = world;

        level.Start();
        Run(level, 0.5f);

        Assert.Equal("town_s1", level.Current!.Definition.Name);
        Assert.Equal(2, first.Avatar!.Position.X, 2);
        Assert.InRange(first.Avatar.Position.Y, 0.5f, 1.5f);                                       // on the floor
        Assert.Single(world.Entities.Entities.OfType<PhysicsProp>());
        Assert.Equal(new Xna.Vector3(5, 1, 0), level.Level.Stage.Camera.Ground, new XnaComparer());              // the four spots' centre (2, 4, 6, 8), 1 up, as the original
    }

    [Fact]
    public void ChangeScene_WaitsForTheFade_ThenMovesThePlayers_AndLeavesTheOldScenesThingsUnlessKept()
    {
        var (level, world, first) = Level("""
            <Trigger id="go"><Then><spawn type="orc" area="door0" id="guard" order="idle"/>
              <changeScene scene="Town_S2" spawnPoint="gate" transition="fade" transitionTime="1" spawnPlayers="true"/></Then></Trigger>
            """, """
            <Trigger id="back" autorun="false"><Then><changeScene scene="town_s1" spawnPoint="door" transition="none" spawnPlayers="true"/></Then></Trigger>
            """);
        using var _ = world;
        level.Start();

        Run(level, 0.5f);
        Assert.Equal("town_s1", level.Current!.Definition.Name);                                  // still fading out
        Assert.True(level.IsChanging);
        Assert.Equal(new SceneTransition(ScriptTransition.Fade, 1, Out: true), level.Level.Stage.Transition);
        Run(level, 1);
        Assert.Equal("town_s2", level.Current!.Definition.Name);
        Assert.False(level.Level.Stage.Transition!.Out);
        Run(level, 1.1f);
        Assert.Null(level.Level.Stage.Transition);                                                // back in
        Assert.Equal(-3, first.Avatar!.Position.X, 2);
        Assert.InRange(first.Avatar.Position.Y, 5.5f, 6.5f);                                       // on the new floor, the old one gone
        Assert.Null(world.Entities.FindByUniqueId(ScriptScene.Hash("guard")));                    // left without keeping: gone
        Assert.InRange(Assert.Single(world.Entities.Entities.OfType<PhysicsProp>()).Position.Y, 5, 6);   // the new scene's own, the old one's gone
        Assert.Contains(level.TakeRequests(), r => r is SceneChangedRequest { Kept: false });

        level.Find("town_s2")!.Scripts!.ExecuteTrigger(ScriptScene.Hash("back"), null);
        Run(level, 0.2f);
        Assert.Equal("town_s1", level.Current!.Definition.Name);
        Assert.Equal(8, first.Avatar.Position.X, 2);
        Assert.Empty(world.Entities.Entities.OfType<PhysicsProp>());                              // as the original: a scene's props stand only on its first entry
    }

    [Fact]
    public void KeptNPCs_WaitSuspended_AndComeBackWhereTheyWere()
    {
        var (level, world, _) = Level("""
            <Trigger id="spawn"><Then><spawn type="orc" area="door0" id="guard" order="idle"/></Then></Trigger>
            <Trigger id="go" autorun="false"><Then><changeScene scene="town_s2" spawnPoint="gate" saveNPCs="true" spawnPlayers="true" transitionTime="0.2"/></Then></Trigger>
            """, """
            <Trigger id="back" autorun="false"><Then><changeScene scene="town_s1" spawnPoint="start" spawnPlayers="true" transition="none"/></Then></Trigger>
            """);
        using var _ = world;
        level.Start();
        Run(level, 0.5f);
        var guard = (Character)world.Entities.FindByUniqueId(ScriptScene.Hash("guard"))!;
        var at = guard.Position;

        Assert.True(level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null));
        Run(level, 0.5f);
        Assert.Equal("town_s2", level.Current!.Definition.Name);
        Assert.Null(guard.Manager);
        Assert.True(guard.Body.Body.IsSuspended);
        Assert.Equal(2, level.Find("town_s1")!.Scripts!.Triggers.Count(t => t.HasTriggered));      // its scripts live on

        level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("back"), null);
        Run(level, 0.5f);
        Assert.Same(guard, world.Entities.FindByUniqueId(ScriptScene.Hash("guard")));
        Assert.True(Numerics.Vector3.Distance(at, guard.Position) < 0.2f);
        Assert.False(guard.Body.Body.IsSuspended);
    }

    [Fact]
    public void ADeadPlayer_StaysBehind_AndASecondOneStandsTwoAlongXFromTheLastLocator()
    {
        var (level, world, first) = Level();
        using var _ = world;
        var second = world.Session!.AddPlayer();
        second.Avatar = new Character(Goblin, world.Physics, new Numerics.Vector3(0, 30, 0)) { IsPlayer = true, Player = second };
        level.Start();
        Run(level, 0.5f);
        Assert.Equal(4, second.Avatar.Position.X, 2);                                             // start1 is missing: start0, 2 along

        first.Avatar!.Kill();
        Run(level, 0.2f);
        level.ChangeScene(new SpawnPoint(ScriptScene.Hash("town_s2"), [ScriptScene.Hash("gate0"), ScriptScene.Hash("gate1")], true),
            ScriptTransition.None, 0, keep: false);
        Run(level, 0.2f);

        Assert.Null(first.Avatar.Manager);                                                        // the corpse stayed behind
        Assert.Equal(-1, second.Avatar.Position.X, 2);
    }

    [Fact]
    public void ARestartAtTheCheckpoint_BringsBackItsState_TheSurvivorsAndFreshWizardsAtItsSpawnPoints()
    {
        var (level, world, first) = Level("""
            <Trigger id="guards"><Then><spawn type="orc" nr="2" area="door0" order="idle"/></Then></Trigger>
            <Trigger id="save" autorun="false"><Then><counter name="n" action="set" value="3"/><triggerCheckpoint spawnPoint="door"/></Then></Trigger>
            <Trigger id="later" autorun="false"><Then><counter name="n" action="set" value="9"/></Then></Trigger>
            """);
        using var _ = world;
        level.Start();
        Run(level, 0.3f);
        var scene = level.Current!.Scripts!;
        var orcs = world.Characters.Where(c => !c.IsPlayer).ToArray();
        orcs[0].Damage(new OpenMagicka.Gameplay.Combat.DamageEntry(OpenMagicka.Gameplay.Combat.AttackProperties.Damage,
            OpenMagicka.Gameplay.Spells.Elements.Earth, 100, 1), null, orcs[0].Position);
        var hurt = orcs[0].HitPoints;
        scene.ExecuteTrigger(ScriptScene.Hash("save"), null);
        Run(level, 0.1f);
        Assert.NotNull(level.Checkpoint);

        orcs[1].Kill();
        scene.ExecuteTrigger(ScriptScene.Hash("later"), null);
        Run(level, 0.5f);
        var oldAvatar = first.Avatar!;
        Assert.True(level.RestartAtCheckpoint());
        Run(level, 0.1f);

        Assert.Equal(3, level.Level.Counter(ScriptScene.Hash("n")));
        var back = world.Characters.Where(c => !c.IsPlayer).ToArray();
        Assert.Equal(2, back.Length);                                                             // both lived at the checkpoint
        Assert.Contains(back, o => MathF.Abs(o.HitPoints - hurt) < 0.01f);
        Assert.NotSame(oldAvatar, first.Avatar);
        Assert.Equal(8, first.Avatar!.Position.X, 1);                                             // at the checkpoint's spawn point
        Assert.True(level.Game.PlayerDiedInLevel);
        Assert.Single(world.Entities.Entities.OfType<PhysicsProp>());                             // the scene's prop, as it was saved
    }

    [Fact]
    public void AnIgnoredTrigger_IsResetAtTheRestart_AndItsActionsAreNotReplayed()
    {
        var (level, world, _) = Level("""
            <Trigger id="ambush"><Then><spawn type="orc" area="door0" order="idle"/><counter name="ambushes" action="inc" value="1"/></Then></Trigger>
            <Trigger id="save" autorun="false"><Then><triggerCheckpoint spawnPoint="start"><ignore trigger="Ambush"/></triggerCheckpoint></Then></Trigger>
            """);
        using var _ = world;
        level.Start();
        Run(level, 0.3f);
        Assert.Single(world.Characters, c => !c.IsPlayer);
        level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("save"), null);
        Run(level, 0.1f);

        Assert.True(level.RestartAtCheckpoint());
        Run(level, 0.3f);

        Assert.Single(world.Characters, c => !c.IsPlayer);                                  // not replayed; fired anew instead
        Assert.Equal(2, level.Level.Counter(ScriptScene.Hash("ambushes")));
    }

    [Fact]
    public void TheNPCsOfASceneLeftWithThemKept_SurviveARestart_AndTheNextCheckpointToo()
    {
        var (level, world, first) = Level("""
            <Trigger id="guards"><Then><spawn type="orc" nr="2" area="door0" order="idle"/></Then></Trigger>
            <Trigger id="go" autorun="false"><Then><changeScene scene="town_s2" spawnPoint="gate" saveNPCs="true" spawnPlayers="true" transition="none"/></Then></Trigger>
            """, """
            <Trigger id="save"><Then><triggerCheckpoint spawnPoint="gate"/></Then></Trigger>
            <Trigger id="back" autorun="false"><Then><changeScene scene="town_s1" spawnPoint="start" spawnPlayers="true" transition="none"/></Then></Trigger>
            """);
        using var _ = world;
        level.Start();
        Run(level, 0.3f);
        level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
        Run(level, 0.3f);                                                                         // in s2: the checkpoint, the orcs waiting in s1
        Assert.NotNull(level.Checkpoint);
        var oldAvatar = first.Avatar!;

        Assert.True(level.RestartAtCheckpoint());
        Run(level, 0.1f);
        Assert.True(oldAvatar.Body.Body.IsRemoved);
        level.TakeCheckpoint([Numerics.Matrix4x4.Identity], []);                                // before s1 is entered again
        Assert.True(level.RestartAtCheckpoint());
        level.Current!.Scripts!.ExecuteTrigger(ScriptScene.Hash("back"), null);
        Run(level, 0.3f);

        Assert.Equal("town_s1", level.Current!.Definition.Name);
        Assert.Equal(2, world.Characters.Count(c => !c.IsPlayer));                                // replayed on the way back
        Assert.Equal(1, world.Entities.Entities.Count(e => e is Character { IsPlayer: true }));
    }

    [Fact]
    public void ASuspendedBody_TouchesNothing_AndComesBackAsItWas()
    {
        using var world = World();
        var box = world.Physics.AddBody(new OpenMagicka.Physics.BoxShape(Numerics.Vector3.One), new Numerics.Vector3(0, 0.5f, 0), 10);
        var down = (new Numerics.Vector3(0, 3, 0), new Numerics.Vector3(0, 0.6f, 0));

        Assert.Same(box, world.Physics.CastSegment(down.Item1, down.Item2)?.Body);
        box.Suspend();
        Assert.True(box.IsSuspended);
        Assert.Null(world.Physics.CastSegment(down.Item1, down.Item2)?.Body);
        OpenMagicka.Tests.Gameplay.CharacterTests.Run(world, 0.5f);
        Assert.Equal(0.5f, box.Position.Y, 3);                                                    // static while away: no fall
        Assert.Throws<InvalidOperationException>(() => box.SetMass(5));
        box.Resume();
        Assert.Same(box, world.Physics.CastSegment(down.Item1, down.Item2)?.Body);
        Assert.False(box.IsStatic);
    }

    private sealed class XnaComparer : IEqualityComparer<Xna.Vector3>
    {
        public bool Equals(Xna.Vector3 a, Xna.Vector3 b) => Xna.Vector3.Distance(a, b) < 0.01f;

        public int GetHashCode(Xna.Vector3 v) => 0;
    }
}
