using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The stage's actions (LEVELS_TRIGGERS.md §9.3): camera, fade, cutscene, input, and the requests beyond the world.</summary>
public class ScriptStageActionsTests
{
    private static ScriptScene Scene(GameWorld world, string triggers)
    {
        var places = new ScriptPlaces([], [new LocatorContent("view", Xna.Matrix.CreateTranslation(10, 0, 5), 1),
            new LocatorContent("start0", Xna.Matrix.CreateTranslation(1, 0, 1), 1), new LocatorContent("start1", Xna.Matrix.CreateTranslation(2, 0, 2), 1)]);
        var xml = $"<Scene><Model>m</Model>{triggers}</Scene>";
        var scene = new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, places: places);
        scene.Enter();
        return scene;
    }

    private static void Fire(ScriptScene scene, string trigger, Character? sender = null)
    {
        scene.ExecuteTrigger(ScriptScene.Hash(trigger), sender);
        scene.Update(Tick);
    }

    [Fact]
    public void TheCamera_MovesToALocator_Zooms_Shakes_LocksInput_AndIsReleased()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="look" autorun="false"><Then><cameraMove target="view" offset="0,1,0" time="2" magnification="1.5" lockInput="true" interpolation="linear"/></Then></Trigger>
            <Trigger id="zoom" autorun="false"><Then><cameraMagnify magnification="0.8" time="3"/><cameraShake magnitude="2" time="0.5"/></Then></Trigger>
            <Trigger id="free" autorun="false"><Then><cameraRelease time="0.5"/></Then></Trigger>
            """);

        Fire(scene, "look");
        var camera = scene.Stage.Camera;
        Assert.Equal(CameraBehaviour.MoveToTarget, camera.Behaviour);
        Assert.Equal(new Xna.Vector3(10, 1, 5), camera.Target);
        Assert.Equal(CameraInterpolation.Linear, camera.Interpolation);
        Assert.Equal(1.5f, camera.TargetMagnification);
        Assert.True(scene.Stage.IsInputLimited);

        Fire(scene, "zoom");
        Assert.Equal(0.8f, scene.Stage.Camera.TargetMagnification);
        Assert.Equal(2, scene.Stage.Camera.ShakeMagnitude);

        Fire(scene, "free");
        Assert.Equal(CameraBehaviour.FollowPlayers, scene.Stage.Camera.Behaviour);
        Assert.Equal(scene.Stage.Camera.DefaultMagnification, scene.Stage.Camera.TargetMagnification);
        scene.Update(0.2f);
        Assert.False(scene.Stage.IsInputLimited);                                                 // the lock gone, after a moment
    }

    [Fact]
    public void Input_IsHeldByTheLevel_TillItsOwnEnable()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="off" autorun="false"><Then><disableInput/></Then></Trigger>
            <Trigger id="on" autorun="false"><Then><enableInput/></Then></Trigger>
            """);

        Fire(scene, "off");
        scene.Stage.LimitInput("menu");
        Fire(scene, "on");
        scene.Update(0.2f);
        Assert.True(scene.Stage.IsInputLimited);                                                  // another hold stays
        scene.Stage.UnlimitInput("menu");
        scene.Update(0.2f);
        Assert.False(scene.Stage.IsInputLimited);
    }

    [Fact]
    public void ACutscene_StopsThePlayedTimers_CalmsTheWorld_AndItsSkipFiresOnce()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="show" autorun="false"><Then><cutscene onSkip="skipped"/><fade fadeIn="true" color="0,0,0,1" time="0.5"/></Then></Trigger>
            <Trigger id="done" autorun="false"><Then><endCutscene/></Then></Trigger>
            <Trigger id="skipped" autorun="false" repeat="true"><Then><counter name="skips" action="inc" value="1"/></Then></Trigger>
            """);
        var feared = Spawn(world, Gameplay.AgentTests.Orc, at: new Numerics.Vector3(1, 0.9f, 0));
        feared.Fear(new Numerics.Vector3(3, 0.9f, 0));
        world.Tick(Tick);                                                                         // the manager's grid sees it

        Fire(scene, "show");
        Assert.True(scene.Game.InCutscene);
        Assert.False(scene.Stage.IsUiEnabled);
        Assert.False(feared.IsFeared);
        Assert.Contains(scene.Stage.TakeRequests(), r => r is EndDialogsRequest);
        Assert.Equal(new ScreenFade(true, new Numerics.Vector4(0, 0, 0, 1), 0.5f, false), scene.Stage.Fade);

        Assert.False(scene.SkipCutscene());                                                       // the tip first
        Assert.True(scene.SkipCutscene());
        Assert.False(scene.SkipCutscene());
        scene.Update(Tick);
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("skips")));

        Fire(scene, "done");
        Assert.False(scene.Game.InCutscene);
        Assert.True(scene.Stage.IsUiEnabled);
    }

    [Fact]
    public void ACutscene_WithNoSkipTrigger_CannotBeSkipped_AndCanKeepTheDialogs()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="show" autorun="false"><Then><cutscene killDialogs="false"/></Then></Trigger>
            """);

        Fire(scene, "show");

        Assert.DoesNotContain(scene.Stage.TakeRequests(), r => r is EndDialogsRequest);
        Assert.False(scene.SkipCutscene());
        Assert.False(scene.SkipCutscene());
        Assert.False(scene.Stage.IsCutsceneSkipped);
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="x"><Then><cameraMagnify magnification="0"/></Then></Trigger>"""));
    }

    [Fact]
    public void Sound_Music_Dialogs_AndSceneChanges_AreAskedOfTheirSystems()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="go" autorun="false" repeat="true"><Then>
              <playSound cue="spells/boom" id="Bell" area="view" volume="0.7" radius="20" apply3D="true"/><stopSound id="bell" instant="true"/>
              <playMusic cue="chapter1" focusValue="0.5"/>
              <startDialog dialog="Hello"/><startDialog dialog="far" position="view"/><startDialog dialog="up" position="0.5,0.2"/>
              <startDialog dialog="odd" position="1,2,3"/>
              <changeScene scene="ch1_s2" spawnPoint="start" transition="crossfade" saveNPCs="true"/>
            </Then></Trigger>
            """);
        var hero = Spawn(world);

        Fire(scene, "go", hero);
        var requests = scene.Stage.TakeRequests();

        Assert.Equal(new PlaySoundRequest(ScriptScene.Hash("bell"), SoundBanks.Spells, "boom", 0.7f, new Numerics.Vector3(10, 0, 5), 20, true), requests[0]);
        Assert.Equal(new StopSoundRequest(ScriptScene.Hash("bell"), true), requests[1]);
        Assert.Equal(new PlayMusicRequest(SoundBanks.Music, "chapter1", 0.5f), requests[2]);
        Assert.Same(hero, Assert.IsType<StartDialogRequest>(requests[3]).Speaker);                // the interactor
        Assert.Equal(new Numerics.Vector3(10, 0, 5), Assert.IsType<StartDialogRequest>(requests[4]).At);
        Assert.Equal(new Numerics.Vector2(0.5f, 0.2f), Assert.IsType<StartDialogRequest>(requests[5]).OnScreen);
        Assert.Equal(new Numerics.Vector3(1, 3, 0), Assert.IsType<StartDialogRequest>(requests[6]).At);   // as the original reads x,y,z
        var change = Assert.IsType<ChangeSceneRequest>(requests[7]);
        Assert.Equal("ch1_s2", change.Scene);
        Assert.Equal(ScriptTransition.CrossFade, change.Transition);
        Assert.True(change.SaveNpcs);
        Assert.Equal(OpenMagicka.Content.LegacyStringHash.Lower("start1"), change.SpawnPoints[1]);
        Assert.Empty(scene.Stage.TakeRequests());

        scene.IsHost = false;
        scene.ExecuteTrigger(ScriptScene.Hash("go"), hero);
        Assert.Empty(scene.Stage.TakeRequests());                                                 // a client fires no triggers
    }

    [Fact]
    public void ACheckpoint_HealsTheLiving_RaisesTheDead_AndAsksTheLevel()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var (first, second) = (session.AddPlayer(), session.AddPlayer());
        first.Avatar = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        second.Avatar = Spawn(world, at: new Numerics.Vector3(3, 0.9f, 0));
        first.Avatar.SetHealth(0.25f);
        second.Avatar.Kill();
        Run(world, 0.5f);
        var scene = Scene(world, """
            <Trigger id="save" autorun="false"><Then><triggerCheckpoint spawnPoint="start"><ignore trigger="Intro"/></triggerCheckpoint></Then></Trigger>
            """);

        Fire(scene, "save");

        Assert.Equal(first.Avatar.MaxHitPoints, first.Avatar.HitPoints);
        Assert.True(second.Ressing);
        var checkpoint = Assert.IsType<CheckpointRequest>(scene.Stage.TakeRequests()[0]);
        Assert.Equal(new Numerics.Vector3(2, 0, 2), checkpoint.SpawnPoints[1].Translation);
        Assert.Equal([ScriptScene.Hash("intro")], checkpoint.Ignore);
        Assert.False(checkpoint.SpawnFairy);                                                     // two players: no fairy
        scene.BeginCutscene(0, false, true);                                                     // a cutscene at once lets the revival rise
        Run(world, 0.5f);
        Assert.False(second.Avatar.IsDead);
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="x"><Then><triggerCheckpoint><keep/></triggerCheckpoint></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="x"><Then><playSound cue="7/boom"/></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="x"><Then><playSound cue="a/b/c"/></Then></Trigger>"""));
    }
}
