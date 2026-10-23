using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game.Viewer;

/// <summary>A scene the renderer has loaded: its index, bounds, camera mesh and zoom.</summary>
public sealed record LoadedScene(int Scene, BoundingBox Bounds)
{
    public CameraMesh? Mesh { get; init; }

    /// <summary>The scene's <c>Magnify</c> setting.</summary>
    public float? Magnify { get; init; }
}

/// <summary>What the viewer draws: the camera at the last two ticks and the scene to show.</summary>
public sealed class ViewerFrame
{
    public FrameTiming Timing { get; set; }

    public FreeCamera Previous { get; set; } = new(Vector3.Zero, 0, 0);

    public FreeCamera Current { get; set; } = new(Vector3.Zero, 0, 0);

    /// <summary>The game camera at the last two ticks; null when the free camera is used.</summary>
    public GameCamera? PreviousGame { get; set; }

    public GameCamera? CurrentGame { get; set; }

    public int Scene { get; set; }
}

/// <summary>
/// The viewer's logic (logic thread): WASD moves, Q/E or Space/C go down/up, Shift is fast, the
/// right mouse button looks around, PageUp/PageDown switch scenes, Tab jumps to the next section
/// (scenes, models), F frames the scene, G switches to the game's camera and back (WASD then walk
/// a stand-in player), Escape quits. When the renderer reports a newly loaded scene's bounds, the
/// camera frames them.
/// </summary>
/// <param name="gameCamera">Start with the game camera.</param>
public sealed class ViewerSimulation(IGameHost host, int sceneCount, int startScene, IReadOnlyList<int>? sectionStarts = null, bool gameCamera = false)
    : ISimulation<ViewerFrame>
{
    public const float Speed = 150;
    public const float FastSpeed = 600;
    public const float MouseRadiansPerPixel = 0.005f;

    private FreeCamera previous = new(new Vector3(0, 100, 200), 0, -0.4f);
    private InputState last;
    private int framedScene = -1;
    private LoadedScene? framed;
    private bool useGameCamera = gameCamera;
    private ViewerGameCamera? game;
    private GameCamera? previousGame;

    /// <summary>Set by the renderer when a scene is loaded.</summary>
    public LatestValue<LoadedScene?> Loaded { get; } = new(null);

    public FreeCamera Camera { get; private set; } = new(new Vector3(0, 100, 200), 0, -0.4f);

    public int Scene { get; private set; } = Math.Clamp(startScene, 0, Math.Max(sceneCount - 1, 0));

    public void Tick(double dt)
    {
        previous = Camera;
        previousGame = game?.Camera;
        var input = host.Input.Value;
        var keys = input.Keyboard;
        if (input.IsActive && keys.IsKeyDown(Keys.Escape))
        {
            host.RequestExit();
        }
        if (Pressed(input, Keys.PageDown))
        {
            Scene = sceneCount == 0 ? 0 : (Scene + 1) % sceneCount;
        }
        if (Pressed(input, Keys.PageUp))
        {
            Scene = sceneCount == 0 ? 0 : (Scene + sceneCount - 1) % sceneCount;
        }
        if (Pressed(input, Keys.Tab) && sectionStarts is { Count: > 1 } starts)
        {
            var next = starts.FirstOrDefault(s => s > Scene, starts[0]);
            Scene = Math.Clamp(next, 0, Math.Max(sceneCount - 1, 0));
        }
        if (Loaded.Value is { } loaded && loaded.Scene == Scene && (framedScene != Scene || Pressed(input, Keys.F)))
        {
            framedScene = Scene;
            framed = loaded;
            Camera = FreeCamera.Framing(loaded.Bounds);
            previous = Camera;
            game = null;
        }
        if (Pressed(input, Keys.G))
        {
            useGameCamera = !useGameCamera;
            if (!useGameCamera && game is not null)
            {
                Camera = previous = game.Free;
            }
            game = null;
        }
        if (useGameCamera && game is null && framed is { } scene && scene.Scene == Scene)
        {
            game = ViewerGameCamera.Start(Camera, GroundHeight(scene.Bounds), scene.Mesh, scene.Magnify);
            previousGame = game.Camera;
        }
        if (game is not null)
        {
            // The camera keeps easing in an inactive window; only the player stands still.
            var (right, forward) = input.IsActive ? (Axis(keys, Keys.D, Keys.A), Axis(keys, Keys.W, Keys.S)) : (0f, 0f);
            game = game.Move(right, forward, (float)dt, MoveSpeed(keys));
        }
        else if (input.IsActive)
        {
            // Both ticks in an active window: a position from before focus came back is no reference.
            if (last.IsActive && input.Mouse.RightButton == ButtonState.Pressed && last.Mouse.RightButton == ButtonState.Pressed)
            {
                Camera = Camera.Turn(-(input.Mouse.X - last.Mouse.X) * MouseRadiansPerPixel, -(input.Mouse.Y - last.Mouse.Y) * MouseRadiansPerPixel);
            }
            var forward = Axis(keys, Keys.W, Keys.S);
            var right = Axis(keys, Keys.D, Keys.A);
            var up = Axis(keys, Keys.E, Keys.Q) + Axis(keys, Keys.Space, Keys.C);
            Camera = Camera.Move(forward, right, Math.Clamp(up, -1, 1), (float)dt, MoveSpeed(keys));
        }
        last = input;
    }

    public void WriteFrame(ViewerFrame frame, FrameTiming timing)
    {
        frame.Timing = timing;
        frame.Previous = previous;
        frame.Current = Camera;
        frame.PreviousGame = game is null ? null : previousGame ?? game.Camera;
        frame.CurrentGame = game?.Camera;
        frame.Scene = Scene;
    }

    /// <summary>The levels' ground is near height 0; kept within the level for those that lie elsewhere.</summary>
    public static float GroundHeight(BoundingBox bounds) => Math.Clamp(0, bounds.Min.Y, bounds.Max.Y);

    private bool Pressed(InputState input, Keys key) =>
        input.IsActive && input.Keyboard.IsKeyDown(key) && !last.Keyboard.IsKeyDown(key);

    private static float MoveSpeed(KeyboardState keys) => keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) ? FastSpeed : Speed;

    private static float Axis(KeyboardState keys, Keys positive, Keys negative) =>
        (keys.IsKeyDown(positive) ? 1 : 0) - (keys.IsKeyDown(negative) ? 1 : 0);
}
