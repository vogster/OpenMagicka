using OpenMagicka.Game;

namespace OpenMagicka.Tests.Engine;

public class EffectWarmupTests
{
    [Fact]
    public void AnEffectTheGameNeverDraws_FailingItsTestDraw_IsSkipped()
    {
        Assert.Equal((true, "ChasmEffect (the game never draws it: no content uses it): X"), EffectWarmup.Outcome("ChasmEffect", "X"));
        Assert.Equal((false, "AdditiveEffect: X"), EffectWarmup.Outcome("AdditiveEffect", "X"));
    }
}
