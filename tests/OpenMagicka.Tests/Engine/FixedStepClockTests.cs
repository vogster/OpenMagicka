using OpenMagicka.Engine;

namespace OpenMagicka.Tests.Engine;

public class FixedStepClockTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void StepSeconds_IsInverseOfTickRate()
    {
        var clock = new FixedStepClock(ticksPerSecond: 60);

        Assert.Equal(1.0 / 60, clock.StepSeconds, precision: 12);
    }

    [Fact]
    public void Advance_LessThanOneStep_RunsNoStepsAndAccumulates()
    {
        var clock = new FixedStepClock(ticksPerSecond: 50);

        var steps = clock.Advance(Ms(10));

        Assert.Equal(0, steps);
        Assert.Equal(0, clock.Tick);
        Assert.Equal(0.5, clock.Alpha, precision: 9);
    }

    [Fact]
    public void Advance_CarriesRemainderBetweenFrames()
    {
        var clock = new FixedStepClock(ticksPerSecond: 50);

        clock.Advance(Ms(15));
        var steps = clock.Advance(Ms(15));

        Assert.Equal(1, steps);
        Assert.Equal(1, clock.Tick);
        Assert.Equal(0.5, clock.Alpha, precision: 9);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void OneSecondOfFrames_AtAnyRefreshRate_RunsSameNumberOfTicks(int fps)
    {
        var clock = new FixedStepClock(ticksPerSecond: 60);

        for (var i = 0; i < fps; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1.0 / fps));
        }

        Assert.InRange(clock.Tick, 59, 60);
    }

    [Fact]
    public void Advance_LongStall_IsCappedAndDropsTheBacklog()
    {
        var clock = new FixedStepClock(ticksPerSecond: 60, maxStepsPerFrame: 5);

        var steps = clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(5, steps);
        Assert.Equal(5, clock.Tick);
        Assert.Equal(0, clock.Alpha);
        Assert.Equal(0, clock.Advance(TimeSpan.Zero));
    }

    [Fact]
    public void Advance_NegativeElapsed_Throws()
    {
        var clock = new FixedStepClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(Ms(-1)));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-30, 5)]
    [InlineData(60, 0)]
    public void Constructor_InvalidArguments_Throws(double ticksPerSecond, int maxSteps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(ticksPerSecond, maxSteps));
    }
}
