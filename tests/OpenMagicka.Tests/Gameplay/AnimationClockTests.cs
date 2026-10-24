using OpenMagicka.Gameplay.Animation;

namespace OpenMagicka.Tests.Gameplay;

public class AnimationClockTests
{
    private static readonly AnimationClip Walk = new("walk", 1);
    private static readonly AnimationClip Swing = new("swing", 0.5f);

    [Fact]
    public void AClip_RunsAtTheCharactersAndItsOwnSpeed_AndFinishesAtItsEnd()
    {
        var clock = new AnimationClock { Speed = 2, ClipSpeed = 0.5f };
        clock.Start(Swing, loop: false);

        clock.Update(0.25f);
        Assert.Equal(0.25f, clock.Time, 5);
        Assert.Equal(0.5f, clock.NormalizedTime, 5);
        clock.Update(1);

        Assert.Equal(0.5f, clock.Time);
        Assert.True(clock.HasFinished);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void ALoopingClip_WrapsAndSaysSo()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);

        Assert.Equal(ClockEvents.None, clock.Update(0.6f));
        Assert.Equal(ClockEvents.Looped, clock.Update(0.6f));
        Assert.Equal(0.2f, clock.Time, 4);
        Assert.False(clock.HasFinished);
    }

    [Fact]
    public void ACrossfade_KeepsTheOldClipPlaying_ThenStartsTheNewOneFromZero()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);
        clock.Update(0.3f);

        clock.CrossFade(Swing, 0.2f, loop: false);
        Assert.Equal(ClockEvents.None, clock.Update(0.1f));
        Assert.Same(Walk, clock.Clip);
        Assert.Equal(0.4f, clock.Time, 4);
        Assert.Same(Swing, clock.CrossFadeClip);

        Assert.Equal(ClockEvents.CrossfadeFinished, clock.Update(0.15f));
        Assert.Same(Swing, clock.Clip);
        Assert.Equal(0f, clock.Time);
        Assert.False(clock.IsCrossFading);
    }

    [Fact]
    public void AnEndedClipBeingFadedOut_IsNotFinished()
    {
        var clock = new AnimationClock();
        clock.Start(Swing, loop: false);
        clock.CrossFade(Walk, 1, loop: true);

        clock.Update(0.6f);

        Assert.False(clock.HasFinished);
        Assert.Equal(0.5f, clock.Time);                                          // held at its end while the blend runs
    }

    [Fact]
    public void ASecondCrossfade_IsQueued_AndHurriesTheFirst()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);
        clock.CrossFade(Swing, 1, loop: false);
        clock.Update(0.2f);

        clock.CrossFade(Walk, 0.3f, loop: true);

        Assert.Equal(0.2f + 0.8f / 2, clock.CrossFadeDuration, 4);
        Assert.Equal(ClockEvents.CrossfadeFinished, clock.Update(0.5f));
        Assert.Same(Swing, clock.Clip);
        Assert.Same(Walk, clock.CrossFadeClip);                                  // the queued one blends next
        Assert.Equal(ClockEvents.CrossfadeFinished, clock.Update(0.31f));
        Assert.Same(Walk, clock.Clip);
        Assert.True(clock.IsLooping);
    }

    [Fact]
    public void TheLastLoopFlag_IsTheOneTheRunningBlendStartsWith_AsTheOriginal()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);
        clock.CrossFade(Swing, 0.1f, loop: false);
        clock.CrossFade(Walk, 0.1f, loop: true);

        clock.Update(0.2f);

        Assert.Same(Swing, clock.Clip);
        Assert.True(clock.IsLooping);
    }

    [Fact]
    public void AHugeSpeed_WrapsAtOnce()
    {
        var clock = new AnimationClock { Speed = 1e10f };
        clock.Start(Walk, loop: true);

        clock.Update(0.016f);

        Assert.InRange(clock.Time, 0, Walk.Duration);
        Assert.Equal(1f, Wrapped(2), 5);                                         // an exact multiple stays at the end

        static float Wrapped(float time)
        {
            var c = new AnimationClock();
            c.Start(Walk, loop: true);
            c.Update(time);
            return c.Time;
        }
    }

    [Fact]
    public void ACleared_Queue_IsForgotten()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);
        clock.CrossFade(Swing, 0.1f, loop: false);
        clock.CrossFade(Walk, 0.1f, loop: true);

        clock.ClearQueue();
        clock.Update(0.2f);

        Assert.Same(Swing, clock.Clip);
        Assert.False(clock.IsCrossFading);
    }

    [Fact]
    public void AtSpeedZero_NothingMoves_NotEvenACrossfade()
    {
        var clock = new AnimationClock();
        clock.Start(Walk, loop: true);
        clock.CrossFade(Swing, 0, loop: false);
        clock.Speed = 0;

        Assert.Equal(ClockEvents.None, clock.Update(1));
        Assert.True(clock.IsCrossFading);
        clock.Speed = -3;
        Assert.Equal(0f, clock.Speed);
    }

    [Fact]
    public void BadClipsAndTimes_AreRejected()
    {
        var clock = new AnimationClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Start(new AnimationClip("none", 0), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Update(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Speed = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Speed = float.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.ClipSpeed = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.CrossFade(Walk, -1, false));
        Assert.Equal(ClockEvents.None, clock.Update(1));                         // nothing to play yet
    }
}
