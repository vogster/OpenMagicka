using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Tests.Engine;

public class ViewerProjectionTests
{
    [Theory]
    [InlineData(3f, 0.15f)]     // a character: close-ups stay possible
    [InlineData(150f, 7.5f)]    // a castle
    [InlineData(600f, 10f)]     // a large outdoor level: capped
    [InlineData(0f, 0.1f)]
    public void TheOriginalPipeline_UsesTheOriginalFarPlane_AndANearPlaneThatGrowsWithTheScene(float size, float near)
    {
        Assert.Equal((near, 500f), ViewerProjection.ClipPlanes(size, originalPipeline: true));
    }

    [Fact]
    public void TheSimpleView_ReachesTheWholeLevel()
    {
        Assert.Equal((0.5f, 1000f), ViewerProjection.ClipPlanes(100, originalPipeline: false));
        Assert.Equal(6000f, ViewerProjection.ClipPlanes(2000, originalPipeline: false).Far);
    }
}
