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

public class LightShadowViewTests
{
    private static Vector3 Project(Vector3 point, Matrix viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]    // the faces in cube order: +X, -X, +Y, -Y, +Z, -Z
    [InlineData(1, -1, 0, 0)]
    [InlineData(2, 0, 1, 0)]
    [InlineData(3, 0, -1, 0)]
    [InlineData(4, 0, 0, 1)]
    [InlineData(5, 0, 0, -1)]
    public void EachCubeFace_LooksOutOfTheLightAlongItsAxis(int face, float x, float y, float z)
    {
        var light = new Vector3(4, 2, -3);
        var faces = LightShadowViews.PointFaces(light, 8);

        var ahead = Project(light + new Vector3(x, y, z) * 4, faces[face]);

        Assert.Equal(6, faces.Count);
        Assert.Equal(0, ahead.X, 3);
        Assert.Equal(0, ahead.Y, 3);
        Assert.InRange(ahead.Z, 0, 1);
    }

    /// <summary>The Direct3D cube convention: each face's texture right (u) and up directions.</summary>
    [Theory]
    [InlineData(0, 1, 0, 0, 0, 0, -1, 0, 1, 0)]
    [InlineData(1, -1, 0, 0, 0, 0, 1, 0, 1, 0)]
    [InlineData(2, 0, 1, 0, 1, 0, 0, 0, 0, -1)]
    [InlineData(3, 0, -1, 0, 1, 0, 0, 0, 0, 1)]
    [InlineData(4, 0, 0, 1, 1, 0, 0, 0, 1, 0)]
    [InlineData(5, 0, 0, -1, -1, 0, 0, 0, 1, 0)]
    public void EachCubeFace_IsOrientedAsTheCubeIsSampled(int face, float ax, float ay, float az, float ux, float uy, float uz, float vx, float vy, float vz)
    {
        var light = new Vector3(4, 2, -3);
        var view = LightShadowViews.PointFaces(light, 8)[face];
        var ahead = light + new Vector3(ax, ay, az) * 4;

        Assert.True(Project(ahead + new Vector3(ux, uy, uz) * 0.5f, view).X > 0.05f, "u");
        Assert.True(Project(ahead + new Vector3(vx, vy, vz) * 0.5f, view).Y > 0.05f, "up");
    }

    [Fact]
    public void ACubeFace_MirrorsX_AsTheOriginalDoes()
    {
        var faces = LightShadowViews.PointFaces(Vector3.Zero, 1);

        // Looking along +Z (face 4) with up +Y, a point to the +X side lands on the face's +X half once mirrored.
        Assert.True(Project(new Vector3(0.3f, 0, 0.5f), faces[4]).X > 0);
    }

    [Fact]
    public void ASpotLightsShadowView_LooksDownItsCone_UpToItsRange()
    {
        var spot = new SpotLighting(new Vector3(1, 10, 1), Vector3.Down, 20, 0.5f, 1, true, Vector3.One, Vector3.Zero, 0, 512);

        var view = LightShadowViews.Spot(spot);

        var centre = Project(new Vector3(1, 0, 1), view);
        Assert.Equal((0f, 0f), (MathF.Round(centre.X, 3), MathF.Round(centre.Y, 3)));
        Assert.InRange(centre.Z, 0, 1);
        Assert.True(Project(new Vector3(1, -15, 1), view).Z > 1, "beyond the range");
        Assert.True(MathF.Abs(Project(new Vector3(1 + 10 * MathF.Tan(0.6f), 0, 1), view).X) > 1, "outside the cone");
    }
}

public class LightShadowSizeTests
{
    private static LevelLightContent Make(LevelLightType type, bool shadows, int size) => new(
        "lamp", Vector3.Zero, Vector3.Down, type, LightVariationType.None, 6, true, 0.5f, 1, Vector3.One, Vector3.Zero, 0, 0, 0, size, shadows);

    [Fact]
    public void PointAndSpotLights_CastShadows_WhenTheLevelSaysSo()
    {
        Assert.Equal([512, 0, 0], SceneLighting.PointLights([Make(LevelLightType.Point, true, 512), Make(LevelLightType.Point, false, 512),
            Make(LevelLightType.Point, true, 0)]).Select(l => l.ShadowMapSize));
        Assert.Equal([256, 0], SceneLighting.SpotLights([Make(LevelLightType.Spot, true, 256), Make(LevelLightType.Spot, false, 256)])
            .Select(l => l.ShadowMapSize));
        Assert.Equal([1024], SceneLighting.PointLights([Make(LevelLightType.Point, true, 4096)]).Select(l => l.ShadowMapSize));
        Assert.Equal([0], SceneLighting.SpotLights([Make(LevelLightType.Spot, true, 256) with { Range = 0.01f }]).Select(l => l.ShadowMapSize));
    }
}
