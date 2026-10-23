using System.Diagnostics;

namespace OpenMagicka.Engine.Threading;

/// <summary>
/// When a published frame's simulation state was taken: after tick <see cref="Tick"/>, which ended at
/// <see cref="TickTime"/> on the loop's clock.
/// </summary>
public readonly record struct FrameTiming(long Tick, TimeSpan TickTime, double StepSeconds)
{
    /// <summary>How far <paramref name="now"/> is past the frame's tick, in ticks, clamped to [0, 1], for interpolation.</summary>
    public double Alpha(TimeSpan now) => StepSeconds > 0 ? Math.Clamp((now - TickTime).TotalSeconds / StepSeconds, 0, 1) : 1;
}

/// <summary>The game simulation driven by a <see cref="LogicLoop{TFrame}"/>, called only on the logic thread.</summary>
public interface ISimulation<in TFrame>
{
    /// <summary>Advances the simulation by one fixed tick of <paramref name="dt"/> seconds.</summary>
    void Tick(double dt);

    /// <summary>Copies what the renderer needs into a frame slot (reused: overwrite everything).</summary>
    void WriteFrame(TFrame frame, FrameTiming timing);
}

/// <summary>
/// Runs a simulation on its own thread at a fixed tick (ADR-0001): ticks that are due run back to
/// back, then a frame is written into <see cref="Frames"/> for the render thread, then the thread
/// sleeps until the next tick. The logic thread never touches the GPU. An exception stops the
/// thread; <see cref="ThrowIfFaulted"/> rethrows it on the caller. Start, Stop and Dispose
/// belong to the owner thread; Stop and Dispose may also be called by the simulation itself.
/// </summary>
public sealed class LogicLoop<TFrame> : IDisposable
    where TFrame : class
{
    private readonly ISimulation<TFrame> simulation;
    private readonly FixedStepClock clock;
    private readonly Func<TimeSpan> now;
    // Never disposed: it has no timer, and the logic thread may still be waiting on it.
    private readonly CancellationTokenSource stop = new();
    private TimeSpan last;
    private Thread? thread;
    private volatile Exception? fault;

    /// <param name="time">The loop's clock; a monotonic stopwatch when null.</param>
    public LogicLoop(ISimulation<TFrame> simulation, Func<TFrame> createFrame, double ticksPerSecond = FixedStepClock.DefaultTicksPerSecond,
        Func<TimeSpan>? time = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        this.simulation = simulation;
        clock = new FixedStepClock(ticksPerSecond);
        Frames = new FrameExchange<TFrame>(createFrame);
        if (time is null)
        {
            var stopwatch = Stopwatch.StartNew();
            time = () => stopwatch.Elapsed;
        }
        now = time;
        last = now();
    }

    public FrameExchange<TFrame> Frames { get; }

    /// <summary>The loop's clock, for the renderer's interpolation (<see cref="FrameTiming.Alpha"/>).</summary>
    public TimeSpan Now => now();

    public long Ticks => Interlocked.Read(ref ticks);

    public bool IsRunning => thread is { IsAlive: true };

    private long ticks;

    /// <summary>
    /// One iteration: runs the ticks due since the last call and publishes a frame after them.
    /// Runs on the logic thread; tests call it directly instead of starting the thread.
    /// </summary>
    internal int Step()
    {
        var time = now();
        var elapsed = time - last;
        last = time;
        var steps = clock.Advance(elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
        for (var i = 0; i < steps && !stop.IsCancellationRequested; i++)
        {
            simulation.Tick(clock.StepSeconds);
            Interlocked.Increment(ref ticks);
        }
        if (steps > 0)
        {
            var tickTime = time - TimeSpan.FromSeconds(clock.Alpha * clock.StepSeconds);
            simulation.WriteFrame(Frames.BeginWrite(), new FrameTiming(clock.Tick, tickTime, clock.StepSeconds));
            Frames.Publish();
        }
        return steps;
    }

    public void Start()
    {
        if (thread is not null)
        {
            throw new InvalidOperationException("the logic loop was already started");
        }
        last = now();
        thread = new Thread(Run) { Name = "Logic", IsBackground = true };
        thread.Start();
    }

    /// <summary>
    /// Stops the thread and waits for it (not when called from the logic thread itself); safe to
    /// call more than once. Returns false if the thread did not end within the timeout.
    /// </summary>
    public bool Stop(TimeSpan? timeout = null)
    {
        stop.Cancel();
        if (thread is null || thread == Thread.CurrentThread)
        {
            return true;
        }
        if (timeout is { } limit)
        {
            return thread.Join(limit);
        }
        thread.Join();
        return true;
    }

    public void ThrowIfFaulted()
    {
        if (fault is { } e)
        {
            throw new InvalidOperationException($"the logic thread failed: {e.Message}", e);
        }
    }

    public void Dispose() => Stop();

    private void Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Step();
                WaitForNextTick();
            }
        }
        catch (Exception e)
        {
            fault = e;
        }
    }

    /// <summary>
    /// Sleeps most of the way to the next tick, then spins the last stretch. The clock catches up
    /// any lateness: with a coarse system timer (a minimized window on Windows) a frame may carry
    /// two ticks, but no tick is lost.
    /// </summary>
    private void WaitForNextTick()
    {
        var due = last + TimeSpan.FromSeconds((1 - clock.Alpha) * clock.StepSeconds);
        var remaining = due - now();
        if (remaining > TimeSpan.FromMilliseconds(2))
        {
            stop.Token.WaitHandle.WaitOne(remaining - TimeSpan.FromMilliseconds(1));
        }
        var spin = new SpinWait();
        while (now() < due && !stop.IsCancellationRequested)
        {
            spin.SpinOnce(sleep1Threshold: -1);
        }
    }
}
