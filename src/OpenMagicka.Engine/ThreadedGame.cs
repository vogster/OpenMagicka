using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine;

/// <summary>
/// The input of one frame, polled on the render thread (where SDL delivers it): the keyboard, the mouse,
/// whether the window has focus, its back buffer's size (for the cursor), and the first pad (an SDL game
/// controller; not connected when none is). The logic thread sees the latest snapshot only: a press and
/// release between two ticks is missed.
/// </summary>
public readonly record struct InputState(KeyboardState Keyboard, MouseState Mouse, bool IsActive, Point Screen = default, GamePadState Pad = default);

/// <summary>What the simulation may use of the game from the logic thread.</summary>
public interface IGameHost
{
    LatestValue<InputState> Input { get; }

    /// <summary>Asks the game to close; safe from any thread.</summary>
    void RequestExit();
}

/// <summary>
/// FNA game with the threading model of ADR-0001 (docs/engine/ARCHITECTURE.md §2, §15): the
/// simulation runs on a logic thread at a fixed tick and publishes CPU frames through a triple
/// buffer; this (the render) thread polls input, runs the GPU work other threads queued, and
/// draws the newest frame as often as the display allows. Only this thread touches the GPU.
/// </summary>
public abstract class ThreadedGame<TFrame> : Microsoft.Xna.Framework.Game, IGameHost
    where TFrame : class
{
    /// <summary>GPU work run per frame (items and time), so that loading does not stall drawing.</summary>
    public const int GpuWorkPerFrame = 32;

    public static readonly TimeSpan GpuWorkTimePerFrame = TimeSpan.FromMilliseconds(4);

    /// <summary>How long shutdown waits for the logic thread.</summary>
    public static readonly TimeSpan LogicStopTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<TFrame> createFrame;
    private readonly double ticksPerSecond;
    private LogicLoop<TFrame>? logic;
    private volatile bool exitRequested;

    protected ThreadedGame(Func<TFrame> createFrame, double ticksPerSecond = FixedStepClock.DefaultTicksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(createFrame);
        this.createFrame = createFrame;
        this.ticksPerSecond = ticksPerSecond;
        Graphics = new GraphicsDeviceManager(this)
        {
            SynchronizeWithVerticalRetrace = true,
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        // Frames are drawn as fast as the display allows; the logic thread keeps the tick.
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected GraphicsDeviceManager Graphics { get; }

    /// <summary>Work for the render thread: loaders enqueue GPU resource creation here.</summary>
    public RenderQueue<GraphicsDevice> GpuWork { get; } = new();

    /// <summary>The input of the latest frame, for the simulation to read on the logic thread.</summary>
    public LatestValue<InputState> Input { get; } = new(default);

    /// <summary>Asks the game to close; safe from any thread.</summary>
    public void RequestExit() => exitRequested = true;

    /// <summary>The simulation to run on the logic thread; called once, when the game starts.</summary>
    protected abstract ISimulation<TFrame> CreateSimulation();

    /// <summary>Draws a frame (null until the first one is published); <paramref name="now"/> is on the frame timing's clock.</summary>
    protected abstract void Render(TFrame? frame, TimeSpan now);

    protected override void BeginRun()
    {
        base.BeginRun();
        logic = new LogicLoop<TFrame>(CreateSimulation(), createFrame, ticksPerSecond);
        logic.Start();
    }

    protected sealed override void Update(GameTime gameTime)
    {
        logic?.ThrowIfFaulted();
        var screen = new Point(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        Input.Value = new InputState(Keyboard.GetState(), Mouse.GetState(), IsActive, screen, GamePad.GetState(PlayerIndex.One));
        GpuWork.RunPending(GraphicsDevice, GpuWorkPerFrame, GpuWorkTimePerFrame);
        if (exitRequested)
        {
            Exit();
        }
        base.Update(gameTime);
    }

    protected sealed override void Draw(GameTime gameTime)
    {
        if (logic is not null)
        {
            Render(logic.Frames.Acquire(), logic.Now);
        }
        base.Draw(gameTime);
    }

    /// <summary>
    /// Closes the GPU queue first, which cancels work the logic thread may be waiting for, then
    /// stops the logic thread. A failure it hit after the last frame is reported, not lost.
    /// </summary>
    protected override void OnExiting(object sender, EventArgs args)
    {
        GpuWork.Close();
        if (logic is not null)
        {
            if (!logic.Stop(LogicStopTimeout))
            {
                Console.Error.WriteLine($"OpenMagicka: the logic thread did not stop within {LogicStopTimeout.TotalSeconds} s");
            }
            try
            {
                logic.ThrowIfFaulted();
            }
            catch (InvalidOperationException e)
            {
                Console.Error.WriteLine($"OpenMagicka: {e.Message}\n{e.InnerException}");
            }
        }
        base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GpuWork.Close();
            logic?.Stop(LogicStopTimeout);
        }
        base.Dispose(disposing);
    }
}
