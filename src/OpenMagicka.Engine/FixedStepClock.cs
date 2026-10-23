namespace OpenMagicka.Engine;

/// <summary>
/// Turns variable frame times into a fixed number of simulation ticks.
/// The simulation always advances in steps of <see cref="StepSeconds"/>, so game behaviour
/// does not depend on the display refresh rate; <see cref="Alpha"/> tells the renderer how far
/// the current frame lies between the last two ticks.
/// </summary>
public sealed class FixedStepClock
{
    public const double DefaultTicksPerSecond = 60;
    public const int DefaultMaxStepsPerFrame = 5;

    private readonly int maxStepsPerFrame;
    private double accumulatedSeconds;

    public FixedStepClock(double ticksPerSecond = DefaultTicksPerSecond, int maxStepsPerFrame = DefaultMaxStepsPerFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);

        StepSeconds = 1.0 / ticksPerSecond;
        this.maxStepsPerFrame = maxStepsPerFrame;
    }

    /// <summary>Length of one simulation tick in seconds; the simulation's dt.</summary>
    public double StepSeconds { get; }

    /// <summary>Number of simulation ticks run since creation.</summary>
    public long Tick { get; private set; }

    /// <summary>Fraction of the next tick already elapsed, in [0, 1).</summary>
    public double Alpha => Math.Min(accumulatedSeconds / StepSeconds, Math.BitDecrement(1.0));

    /// <summary>
    /// Adds the frame time and returns how many ticks to simulate now. After a stall longer than
    /// <c>maxStepsPerFrame</c> ticks the backlog is dropped instead of being caught up.
    /// </summary>
    public int Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        accumulatedSeconds += elapsed.TotalSeconds;
        var due = (long)Math.Floor(accumulatedSeconds / StepSeconds);
        var steps = (int)Math.Min(due, maxStepsPerFrame);
        accumulatedSeconds = due > maxStepsPerFrame
            ? 0
            : Math.Max(0, accumulatedSeconds - steps * StepSeconds);
        Tick += steps;
        return steps;
    }
}
