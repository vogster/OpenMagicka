using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Cameras;
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

    [Fact]
    public void TheGameCamera_IsInterpolatedBetweenTicks()
    {
        var before = GameCamera.Initial;
        var after = GameCamera.Initial with { Eye = GameCamera.Offset + new Vector3(10, 0, 0), Magnification = 2 };

        var (view, projection, eye) = ViewerProjection.Game(before, after, 0.5f, 16 / 9f);
        var (_, last, _) = ViewerProjection.Game(after, after, 0.5f, 16 / 9f);

        Assert.Equal(GameCamera.Offset + new Vector3(5, 0, 0), eye);
        Assert.Equal(Matrix.CreateLookAt(eye, eye + GameCamera.Direction, Vector3.Up), view);
        Assert.Equal(Matrix.CreatePerspectiveFieldOfView((before.Fov + after.Fov) / 2, 16 / 9f, 105, 500), projection);
        Assert.Equal(after.Projection(16 / 9f), last);
    }
}
