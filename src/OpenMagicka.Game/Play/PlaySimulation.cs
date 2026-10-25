using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game.Play;

/// <summary>What the game draws of a level at play: the camera at the last two ticks, the scene and the screen's cover.</summary>
public sealed class PlayFrame
{
    public FrameTiming Timing { get; set; }

    public GameCamera Previous { get; set; } = GameCamera.Initial;

    public GameCamera Current { get; set; } = GameCamera.Initial;

    /// <summary>The scene's level model asset, its name and settings; none before the level starts.</summary>
    public string? Model { get; set; }

    public string? SceneName { get; set; }

    public SceneSettings? Settings { get; set; }

    public ScreenCover Cover { get; set; }

    /// <summary>The models to draw (the characters, their items, the props, the lying items and books).</summary>
    public List<DrawnModel> Models { get; } = [];

    /// <summary>Why the level stopped (it no longer runs), or null.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// The level at play on the logic thread: WASD (or the arrows) walk the wizard, Escape quits. The level
/// starts on the first tick. An exception stops the level (the frame says why) rather than the game.
/// </summary>
public sealed class PlaySimulation(IGameHost host, LevelPlay play) : ISimulation<PlayFrame>
{
    private readonly IGameHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly LevelPlay play = play ?? throw new ArgumentNullException(nameof(play));
    private GameCamera previous = GameCamera.Initial;
    private bool started;
    private string? error;

    public void Tick(double dt)
    {
        var input = host.Input.Value;
        var keys = input.Keyboard;
        if (input.IsActive && keys.IsKeyDown(Keys.Escape))
        {
            host.RequestExit();
        }
        previous = play.Camera;
        if (error is not null)
        {
            return;
        }
        try
        {
            if (!started)
            {
                started = true;
                play.Start();
                previous = play.Camera;
                return;
            }
            var (right, forward) = input.IsActive
                ? (Axis(keys, Keys.D, Keys.A) + Axis(keys, Keys.Right, Keys.Left), Axis(keys, Keys.W, Keys.S) + Axis(keys, Keys.Up, Keys.Down))
                : (0f, 0f);
            play.Tick((float)dt, Math.Clamp(right, -1, 1), Math.Clamp(forward, -1, 1));
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            error = e.Message;
            Console.Error.WriteLine($"OpenMagicka: the level stopped: {e}");
        }
    }

    public void WriteFrame(PlayFrame frame, FrameTiming timing)
    {
        frame.Timing = timing;
        frame.Previous = previous;
        frame.Current = play.Camera;
        var scene = play.Scene;
        frame.Model = scene?.Definition.Model is { Length: > 0 } model ? model : null;
        frame.SceneName = scene?.Definition.Name;
        frame.Settings = scene?.Definition.Settings;
        frame.Cover = play.Cover;
        play.Requests.Clear();                                                // nothing takes them yet (OM-49, OM-51)
        EntityPictures.Collect(play.World, play.Looks, frame.Models);
        frame.Error = error;
    }

    private static float Axis(KeyboardState keys, Keys positive, Keys negative) =>
        (keys.IsKeyDown(positive) ? 1 : 0) - (keys.IsKeyDown(negative) ? 1 : 0);
}
