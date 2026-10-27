using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>A level at play in the game (OM-75): the controls' walk, the camera following, the screen's cover.</summary>
public class LevelPlayTests
{
    private sealed class Content(Dictionary<string, string> scenes) : ILevelContent
    {
        public SceneDefinition Scene(string path) => SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(scenes[path])), path);

        public LevelModelContent? Model(string asset) => new(null, [], [], [], [], [], [],
            [new CollisionMeshContent([new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)], [new CollisionTriangle(0, 2, 1), new CollisionTriangle(0, 3, 2)])],
            null, [], [new LocatorContent("start0", Xna.Matrix.CreateTranslation(2, 0, 0), 1)], new NavMeshContent([], []));
    }

    private static LevelPlay Play(string first = "", string second = "")
    {
        var definition = new LevelDefinition("#Chapter", "#Desc", "#Desc", "loading", new LevelStart("t_s1", "start", true, false, null, true), null,
            ["Levels/T/t_s1.xml", "Levels/T/t_s2.xml"], LevelDefinition.DefaultAvatar, [], []);
        var content = new Content(new()
        {
            ["Levels/T/t_s1.xml"] = $"<Scene><Model>m1</Model>{first}</Scene>",
            ["Levels/T/t_s2.xml"] = $"<Scene><Model>m2</Model>{second}</Scene>",
        });
        return new LevelPlay(definition, content, new LevelServices(), (Goblin, null));
    }

    internal static readonly Xna.Point Screen = new(1280, 720);

    /// <summary>The mouse over a point of the level, its left button held or not.</summary>
    internal static ControlFrame Mouse(LevelPlay play, System.Numerics.Vector3 at, bool held)
    {
        var camera = play.Camera;
        var projected = Xna.Vector4.Transform(new Xna.Vector4(at.X, at.Y, at.Z, 1), camera.View * camera.Projection((float)Screen.X / Screen.Y));
        var (x, y) = ((int)((projected.X / projected.W + 1) / 2 * Screen.X), (int)((1 - projected.Y / projected.W) / 2 * Screen.Y));
        var button = held ? Xna.Input.ButtonState.Pressed : Xna.Input.ButtonState.Released;
        var mouse = new Xna.Input.MouseState(x, y, 0, button, Xna.Input.ButtonState.Released, Xna.Input.ButtonState.Released,
            Xna.Input.ButtonState.Released, Xna.Input.ButtonState.Released);
        return new ControlFrame(new Xna.Input.KeyboardState(), mouse, new Xna.Input.KeyboardState(), mouse);
    }

    /// <summary>Ticks with the left button held over a point beside the avatar (or none held).</summary>
    private static void Run(LevelPlay play, float seconds, float right = 0)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            var avatar = play.Player.Avatar!;
            play.Tick(Tick, Mouse(play, avatar.Position + new System.Numerics.Vector3(right == 0 ? 0.01f : 3 * right, 0, 0), held: right != 0), Screen);
        }
    }

    [Fact]
    public void TheWizard_WalksToTheCursorWhileMoveIsHeld_AndTheCameraFollowsIt()
    {
        using var play = Play();
        play.Start();
        var avatar = play.Player.Avatar!;
        Assert.Equal(2, play.Camera.Ground.X, 1);                                                  // over the wizard from the start

        Run(play, 1, right: 1);

        Assert.True(avatar.Position.X > 4);
        Assert.True(play.Camera.Ground.X > 3);
        Run(play, 0.5f);
        Assert.Equal(Numerics(0, 0, 0), avatar.Body.Movement);                                   // let go: it stops
    }

    [Fact]
    public void AHeldInput_StopsTheWalk()
    {
        using var play = Play("""<Trigger id="hold"><Then><disableInput/></Then></Trigger>""");
        play.Start();
        play.Tick(Tick);                                                                           // the scripts run: the hold is on
        var at = play.Player.Avatar!.Position.X;

        Run(play, 1, right: 1);

        Assert.Equal(at, play.Player.Avatar.Position.X, 1);
    }

    [Fact]
    public void AFade_AndASceneChange_CoverTheScreen()
    {
        using var play = Play("""
            <Trigger id="dark" autorun="false"><Then><fade fadeIn="true" color="1,0,0,1" time="1"/></Then></Trigger>
            <Trigger id="light" autorun="false"><Then><fade fadeIn="false" time="1"/></Then></Trigger>
            <Trigger id="go" autorun="false"><Then><changeScene scene="t_s2" spawnPoint="start" transition="fade" transitionTime="1" spawnPlayers="true"/></Then></Trigger>
            """);
        play.Start();
        Run(play, LevelPlay.FadeIn + 0.1f);                                                         // the start's fade-in over
        var scripts = play.Scene!.Scripts!;

        scripts.ExecuteTrigger(ScriptScene.Hash("dark"), null);
        Run(play, 0.5f);
        Assert.InRange(play.Cover.Amount, 0.4f, 0.6f);
        Assert.Equal(1, play.Cover.Color.X);
        Run(play, 1);
        Assert.Equal(1, play.Cover.Amount, 3);
        scripts.ExecuteTrigger(ScriptScene.Hash("light"), null);
        Run(play, 1.1f);
        Assert.Equal(0, play.Cover.Amount, 3);

        scripts.ExecuteTrigger(ScriptScene.Hash("go"), null);
        Run(play, 0.5f);
        Assert.InRange(play.Cover.Amount, 0.4f, 0.6f);                                           // going out
        Run(play, 0.6f);
        Assert.Equal("t_s2", play.Scene!.Definition.Name);
        Run(play, 1.2f);
        Assert.Equal(0, play.Cover.Amount, 3);                                                     // back in
    }

    [Fact]
    public void ALevel_FadesInFromBlack_AndAnOverridingFadeKeepsTheSceneChangeCovered()
    {
        using var play = Play("""
            <Trigger id="go" autorun="false"><Then><changeScene scene="t_s2" spawnPoint="start" transition="fade" transitionTime="1" spawnPlayers="true"/></Then></Trigger>
            """, """
            <Trigger id="dark"><Then><fade fadeIn="true" time="0" override="true"/></Then></Trigger>
            """);
        play.Start();
        Assert.Equal(1, play.Cover.Amount, 3);                                                     // the start: black,
        Run(play, LevelPlay.FadeIn + 0.1f);
        Assert.Equal(0, play.Cover.Amount, 3);                                                     // faded in

        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
        Run(play, 3);
        Assert.Equal("t_s2", play.Scene!.Definition.Name);
        Assert.Equal(1, play.Cover.Amount, 3);                                                     // the new scene's override fade: no way back in
    }

    [Fact]
    public void ANewScenesPlainStartupFade_IsReplacedByTheWayBack_AndAStartsPlainFadeIsDropped()
    {
        using var play = Play("""
            <Trigger id="faint"><Then><fade fadeIn="true" time="0"/></Then></Trigger>
            <Trigger id="go" autorun="false"><Then><changeScene scene="t_s2" spawnPoint="start" transition="fade" transitionTime="1" spawnPlayers="true"/></Then></Trigger>
            """, """
            <Trigger id="dark"><Then><fade fadeIn="true" time="0"/></Then></Trigger>
            """);
        play.Start();
        Run(play, LevelPlay.FadeIn + 0.1f);
        Assert.Equal(0, play.Cover.Amount, 3);                                                     // the start's own fade dropped: in from black

        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
        Run(play, 3);
        Assert.Equal("t_s2", play.Scene!.Definition.Name);
        Assert.Equal(0, play.Cover.Amount, 3);                                                     // the way back replaced the plain fade
    }

    [Fact]
    public void AnOverrideDuringTheWayOut_KeepsTheScreenCovered()
    {
        using var play = Play("""
            <Trigger id="go" autorun="false"><Then><changeScene scene="t_s2" spawnPoint="start" transition="fade" transitionTime="1" spawnPlayers="true"/></Then></Trigger>
            <Trigger id="hold" autorun="false"><Then><fade fadeIn="true" time="0.5" override="true"/></Then></Trigger>
            """);
        play.Start();
        Run(play, 1);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("go"), null);
        Run(play, 0.3f);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("hold"), null);
        Run(play, 3);

        Assert.Equal("t_s2", play.Scene!.Definition.Name);
        Assert.Equal(1, play.Cover.Amount, 3);                                                     // the scene change does not come back in
    }

    [Fact]
    public void AnEndOnAClearScreen_FlashesItsColour_ThenGoes()
    {
        using var play = Play("""
            <Trigger id="flash" autorun="false"><Then><fade fadeIn="false" color="1,1,1,1" time="0.5"/></Then></Trigger>
            """);
        play.Start();
        Run(play, 1);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("flash"), null);
        play.Tick(Tick);

        Assert.True(play.Cover.Amount > 0.9f);
        Assert.Equal(1, play.Cover.Color.X);
        Run(play, 0.6f);
        Assert.Equal(0, play.Cover.Amount, 3);
    }

    private static System.Numerics.Vector3 Numerics(float x, float y, float z) => new(x, y, z);
}
