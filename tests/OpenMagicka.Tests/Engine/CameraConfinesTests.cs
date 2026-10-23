using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;

namespace OpenMagicka.Tests.Engine;

public class CameraConfinesTests
{
    private static readonly Vector3 A = new(0, 0, 0);
    private static readonly Vector3 B = new(10, 0, 0);
    private static readonly Vector3 C = new(0, 0, 10);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(2, 5, 3, 2, 0, 3)]      // above the face
    [InlineData(5, -1, -4, 5, 0, 0)]    // beyond the edge AB
    [InlineData(8, 0, 8, 5, 0, 5)]      // beyond the edge BC
    [InlineData(-3, 2, -3, 0, 0, 0)]    // beyond the corner A
    [InlineData(15, 0, -1, 10, 0, 0)]   // beyond the corner B
    [InlineData(-1, 0, 14, 0, 0, 10)]   // beyond the corner C
    public void ClosestPointOnATriangle(float x, float y, float z, float ex, float ey, float ez) =>
        Near(new Vector3(ex, ey, ez), CameraMesh.ClosestPoint(new Vector3(x, y, z), A, B, C));

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 10)]     // a = b, p beyond c
    [InlineData(0, 0, 0, 10, 0, 0, 0, 0, 0)]     // a = c, p beyond b
    [InlineData(10, 0, 0, 0, 0, 10, 0, 0, 10)]   // b = c
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0)]      // a single point
    [InlineData(0, 0, 0, 5, 0, 5, 10, 0, 10)]    // on a line
    public void ADegenerateTriangle_GivesOneOfItsPoints(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
    {
        var (a, b, c) = (new Vector3(ax, ay, az), new Vector3(bx, by, bz), new Vector3(cx, cy, cz));
        foreach (var p in new[] { new Vector3(3, 1, 20), new Vector3(20, -1, 3), new Vector3(-4, 2, -4), new Vector3(5, 0, 5) })
        {
            var q = CameraMesh.ClosestPoint(p, a, b, c);
            Assert.True(float.IsFinite(q.X + q.Y + q.Z), $"{p} gave {q}");
            Assert.True(Vector3.Distance(p, q) <= new[] { a, b, c }.Min(v => Vector3.Distance(p, v)) + 1e-3f, $"{p} gave {q}, farther than a corner");
        }
    }

    [Fact]
    public void TheMesh_TakesTheClosestPointOfItsNearestTriangle()
    {
        var mesh = new CameraMesh(new CollisionMeshContent(
            [A, B, C, new Vector3(0, 10, 0), new Vector3(10, 10, 0), new Vector3(0, 10, 10)],
            [new CollisionTriangle(0, 1, 2), new CollisionTriangle(3, 4, 5)]));

        Near(new Vector3(2, 10, 2), mesh.ClosestPoint(new Vector3(2, 7, 2)));
        Near(new Vector3(2, 0, 2), mesh.ClosestPoint(new Vector3(2, 3, 2)));
    }

    [Fact]
    public void AnEmptyMesh_KeepsThePoint_AndBadIndicesAreRejected()
    {
        Near(new Vector3(1, 2, 3), new CameraMesh(new CollisionMeshContent([], [])).ClosestPoint(new Vector3(1, 2, 3)));
        Assert.Throws<InvalidDataException>(() => new CameraMesh(new CollisionMeshContent([A, B], [new CollisionTriangle(0, 1, 2)])));
    }

    [Fact]
    public void TheBox_ClampsInItsOwnFrame()
    {
        // Turned 90° about Y: its X side runs along −Z, its Z side along +X.
        var box = new CameraBox(Vector3.Zero, new Vector3(10, 1, 2), Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2));

        Near(new Vector3(0, 0, -10), box.ClosestPoint(new Vector3(0, 0, -20)));
        Near(new Vector3(2, 0.5f, -5), box.ClosestPoint(new Vector3(5, 0.5f, -5)));
        Near(new Vector3(1, 0.5f, -5), box.ClosestPoint(new Vector3(1, 0.5f, -5)));   // inside
    }

    [Fact]
    public void ATriggerArea_GivesItsBox()
    {
        var area = new TriggerAreaContent("lock", new Vector3(1, 2, 3), new Vector3(4, 5, 6), Quaternion.Identity);

        Assert.Equal(new CameraBox(new Vector3(1, 2, 3), new Vector3(4, 5, 6), Quaternion.Identity), CameraBox.From(area));
    }
}
