using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The meshes the light passes draw to cover a light's reach on screen.</summary>
public static class LightVolumes
{
    /// <summary>
    /// A unit sphere for point lights (scaled by the radius): an icosahedron subdivided once, 42
    /// vertices on the sphere and 80 triangles, as in the original. Triangles are clockwise seen
    /// from outside, so the default culling draws the outer side.
    /// </summary>
    public static (Vector3[] Vertices, ushort[] Indices) Sphere()
    {
        var t = (1 + MathF.Sqrt(5)) / 2;
        var vertices = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        vertices = vertices.Select(Vector3.Normalize).ToList();
        int[] faces =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];
        var midpoints = new Dictionary<(int, int), int>();
        int Midpoint(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!midpoints.TryGetValue(key, out var index))
            {
                index = vertices.Count;
                vertices.Add(Vector3.Normalize(vertices[a] + vertices[b]));
                midpoints[key] = index;
            }
            return index;
        }

        var indices = new List<ushort>();
        void Triangle(int a, int b, int c)
        {
            var inwards = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), vertices[a] + vertices[b] + vertices[c]) < 0;
            indices.AddRange(inwards ? [(ushort)a, (ushort)b, (ushort)c] : [(ushort)a, (ushort)c, (ushort)b]);
        }

        for (var f = 0; f < faces.Length; f += 3)
        {
            var (a, b, c) = (faces[f], faces[f + 1], faces[f + 2]);
            var (ab, bc, ca) = (Midpoint(a, b), Midpoint(b, c), Midpoint(c, a));
            Triangle(a, ab, ca);
            Triangle(b, bc, ab);
            Triangle(c, ca, bc);
            Triangle(ab, bc, ca);
        }
        return ([.. vertices], [.. indices]);
    }
}

/// <summary>The light sphere on the GPU (positions only). Render thread only.</summary>
internal sealed class SphereMesh : IDisposable
{
    private static readonly VertexDeclaration Positions = new(new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0));
    private readonly VertexBuffer vertices;
    private readonly IndexBuffer indices;
    private readonly int primitives;

    public SphereMesh(GraphicsDevice device)
    {
        var (points, triangles) = LightVolumes.Sphere();
        vertices = new VertexBuffer(device, Positions, points.Length, BufferUsage.WriteOnly);
        IndexBuffer? created = null;
        try
        {
            vertices.SetData(points);
            created = new IndexBuffer(device, IndexElementSize.SixteenBits, triangles.Length, BufferUsage.WriteOnly);
            created.SetData(triangles);
        }
        catch
        {
            vertices.Dispose();
            created?.Dispose();
            throw;
        }
        indices = created;
        primitives = triangles.Length / 3;
    }

    /// <summary>Binds the sphere; then <see cref="Draw"/> once per light.</summary>
    public void Bind(GraphicsDevice device)
    {
        device.SetVertexBuffer(vertices);
        device.Indices = indices;
    }

    /// <summary>Draws with <paramref name="cull"/>, set after the pass, since applying a pass sets the effect's own cull mode.</summary>
    public void Draw(GraphicsDevice device, Effect effect, RasterizerState cull)
    {
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.RasterizerState = cull;
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertices.VertexCount, 0, primitives);
        }
    }

    public void Dispose()
    {
        vertices.Dispose();
        indices.Dispose();
    }
}
