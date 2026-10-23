using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Tests.Gameplay;

public class ImpactDamageTests
{
    [Fact]
    public void TheDamageGrowsWithThe2Point7thPowerOfTheSpeed()
    {
        Assert.Equal(100f, ImpactDamage.Amount(10), 3);
        Assert.Equal(100 * MathF.Pow(2, 2.7f), ImpactDamage.Amount(20), 2);
        Assert.Equal(0f, ImpactDamage.Amount(0));
        Assert.Equal(0f, ImpactDamage.Amount(-5));
        Assert.Equal(0f, ImpactDamage.Amount(float.NaN));
    }

    [Theory]
    [InlineData(7.5f, null, false)]          // 46: too soft
    [InlineData(8f, 54.8f, false)]
    [InlineData(10.5f, 114.1f, true)]
    public void SoftImpactsDoNotHurt_HardOnesKnockDown(float speed, float? amount, bool knocksDown)
    {
        var damage = ImpactDamage.For(speed);

        Assert.Equal(amount is null, damage is null);
        if (damage is { } d)
        {
            Assert.Equal(amount!.Value, d.Amount, 0);
            Assert.Equal(knocksDown, d.KnocksDown);
        }
    }

    [Fact]
    public void FallingAboutOneAndAHalfMetres_IsTheThreshold()
    {
        static float Speed(float height) => MathF.Sqrt(2 * 2 * 9.82f * height);

        Assert.Null(ImpactDamage.For(Speed(1.4f)));
        Assert.NotNull(ImpactDamage.For(Speed(1.6f)));
    }
}
