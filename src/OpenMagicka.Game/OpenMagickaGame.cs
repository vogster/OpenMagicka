using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
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
/// and is interpolated on the render thread, which shows the threading model at work.
/// </summary>
public sealed class OpenMagickaGame : ThreadedGame<GameFrame>
{
    public OpenMagickaGame()
        : base(() => new GameFrame())
    {
        Window.Title = "OpenMagicka";
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
