using OpenMagicka.Content.Particles;

namespace OpenMagicka.Tests.Particles;

public class ParameterCurveTests
{
    private static ParameterCurve Curve(params (float Time, float Value)[] keys) =>
        new(keys.Select(k => new Keyframe(k.Time, k.Value)).ToArray());

    [Theory]
    [InlineData(-1f, 10f)]     // before the first key: the first value
    [InlineData(0f, 10f)]
    [InlineData(0.5f, 15f)]    // blended linearly
    [InlineData(1f, 20f)]
    [InlineData(1.5f, 25f)]
    [InlineData(5f, 30f)]      // after the last key: the last value
    public void Evaluate_BlendsBetweenKeys_AndHoldsTheEdges(float time, float expected)
    {
        Assert.Equal(expected, Curve((0, 10), (1, 20), (2, 30)).Evaluate(time), 4);
    }

    [Fact]
    public void ASingleKey_IsAConstantWhateverItsTime()
    {
        Assert.Equal(7f, Curve((3, 7)).Evaluate(0));
        Assert.Equal(7f, ParameterCurve.Constant(7).Evaluate(100));
    }

    [Fact]
    public void KeysOutOfOrder_AreScannedInFileOrderLikeTheOriginal()
    {
        // Blends the last key at or before t that precedes the first key after t.
        var curve = Curve((0, 0), (2, 20), (1, 10));

        Assert.Equal(5f, curve.Evaluate(0.5f), 4);   // (0,0)..(2,20)
        Assert.Equal(15f, curve.Evaluate(1.5f), 4);  // (0,0)..(2,20) again: (1,10) comes after (2,20)
        Assert.Equal(10f, curve.Evaluate(3f));       // no later key: the last key at or before t in file order
    }

    [Fact]
    public void TheScanStopsAtTheFirstLaterKey_SoAnOutOfOrderKeyAfterItIsNotUsed()
    {
        // At t=7 the scan stops at (10,100): (5,0) after it is never seen, so (0,0)..(10,100) is used.
        Assert.Equal(70f, Curve((0, 0), (10, 100), (5, 0)).Evaluate(7), 4);
    }

    [Fact]
    public void Keys_AreCopied()
    {
        var keys = new[] { new Keyframe(0, 1) };
        var curve = new ParameterCurve(keys);
        keys[0] = new Keyframe(0, 5);

        Assert.Equal(1f, curve.Evaluate(0));
        Assert.False(curve.Keys is Keyframe[]);
    }

    [Fact]
    public void ACurve_NeedsAKey()
    {
        Assert.Throws<ArgumentException>(() => new ParameterCurve([]));
    }
}
