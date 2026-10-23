using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game.Viewer;

/// <summary>What the viewer draws: the camera at the last two ticks and the scene to show.</summary>
public sealed class ViewerFrame
{
    public FrameTiming Timing { get; set; }

    public FreeCamera Previous { get; set; } = new(Vector3.Zero, 0, 0);

    public FreeCamera Current { get; set; } = new(Vector3.Zero, 0, 0);

    public int Scene { get; set; }
}

/// <summary>
/// The viewer's logic (logic thread): WASD moves, Q/E or Space/C go down/up, Shift is fast, the
/// right mouse button looks around, PageUp/PageDown switch scenes, Tab jumps to the next section
/// (scenes, models), F frames the scene, Escape quits. When the renderer reports a newly loaded
/// scene's bounds, the camera frames them.
/// </summary>
public sealed class ViewerSimulation(IGameHost host, int sceneCount, int startScene, IReadOnlyList<int>? sectionStarts = null) : ISimulation<ViewerFrame>
{
    public const float Speed = 150;
    public const float FastSpeed = 600;
    public const float MouseRadiansPerPixel = 0.005f;

    private FreeCamera previous = new(new Vector3(0, 100, 200), 0, -0.4f);
    private InputState last;
    private int framedScene = -1;
    private BoundingBox framedBounds;

    /// <summary>Set by the renderer when a scene is loaded: its index and bounds.</summary>
    public LatestValue<(int Scene, BoundingBox Bounds)?> LevelBounds { get; } = new(null);

    public FreeCamera Camera { get; private set; } = new(new Vector3(0, 100, 200), 0, -0.4f);

    public int Scene { get; private set; } = Math.Clamp(startScene, 0, Math.Max(sceneCount - 1, 0));

    public void Tick(double dt)
    {
        previous = Camera;
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
        if (LevelBounds.Value is { } loaded && loaded.Scene == Scene && (framedScene != Scene || Pressed(input, Keys.F)))
        {
            framedScene = Scene;
            framedBounds = loaded.Bounds;
            Camera = FreeCamera.Framing(framedBounds);
            previous = Camera;
        }
        if (input.IsActive)
        {
            // Both ticks in an active window: a position from before focus came back is no reference.
            if (last.IsActive && input.Mouse.RightButton == ButtonState.Pressed && last.Mouse.RightButton == ButtonState.Pressed)
            {
                Camera = Camera.Turn(-(input.Mouse.X - last.Mouse.X) * MouseRadiansPerPixel, -(input.Mouse.Y - last.Mouse.Y) * MouseRadiansPerPixel);
            }
            var forward = Axis(keys, Keys.W, Keys.S);
            var right = Axis(keys, Keys.D, Keys.A);
            var up = Axis(keys, Keys.E, Keys.Q) + Axis(keys, Keys.Space, Keys.C);
            var speed = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) ? FastSpeed : Speed;
            Camera = Camera.Move(forward, right, Math.Clamp(up, -1, 1), (float)dt, speed);
        }
        last = input;
    }

    public void WriteFrame(ViewerFrame frame, FrameTiming timing)
    {
        frame.Timing = timing;
        frame.Previous = previous;
        frame.Current = Camera;
        frame.Scene = Scene;
    }

    private bool Pressed(InputState input, Keys key) =>
        input.IsActive && input.Keyboard.IsKeyDown(key) && !last.Keyboard.IsKeyDown(key);

    private static float Axis(KeyboardState keys, Keys positive, Keys negative) =>
        (keys.IsKeyDown(positive) ? 1 : 0) - (keys.IsKeyDown(negative) ? 1 : 0);
}
