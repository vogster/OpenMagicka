using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using Light = OpenMagicka.Content.Magicka.LevelLightContent;

namespace OpenMagicka.Tests.Engine;

public class SpotLightTests
{
    private static (int, int)[] DirectedEdges(ushort[] indices) => Enumerable.Range(0, indices.Length / 3)
        .SelectMany(t => new[] { (indices[t * 3], indices[t * 3 + 1]), (indices[t * 3 + 1], indices[t * 3 + 2]), (indices[t * 3 + 2], indices[t * 3]) })
        .Select(e => ((int)e.Item1, (int)e.Item2))
        .ToArray();

    [Fact]
    public void TheCone_IsAClosedMeshOf16SidesAnd4Rings()
    {
        var (vertices, indices) = LightVolumes.Cone();

        Assert.Equal((82, 160 * 3), (vertices.Length, indices.Length));
        var edges = DirectedEdges(indices);
        Assert.Equal(edges.Length, edges.Distinct().Count());
        Assert.All(edges, e => Assert.Contains((e.Item2, e.Item1), edges));
    }

    [Fact]
    public void NoConeVertex_LiesOnTheAxis_WhereTheShaderWouldDivideZeroByZero()
    {
        var (vertices, _) = LightVolumes.Cone();

        Assert.All(vertices, v => Assert.True(v.X * v.X + v.Y * v.Y > 0, $"{v}"));
    }

    // SpotShape restates the shader's bend as read from its bytecode; these tests pin the mesh against that reading.
    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.8f)]
    [InlineData(1.4f)]
    public void Shaped_TheConeCoversTheLightsReach_AndIsSeenFromOutside(float cutoff)
    {
        var (unit, indices) = LightVolumes.Cone();
        var shaped = unit.Select(v => LightVolumes.SpotShape(v, cutoff)).ToArray();

        // Apex at the light, the cap on the unit sphere within the cutoff angle around -Z.
        Assert.Equal(0, shaped[0].Length(), 4);
        Assert.All(shaped.Skip(1), v => Assert.Equal(1, v.Length(), 4));
        Assert.All(shaped.Skip(1), v => Assert.True(MathF.Acos(Math.Clamp(-v.Z, -1, 1)) <= cutoff + 1e-4f, $"{v}"));
        var inside = new Vector3(0, 0, -0.5f);
        for (var t = 0; t < indices.Length; t += 3)
        {
            var (a, b, c) = (shaped[indices[t]], shaped[indices[t + 1]], shaped[indices[t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() > 1e-12f)
            {
                Assert.True(Vector3.Dot(normal, (a + b + c) / 3 - inside) < 0, $"triangle {t / 3}");
            }
        }
    }

    private static Light Make(LevelLightType type, Vector3 direction, float range, float cutoff, bool attenuation = true) => new(
        "lamp", new Vector3(1, 2, 3), direction, type, LightVariationType.None, range, attenuation, cutoff, 2, new Vector3(1, 0.5f, 0.25f),
        new Vector3(0.1f), 0.5f, 0, 0, 512, false);

    [Fact]
    public void SpotLights_AreTheLevelsSpotLightsWithAReachAndAnAngle()
    {
        var lights = SceneLighting.SpotLights([
            Make(LevelLightType.Spot, new Vector3(0, -2, 0), 8, 1.0f),
            Make(LevelLightType.Point, Vector3.Down, 8, 1.0f),
            Make(LevelLightType.Spot, Vector3.Down, 0, 1.0f),
            Make(LevelLightType.Spot, Vector3.Down, float.PositiveInfinity, 1.0f),
            Make(LevelLightType.Spot, Vector3.Zero, 8, 1.0f),
            Make(LevelLightType.Spot, Vector3.Down, 8, 0),
        ]);

        var light = Assert.Single(lights);
        Assert.Equal(new SpotLighting(new Vector3(1, 2, 3), Vector3.Down, 8, 1.0f, 2, true, new Vector3(1, 0.5f, 0.25f), new Vector3(0.1f), 0.5f), light);
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(0.6f, -0.3f, 0.74f)]
    [InlineData(1, 0, 0)]
    public void ASpotLightsWorld_PointsTheConesAxisAlongTheLight(float x, float y, float z)
    {
        var direction = Vector3.Normalize(new Vector3(x, y, z));
        var light = new SpotLighting(new Vector3(1, 2, 3), direction, 8, 1, 2, true, Vector3.One, Vector3.Zero, 0);

        var tip = Vector3.Transform(new Vector3(0, 0, -1), light.World);

        Assert.Equal(new Vector3(1, 2, 3), light.World.Translation);
        Assert.Equal(1, light.World.Determinant(), 4);   // a rotation: no scale, no mirror (which would flip the culled side)
        Assert.True(Vector3.Distance(new Vector3(1, 2, 3) + direction, tip) < 1e-4f, $"{tip}");
    }
}
