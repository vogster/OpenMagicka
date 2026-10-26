using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Input;
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

    /// <summary>What the screen shows over the world (the speech boxes).</summary>
    public Engine.Rendering.GuiPictures Gui { get; } = new();

    /// <summary>The spells' own pictures (beams, their lights).</summary>
    public Engine.Rendering.SpellPictures Spells { get; } = new();

    /// <summary>Where the level's animated parts are, as its batches number them (LevelParts.Order).</summary>
    public List<System.Numerics.Matrix4x4> Parts { get; } = [];

    /// <summary>Why the level stopped (it no longer runs), or null.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// The level at play on the logic thread: the player's keyboard and mouse, or its pad (the one last touched), drive the wizard (both ticks of a
/// focused window are needed, so nothing is pressed by a focus coming back). Escape quits; in the game (OM-53) it,
/// or the pad's Start, pauses instead (<paramref name="escape"/>). The level starts on the first tick. An exception
/// stops the level (the frame says why) rather than the game.
/// </summary>
public sealed class PlaySimulation(IGameHost host, LevelPlay play, Action? escape = null) : ISimulation<PlayFrame>
{
    private readonly IGameHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly LevelPlay play = play ?? throw new ArgumentNullException(nameof(play));
    private readonly Action? pauses = escape;
    private GameCamera previous = GameCamera.Initial;
    private InputState last;
    private bool usingPad, wasUsingPad;
    private bool started, escaping;
    private string? error;

    /// <summary>The level stopped (an exception), or null.</summary>
    public string? Error => error;

    /// <summary>
    /// The level is left paused: the next tick starts afresh (the wizard lets go of what it held), and an Escape or
    /// Start still held from the pause menu does not pause again.
    /// </summary>
    public void Pause()
    {
        last = default;
        escaping = true;
    }

    public void Tick(double dt)
    {
        var input = host.Input.Value;
        var keys = input.Keyboard;
        var held = input.IsActive && (keys.IsKeyDown(Keys.Escape) || (pauses is not null && input.Pad.IsButtonDown(Buttons.Start)));
        var pressed = held && !escaping;
        escaping = held;
        if (pressed && pauses is { } pause)
        {
            pause();
            return;
        }
        if (held && pauses is null)
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
            var frame = input.IsActive && last.IsActive ? new ControlFrame(input.Keyboard, input.Mouse, last.Keyboard, last.Mouse) : (ControlFrame?)null;
            usingPad = Device(input, last, usingPad);
            var pad = frame is not null && usingPad ? new PadFrame(input.Pad, last.Pad, play.PadBindings) : (PadFrame?)null;
            last = input;
            if (play.Player.Avatar is { IsDead: false } avatar && (frame is null || usingPad != wasUsingPad))
            {
                ControlTargets.LetGo(avatar);                                 // the window lost focus, or the other device took over
                play.Controls.Reset();
                play.Pad.Reset();
            }
            wasUsingPad = usingPad;
            play.Tick((float)dt, frame, input.Screen, pad);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            error = e.Message;
            Console.Error.WriteLine($"OpenMagicka: the level stopped: {e}");
        }
    }

    /// <summary>
    /// Which device drives the player: the one last touched (a pad's button or a stick past its dead zone; a
    /// key, a mouse button, the wheel or the mouse moving), so the two never fight; a pad unplugged gives way.
    /// </summary>
    public static bool Device(InputState input, InputState last, bool usingPad)
    {
        var pad = input.Pad;
        var padTouched = pad.IsConnected && (pad.Buttons != last.Pad.Buttons || pad.ThumbSticks.Left.Length() > GamePadControl.DeadZone
            || pad.ThumbSticks.Right.Length() > GamePadControl.GestureRest || pad.Triggers.Left >= PadBindings.Held || pad.Triggers.Right >= PadBindings.Held);
        var keysTouched = input.Keyboard.GetPressedKeys().Length > 0 || input.Mouse.LeftButton == ButtonState.Pressed
            || input.Mouse.RightButton == ButtonState.Pressed || input.Mouse.MiddleButton == ButtonState.Pressed
            || input.Mouse.X != last.Mouse.X || input.Mouse.Y != last.Mouse.Y || input.Mouse.ScrollWheelValue != last.Mouse.ScrollWheelValue;
        return pad.IsConnected && (padTouched || (usingPad && !keysTouched));
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
        play.Spells.WriteModels(frame.Models);
        play.Spells.Write(frame.Spells);
        frame.Gui.Clear();
        play.WriteHud(frame.Gui);
        play.WriteTutorial(frame.Gui);
        play.Dialogs?.Write(frame.Gui);
        play.WriteCutscene(frame.Gui);
        frame.Gui.Prompt = play.Prompt;
        if (scene?.Scripts is { } scripts)
        {
            scripts.Places.Placements(frame.Parts);
        }
        else
        {
            frame.Parts.Clear();
        }
        frame.Error = error;
    }
}
