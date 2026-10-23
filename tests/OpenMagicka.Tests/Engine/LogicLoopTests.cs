using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Tests.Engine;

public class LogicLoopTests
{
    private sealed class Frame
    {
        public long Ticks;
        public FrameTiming Timing;
    }

    private sealed class Counter : ISimulation<Frame>
    {
        public List<double> Steps { get; } = [];

        public Exception? FailWith { get; set; }

        public Action? OnTick { get; set; }

        public void Tick(double dt)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }
            Steps.Add(dt);
            OnTick?.Invoke();
        }

        public void WriteFrame(Frame frame, FrameTiming timing)
        {
            frame.Ticks = Steps.Count;
            frame.Timing = timing;
        }
    }

    private sealed class FakeClock
    {
        public TimeSpan Now { get; set; }
    }

    private static (LogicLoop<Frame> Loop, Counter Sim, FakeClock Clock) Create(double ticksPerSecond = 50)
    {
        var sim = new Counter();
        var clock = new FakeClock();
        return (new LogicLoop<Frame>(sim, () => new Frame(), ticksPerSecond, () => clock.Now), sim, clock);
    }

    [Fact]
    public void Step_RunsTheTicksThatAreDue_AndPublishesAFrameAfterThem()
    {
        var (loop, sim, clock) = Create();

        clock.Now = TimeSpan.FromMilliseconds(50);
        Assert.Equal(2, loop.Step());

        Assert.Equal([0.02, 0.02], sim.Steps);
        var frame = loop.Frames.Acquire()!;
        Assert.Equal(2, frame.Ticks);
        Assert.Equal(2, frame.Timing.Tick);
        Assert.Equal(TimeSpan.FromMilliseconds(40), frame.Timing.TickTime);   // the second tick ended at 40 ms
    }

    [Fact]
    public void Step_WithNoTickDue_PublishesNothing()
    {
        var (loop, sim, clock) = Create();
        clock.Now = TimeSpan.FromMilliseconds(5);

        Assert.Equal(0, loop.Step());
        Assert.Empty(sim.Steps);
        Assert.Null(loop.Frames.Acquire());
    }

    [Fact]
    public void Alpha_IsHowFarTheRendererIsPastTheFramesTick()
    {
        var timing = new FrameTiming(Tick: 3, TickTime: TimeSpan.FromMilliseconds(60), StepSeconds: 0.02);

        Assert.Equal(0.5, timing.Alpha(TimeSpan.FromMilliseconds(70)), 9);
        Assert.Equal(0, timing.Alpha(TimeSpan.FromMilliseconds(50)));         // clock read before the tick
        Assert.Equal(1, timing.Alpha(TimeSpan.FromMilliseconds(500)));        // logic stalled: hold the newest state
    }

    [Fact]
    public void TheThread_RunsTicksUntilStopped()
    {
        var sim = new Counter();
        using var loop = new LogicLoop<Frame>(sim, () => new Frame(), ticksPerSecond: 200);

        loop.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (loop.Frames.Acquire() is not { Ticks: >= 10 } && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }
        loop.Stop();
        var ticks = loop.Ticks;
        Thread.Sleep(30);

        Assert.True(ticks >= 10, $"only {ticks} ticks");
        Assert.Equal(ticks, loop.Ticks);              // stopped: no more ticks
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public void AnExceptionOnTheLogicThread_StopsItAndSurfacesOnTheCaller()
    {
        var sim = new Counter { FailWith = new InvalidOperationException("boom") };
        using var loop = new LogicLoop<Frame>(sim, () => new Frame(), ticksPerSecond: 200);

        loop.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (loop.IsRunning && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        var error = Assert.Throws<InvalidOperationException>(loop.ThrowIfFaulted);
        Assert.Equal("boom", error.InnerException!.Message);
    }

    [Fact]
    public void DisposingFromTheLogicThread_StopsItCleanly()
    {
        var sim = new Counter();
        var loop = new LogicLoop<Frame>(sim, () => new Frame(), ticksPerSecond: 200);
        sim.OnTick = () => loop.Dispose();

        loop.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (loop.IsRunning && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.False(loop.IsRunning);
        loop.ThrowIfFaulted();                       // no ObjectDisposedException from the wait
        Assert.Equal(1, loop.Ticks);
    }

    [Fact]
    public void ADefaultTiming_HasNoNaNAlpha()
    {
        Assert.Equal(1, default(FrameTiming).Alpha(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task LatestValue_HandsOverWholeValues()
    {
        var latest = new LatestValue<(int, int)>((0, 0));
        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 100_000; i++)
            {
                latest.Value = (i, -i);
            }
        });
        while (!writer.IsCompleted)
        {
            var (a, b) = latest.Value;
            Assert.Equal(a, -b);
        }
        await writer;
        Assert.Equal((100_000, -100_000), latest.Value);
    }

    [Fact]
    public void StartTwice_IsAnError_AndStopIsIdempotent()
    {
        var (loop, _, _) = Create();
        using (loop)
        {
            loop.Start();
            Assert.Throws<InvalidOperationException>(loop.Start);
            loop.Stop();
            loop.Stop();
        }
    }
}
