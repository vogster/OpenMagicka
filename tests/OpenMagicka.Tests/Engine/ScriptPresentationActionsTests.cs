using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Engine.ScriptWorldActionsTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>OM-46's stage, tutorial and profile actions (LEVELS_TRIGGERS.md §9.5): the camera, the lights, the HUD's requests, the bar, the tutorial and the profile.</summary>
public class ScriptPresentationActionsTests
{
    private static void Run(ScriptScene scene, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            scene.Update(Tick);
        }
    }

    [Fact]
    public void TheCamera_LocksOnAnArea_Biases_Follows_StandsAtALocator_AndTakesEffects()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="lock" autorun="false"><Then><cameraLock area="gate" time="2"/><cameraBias bias="1,0,2"/></Then></Trigger>
            <Trigger id="follow" autorun="false"><Then><cameraFollow id="hero"/></Then></Trigger>
            <Trigger id="stand" autorun="false"><Then><cameraPosition target="spot" offset="0,1,0" magnification="2"/></Then></Trigger>
            <Trigger id="fx" autorun="false"><Then><addCameraEffect effect="Snow"/><addCameraEffect effect="rain"/><removeCameraEffect effect="snow"/></Then></Trigger>
            <Trigger id="clear" autorun="false"><Then><removeCameraEffect/></Then></Trigger>
            """);
        var hero = Spawn(world, at: new Numerics.Vector3(3, 0.9f, 3));
        hero.UniqueId = ScriptScene.Hash("hero");

        Fire(scene, "lock");
        Assert.Equal(new Xna.Vector3(-2, 0, -2), scene.Stage.Camera.Box!.Corner);
        Assert.Equal(new Xna.Vector3(1, 0, 2), scene.Stage.Camera.TargetBias);
        Assert.Equal(20, scene.Stage.Camera.BiasTime);
        Fire(scene, "follow");
        Assert.Same(hero, scene.Stage.Followed);
        Assert.Equal(OpenMagicka.Engine.Cameras.CameraBehaviour.FollowEntity, scene.Stage.Camera.Behaviour);
        Fire(scene, "stand");
        Assert.Equal(new Xna.Vector3(5, 1, 1), scene.Stage.Camera.Ground);
        Assert.Equal(2, scene.Stage.Camera.Magnification);
        Fire(scene, "fx");
        Assert.Equal(["rain"], scene.Stage.CameraEffects);
        Fire(scene, "clear");
        Assert.Empty(scene.Stage.CameraEffects);
    }

    [Fact]
    public void Lights_GoOffAndOn_AndAReplaySwitchesThemAtOnce()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="off" autorun="false"><Then><disableLight id="Torch1,torch2" effect="flicker" time="2"/></Then></Trigger>
            <Trigger id="on" autorun="false"><Then><enableLight id="torch2" effect="linear" time="1"/></Then></Trigger>
            """);

        Fire(scene, "off");
        Fire(scene, "on");

        Assert.False(scene.Lights.IsOn(ScriptScene.Hash("torch1")));
        Assert.Equal(new LightSwitch(false, LightTransitionType.Flicker, 2), scene.Lights.Switched[ScriptScene.Hash("torch1")]);
        Assert.True(scene.Lights.IsOn(ScriptScene.Hash("torch2")));
        Assert.True(scene.Lights.IsOn(ScriptScene.Hash("lamp")));

        var snapshot = scene.Save();
        var replay = scene.Load(snapshot, null);
        Assert.Empty(scene.Lights.Switched);
        foreach (var action in replay)
        {
            action.Replay();
        }
        Assert.Equal(new LightSwitch(false, LightTransitionType.None, 0), scene.Lights.Switched[ScriptScene.Hash("torch1")]);
    }

    [Fact]
    public void TheRequests_GoToTheSystemsBeyondTheWorld()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <flash/><stopMusic/><setMusicFocus id="spot" radius="10"/><setVolume id="Wind" volume="0.5"/>
              <displayTitleInLevel title="#chapter" subTitle="#where" textAlignment="L"/><bossTitle title="#boss" displayTime="4"/>
              <scrollScore value="100" area="spot" offset="0,2,0" ttl="1"/><rollCredits file="credits"/>
              <endGame type="levelComplete"/><killDialogs/>
            </Then></Trigger>
            """);

        Fire(scene, "go");

        Assert.Equal<ScriptRequest>([
            new FlashRequest(0.5f), new StopMusicRequest(), new MusicFocusRequest(new Numerics.Vector3(5, 0, 1), 10, 1), new SetVolumeRequest(ScriptScene.Hash("wind"), 0.5f),
            new TitleRequest("#chapter", "#where", false, 2, 1, 1, 'L', Boss: false), new TitleRequest("#boss", "", false, 4, 1, 1, 'C', Boss: true),
            new ScoreRequest("100", new Numerics.Vector3(5, 2, 1), 1), new CreditsRequest("credits"), new EndGameRequest(EndGameCondition.LevelComplete, true, false),
            new EndDialogsRequest()], scene.Stage.TakeRequests());
    }

    [Fact]
    public void TheHud_HidesAndShows()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="hide" autorun="false"><Then><disableUI/></Then></Trigger>
            <Trigger id="show" autorun="false"><Then><enableUI/></Then></Trigger>
            """);

        Fire(scene, "hide");
        Assert.False(scene.Stage.IsUiEnabled);
        Fire(scene, "show");
        Assert.True(scene.Stage.IsUiEnabled);
    }

    [Fact]
    public void TheBar_RunsDownAfterItsDelay_AndSetsOffItsTriggerOnce()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="set" autorun="false"><Then>
              <healthbar type="timerdecreasing" graphicstype="dynamite" time="2" initialtimerdelay="1" isscaled="true" OnEndTrigger="Boom"/>
              <activatehealthbar/>
            </Then></Trigger>
            <Trigger id="boom" autorun="false" repeat="true"><Then><counter name="booms" action="inc" value="1"/></Then></Trigger>
            <Trigger id="gone" autorun="false"><Then><removeHealthBar/></Then></Trigger>
            """);
        var bar = scene.Game.HealthBar;

        Fire(scene, "set");
        Assert.True(bar.IsActive);
        Assert.Equal(GenericHealthBarGraphics.Dynamite, bar.Graphics);
        Run(scene, 1.5f);
        Assert.InRange(bar.Shown, 0.7f, 0.8f);                                                   // half a second run after the delay
        Run(scene, 2);
        Assert.True(bar.IsDone);
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("booms")));

        Fire(scene, "gone");
        Assert.False(bar.IsActive);
    }

    [Fact]
    public void TheTutorial_LocksElementsCastTypesAndPush_AndShowsItsHints()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="lock" autorun="false"><Then>
              <disableElement element="fire"/><disableElement element="cold"/><enableElement element="fire"/>
              <disableCastType type="blast"/><disableCastType type="self"/><enableCastType type="self"/>
              <disablePush/><enableDark/>
              <setHint id="hint_move" appendController="true" animation="ConjureFire" position="top"/>
              <setDialogHint id="hint_heal" element="life" trigger="healed"/>
            </Then></Trigger>
            <Trigger id="free" autorun="false" repeat="true"><Then><enablePush/><disableDark/><removeHint/><removeDialogHint/></Then></Trigger>
            """);
        var tutorial = scene.Game.Tutorial;

        Fire(scene, "lock");
        Assert.Equal(Elements.Cold, tutorial.DisabledElements);
        Assert.Equal([CastType.None], tutorial.DisabledCastTypes);                               // a blast is the "none" cast
        Assert.True(tutorial.IsPushDisabled && tutorial.IsDark);
        Assert.Equal(new TutorialHint("hint_move", true, HintAnimations.ConjureFire, HintPosition.Top), tutorial.Hint);
        Assert.Equal(new TutorialDialogHint(Elements.Life, MagickType.None, "hint_heal", ScriptScene.Hash("healed"), null, null), tutorial.DialogHint);

        Fire(scene, "free");
        Assert.NotNull(tutorial.DialogHint);                                                      // its first second: not closed
        tutorial.Step(ScriptTutorial.HoldOff);
        Fire(scene, "free");
        Assert.False(tutorial.IsPushDisabled || tutorial.IsDark);
        Assert.Null(tutorial.Hint);
        Assert.Null(tutorial.DialogHint);
    }

    [Fact]
    public void Achievements_AreWonOnce_AndTheConditionalOnesByTheirRules()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var avatar = Avatar(world, session.AddPlayer());
        var scene = Scene(world, """
            <Trigger id="win" autorun="false" repeat="true"><Then><unlockAchievement achievement="AFriendInNeed"/></Then></Trigger>
            <Trigger id="slain" autorun="false"><Then><conditionalUnlockAchievement achievement="thisismagicka" victim="king"/></Then></Trigger>
            <Trigger id="clean" autorun="false"><Then><conditionalUnlockAchievement achievement="drivenmad"/></Then></Trigger>
            <Trigger id="notes" autorun="false"><Then><foundSecretArea id="Cave"/><foundMoose id="moose1"/><killedAPony id="pony"/></Then></Trigger>
            """);
        var king = Spawn(world, at: new Numerics.Vector3(1, 0.9f, 0));
        king.UniqueId = ScriptScene.Hash("king");
        king.Damage(new OpenMagicka.Gameplay.Combat.DamageEntry(OpenMagicka.Gameplay.Combat.AttackProperties.Damage, Elements.Earth, 10, 1), avatar, avatar.Position);
        scene.Game.PlayerDiedInLevel = true;

        Fire(scene, "win");
        Fire(scene, "win");
        Fire(scene, "slain");
        Fire(scene, "clean");
        Fire(scene, "notes");

        Assert.Equal(["afriendinneed", "thisismagicka"], scene.Game.Achievements.Order());
        Assert.Equal(2, scene.Stage.TakeRequests().OfType<AchievementRequest>().Count());
        Assert.Contains("cave", scene.Game.SecretAreas);
        Assert.Contains("moose1", scene.Game.Moose);
        Assert.Contains("pony", scene.Game.Ponies);
    }

    [Fact]
    public void AConditionalAchievementWithNoRule_FailsTheLoading()
    {
        using var world = World();
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="t"><Then><conditionalUnlockAchievement achievement="pacifist"/></Then></Trigger>"""));
    }

    [Fact]
    public void TheWizards_LightTheirStaffs_AndTheSectionsComeAndGo()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var avatar = Avatar(world, session.AddPlayer());
        var scene = Scene(world, """
            <Trigger id="dark" autorun="false"><Then><wizardControl action="StaffLightOn"/><beginDefaultAvatarSection/><beginUnequipWeaponsSection/></Then></Trigger>
            <Trigger id="day" autorun="false"><Then><wizardControl action="staffLightOff"/><endDefaultAvatarSection/><endUnequipWeaponsSection animation="pickup"/></Then></Trigger>
            <Trigger id="quiet" autorun="false"><Then><telemetryTutorialBegin tutorialName="t" stepName="s"/><printDebug message="hi"/><releaseGrip id="x"/></Then></Trigger>
            """);

        Fire(scene, "dark");
        Assert.True(scene.Game.StaffLight && scene.Game.DefaultAvatarSection && scene.Game.UnequipWeaponsSection);
        Fire(scene, "day");
        Assert.False(scene.Game.StaffLight || scene.Game.DefaultAvatarSection || scene.Game.UnequipWeaponsSection);
        Assert.Equal("pickup", avatar.NextAttackAnimation);
        Fire(scene, "quiet");
    }
}
