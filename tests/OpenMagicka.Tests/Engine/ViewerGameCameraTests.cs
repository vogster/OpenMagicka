using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Tests.Engine;

public class ViewerGameCameraTests
{
    private static readonly FreeCamera LookingDown = new(new Vector3(10, 100, 100), 0, -MathHelper.PiOver4);

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void Starting_PutsTheStandInPlayerWhereTheFreeCameraLooks_AndTheCameraOverIt()
    {
        var game = ViewerGameCamera.Start(LookingDown, groundHeight: 0, mesh: null, magnify: 1.5f);

        Near(new Vector3(10, 0, 0), game.Player);
        Near(game.Player, game.Camera.Ground);
        Near(game.Player + GameCamera.Offset, game.Camera.Eye);
        Assert.Equal((1.5f, 1.5f), (game.Camera.DefaultMagnification, game.Camera.Magnification));
    }

    [Fact]
    public void Moving_WalksThePlayerOverTheGround_AndTheCameraFollows()
    {
        var game = ViewerGameCamera.Start(LookingDown, 0, null, null);

        var moved = game.Move(right: 1, forward: 1, dt: 0.1f, speed: 100);

        Near(new Vector3(20, 0, -10), moved.Player);
        Assert.True(moved.Camera.Ground.X > game.Camera.Ground.X && moved.Camera.Ground.Z < game.Camera.Ground.Z, "follows");
        Near(new Vector3(10, 0, 0), game.Player);   // immutable
    }

    [Fact]
    public void TheCamera_StaysOnTheCameraMesh()
    {
        var mesh = new CameraMesh(new CollisionMeshContent([new Vector3(0, 0, -5), new Vector3(5, 0, -5), new Vector3(0, 0, 5)], [new CollisionTriangle(0, 1, 2)]));

        var game = ViewerGameCamera.Start(LookingDown, 0, mesh, null);

        Near(new Vector3(4, 0, -3), game.Camera.Ground);   // the nearest point of the long edge
    }

    [Fact]
    public void Leaving_GivesAFreeCameraAtTheEye_LookingTheSameWay()
    {
        var free = ViewerGameCamera.Start(LookingDown, 0, null, null).Free;

        Near(new Vector3(10, 144, 171), free.Position);
        Near(GameCamera.Direction, free.Forward);
    }

    [Fact]
    public void G_SwitchesTheViewerToTheGameCameraAndBack()
    {
        var host = new ViewerTests.Host();
        var simulation = new ViewerSimulation(host, sceneCount: 1, startScene: 0);
        simulation.Loaded.Value = new LoadedScene(0, new BoundingBox(new Vector3(-50, 0, -50), new Vector3(50, 10, 50)));
        simulation.Tick(0.1);
        var frame = new ViewerFrame();

        host.Input.Value = ViewerTests.Keys(Keys.G);
        simulation.Tick(0.1);
        host.Input.Value = ViewerTests.Keys(Keys.D);
        simulation.Tick(0.1);
        simulation.WriteFrame(frame, new FrameTiming(1, TimeSpan.Zero, 0.1));

        Assert.NotNull(frame.CurrentGame);
        Assert.NotNull(frame.PreviousGame);
        Assert.True(frame.CurrentGame.Eye.X > frame.PreviousGame.Eye.X, "D walks the player right");

        host.Input.Value = ViewerTests.Keys(Keys.G);
        simulation.Tick(0.1);
        simulation.WriteFrame(frame, new FrameTiming(2, TimeSpan.Zero, 0.1));

        Assert.Null(frame.CurrentGame);
        Near(simulation.Camera.Position, frame.Current.Position);
        Assert.True(Vector3.Distance(simulation.Camera.Forward, GameCamera.Direction) < 1e-3f, "the free camera takes over the game camera's view");
    }

    [Fact]
    public void TheViewer_CanStartWithTheGameCamera()
    {
        var host = new ViewerTests.Host();
        var simulation = new ViewerSimulation(host, sceneCount: 1, startScene: 0, gameCamera: true);
        var frame = new ViewerFrame();

        simulation.Tick(0.1);
        simulation.WriteFrame(frame, new FrameTiming(1, TimeSpan.Zero, 0.1));
        Assert.Null(frame.CurrentGame);           // waits for the scene

        simulation.Loaded.Value = new LoadedScene(0, new BoundingBox(new Vector3(-50, 0, -50), new Vector3(50, 10, 50))) { Magnify = 2 };
        simulation.Tick(0.1);
        simulation.WriteFrame(frame, new FrameTiming(2, TimeSpan.Zero, 0.1));

        Assert.NotNull(frame.CurrentGame);
        Assert.Equal(2, frame.CurrentGame.Magnification);
    }
}
