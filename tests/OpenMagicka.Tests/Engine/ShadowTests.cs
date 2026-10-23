using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using Light = OpenMagicka.Content.Magicka.LevelLightContent;

namespace OpenMagicka.Tests.Engine;

public class ShadowTests
{
    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.4f, -1, -0.3f));

    [Fact]
    public void TheSunsShadowView_CentresOnTheFocus_SnappedToItsTexels()
    {
        var focus = new Vector3(12.34f, 1.5f, -7.89f);

        var view = SunShadowView.Around(SunDirection, focus, 1024);

        var centre = Vector3.Transform(focus, view.LightViewProjection);
        var texel = 2f / 1024;   // one texel in clip space (60 units over 1024 texels)
        Assert.True(MathF.Abs(centre.X) <= texel && MathF.Abs(centre.Y) <= texel, $"{centre}");
        Assert.True(Vector3.Distance(focus, view.SnappedGround) <= 60f / 1024 * MathF.Sqrt(3), $"{view.SnappedGround}");
        // Snapped: the world's origin falls on the map's texel grid, wherever the focus is.
        var origin = Vector3.Transform(Vector3.Zero, view.LightViewProjection) * 1024 / 2;
        Assert.Equal(MathF.Round(origin.X), origin.X, 2);
        Assert.Equal(MathF.Round(origin.Y), origin.Y, 2);
    }

    [Fact]
    public void TheSunsShadowView_Covers30UnitsAroundTheFocus_AndReachesUpAndDownTheLight()
    {
        var view = SunShadowView.Around(SunDirection, Vector3.Zero, 1024);
        var side = Vector3.Normalize(Vector3.Cross(SunDirection, Vector3.Up));

        var edge = Vector3.Transform(side * 29.9f, view.LightViewProjection);
        var outside = Vector3.Transform(side * 30.5f, view.LightViewProjection);
        var above = Vector3.Transform(-SunDirection * 190, view.LightViewProjection);   // 200 units towards the light
        var below = Vector3.Transform(SunDirection * 90, view.LightViewProjection);     // 100 units beyond the focus

        Assert.True(MathF.Max(MathF.Abs(edge.X), MathF.Abs(edge.Y)) < 1);
        Assert.True(MathF.Max(MathF.Abs(outside.X), MathF.Abs(outside.Y)) > 1);
        Assert.InRange(above.Z, 0, 1);
        Assert.InRange(below.Z, 0, 1);
        Assert.True(above.Z < below.Z, "nearer the light is nearer in depth");
    }

    [Fact]
    public void ASunStraightDown_StillHasAView()
    {
        var view = SunShadowView.Around(Vector3.Down, new Vector3(5, 0, 5), 512);

        Assert.All(new[] { view.LightViewProjection.M11, view.LightViewProjection.M22, view.LightViewProjection.M33 }, v => Assert.True(float.IsFinite(v)));
        Assert.Equal(0, Vector3.Transform(new Vector3(5, 0, 5), view.LightViewProjection).X, 2);
    }

    private static Light Sun(bool shadows, int size) => new(
        "sun", Vector3.Zero, Vector3.Down, LevelLightType.Directional, LightVariationType.None, 0, false, 0, 0, Vector3.One,
        new Vector3(0.1f), 0.5f, 0, 0, size, shadows);

    [Theory]
    [InlineData(true, 1024, 1024)]
    [InlineData(true, 2048, 2048)]
    [InlineData(false, 1024, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 100000, 4096)]
    public void TheSunCastsShadows_WhenTheLevelSaysSo_AtItsMapSize(bool shadows, int size, int expected)
    {
        Assert.Equal(expected, SceneLighting.Sun([Sun(shadows, size)]).ShadowMapSize);
    }

    [Theory]
    [InlineData(-1f, 50f)]       // looking straight down from 50 units up: the ground below
    [InlineData(0.2f, 224f)]     // looking up: the original's focal distance
    public void TheShadowFocus_IsWhereTheViewMeetsTheGround(float forwardY, float distance)
    {
        var camera = new Vector3(3, 50, 4);
        var forward = Vector3.Normalize(new Vector3(0, forwardY, forwardY < 0 ? 0 : 1));

        var focus = SunShadowView.Focus(camera, forward, groundHeight: 0);

        Assert.Equal(distance, Vector3.Distance(camera, focus), 2);
    }
}
