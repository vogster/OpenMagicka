using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using Light = OpenMagicka.Content.Magicka.LevelLightContent;

namespace OpenMagicka.Tests.Engine;

public class LightVolumeTests
{
    [Fact]
    public void TheSphere_IsAClosedUnitMeshOf80Triangles()
    {
        var (vertices, indices) = LightVolumes.Sphere();

        Assert.Equal((42, 240), (vertices.Length, indices.Length));
        Assert.All(vertices, v => Assert.Equal(1, v.Length(), 5));
        var edges = Enumerable.Range(0, indices.Length / 3)
            .SelectMany(t => new[] { (indices[t * 3], indices[t * 3 + 1]), (indices[t * 3 + 1], indices[t * 3 + 2]), (indices[t * 3 + 2], indices[t * 3]) })
            .ToArray();
        // Closed and consistently wound: every directed edge appears once and its reverse once.
        Assert.Equal(edges.Length, edges.Distinct().Count());
        Assert.All(edges, e => Assert.Contains((e.Item2, e.Item1), edges));
    }

    [Fact]
    public void TheSphere_IsWoundToBeSeenFromOutside()
    {
        var (vertices, indices) = LightVolumes.Sphere();

        for (var t = 0; t < indices.Length; t += 3)
        {
            var (a, b, c) = (vertices[indices[t]], vertices[indices[t + 1]], vertices[indices[t + 2]]);
            // Clockwise from outside under the default culling: the right-handed normal points inwards.
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0, $"triangle {t / 3}");
        }
    }

    private static Light Make(LevelLightType type, float range) => new(
        "torch", new Vector3(1, 2, 3), Vector3.Down, type, LightVariationType.None, range, false, 0, 0, new Vector3(1, 0.5f, 0.25f),
        new Vector3(0.1f), 0.5f, 0, 0, 512, false);

    [Fact]
    public void PointLights_AreTheLevelsPointLightsWithARange()
    {
        var lights = SceneLighting.PointLights([Make(LevelLightType.Point, 6), Make(LevelLightType.Spot, 6), Make(LevelLightType.Point, 0),
            Make(LevelLightType.Point, float.PositiveInfinity), Make(LevelLightType.Point, float.NaN)]);

        var light = Assert.Single(lights);
        Assert.Equal(new PointLighting(new Vector3(1, 2, 3), 6, new Vector3(1, 0.5f, 0.25f), new Vector3(0.1f), 0.5f), light);
        Assert.Equal(Matrix.CreateScale(6) * Matrix.CreateTranslation(1, 2, 3), light.World);
    }
}
