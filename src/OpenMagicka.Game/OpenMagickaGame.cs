using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game;

/// <summary>What the renderer draws: for now the background colour's phase at the last two ticks.</summary>
public sealed class GameFrame
{
    public FrameTiming Timing { get; set; }

    public double Phase { get; set; }

    public double PreviousPhase { get; set; }
}

/// <summary>
/// Top-level game. For now a window whose background colour cycles on the logic thread's tick
/// and is interpolated on the render thread, which shows the threading model at work. With the
/// game's content it warms up all original effects in the background (--check-effects: then exits).
/// </summary>
public sealed class OpenMagickaGame : ThreadedGame<GameFrame>
{
    private readonly GameOptions options;
    private ShaderLibrary? shaders;

    public OpenMagickaGame(GameOptions options)
        : base(() => new GameFrame())
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        Window.Title = "OpenMagicka";
    }

    /// <summary>For --check-effects: 1 until the check has passed (closing the window early is a failure).</summary>
    public int ExitCode { get; private set; }

    protected override void LoadContent()
    {
        base.LoadContent();
        if (options.ContentDirectory is not { } content)
        {
            return;
        }
        ExitCode = options.CheckEffects ? 1 : 0;
        var loader = new ContentLoader(new ContentFileSystem(content), XnbReaderRegistry.Magicka);
        shaders = new ShaderLibrary(new ShaderSource(loader, GraphicsDriver.HasOwnFna3d()), GpuWork);
        var library = shaders;
        Task.Run(() => EffectWarmup.Run(library, GpuWork)).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                return;   // the game closed first
            }
            var report = task.IsCompletedSuccessfully ? task.Result : new EffectWarmupReport([], [], [$"warmup: {task.Exception?.GetBaseException().Message}"]);
            Console.WriteLine(report.Summary);
            if (options.CheckEffects)
            {
                ExitCode = report.ExitCode;
                RequestExit();
            }
        }, TaskScheduler.Default);
    }

    protected override void UnloadContent()
    {
        shaders?.Dispose();
        base.UnloadContent();
    }

    protected override ISimulation<GameFrame> CreateSimulation() => new Simulation(this);

    protected override void Render(GameFrame? frame, TimeSpan now)
    {
        var phase = frame is null
            ? 0
            : double.Lerp(frame.PreviousPhase, frame.Phase, frame.Timing.Alpha(now));
        var pulse = (float)(0.5 + 0.5 * Math.Sin(phase));
        GraphicsDevice.Clear(new Color(24 + (int)(16 * pulse), 20, 36 + (int)(24 * pulse)));
    }

    /// <summary>Runs on the logic thread; it sees the game only through <see cref="IGameHost"/>.</summary>
    private sealed class Simulation(IGameHost game) : ISimulation<GameFrame>
    {
        private const double RadiansPerSecond = 0.8;

        private double previousPhase;
        private double phase;

        public void Tick(double dt)
        {
            var input = game.Input.Value;
            if (input.IsActive && input.Keyboard.IsKeyDown(Keys.Escape))
            {
                game.RequestExit();
            }
            previousPhase = phase;
            phase += RadiansPerSecond * dt;
        }

        public void WriteFrame(GameFrame frame, FrameTiming timing)
        {
            frame.Timing = timing;
            frame.Phase = phase;
            frame.PreviousPhase = previousPhase;
        }
    }
}
