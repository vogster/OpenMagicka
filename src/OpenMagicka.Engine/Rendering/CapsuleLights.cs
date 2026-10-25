using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// A light along a segment (a beam's, a bolt's): it lights what lies within its radius of the segment, fading
/// linearly, its colours already times its intensity; it casts no shadow and has no shine.
/// </summary>
public readonly record struct CapsuleLighting(Vector3 Start, Vector3 End, float Radius, Vector3 Diffuse, Vector3 Ambient)
{
    /// <summary>
    /// Where the unit capsule is put: its middle halfway along, its −Z toward the end (up is the world's, or its
    /// back for an upright segment, as the original).
    /// </summary>
    public Matrix World
    {
        get
        {
            var middle = (Start + End) / 2;
            var along = End - Start;
            if (along.LengthSquared() < 1e-12f)
            {
                return Matrix.CreateTranslation(middle);
            }
            var up = Vector3.Cross(Vector3.Normalize(along), Vector3.Up).LengthSquared() < 1e-8f ? Vector3.Backward : Vector3.Up;
            return Matrix.Invert(Matrix.CreateLookAt(middle, End, up));
        }
    }
}

/// <summary>
/// The capsule lights of the deferred light pass (docs/engine/ARCHITECTURE.md §17): the original's
/// <c>CapsuleLight</c> effect over its unit capsule (<c>models/effects/Capsule</c>), which its vertex shader
/// stretches to the segment and swells to the radius; added like the point lights, from the G-buffer. Render
/// thread only.
/// </summary>
public sealed class CapsuleLights : IDisposable
{
    public const string Mesh = "models/effects/Capsule";

    private static readonly VertexDeclaration Layout = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    private readonly GameEffect effect;
    private readonly VertexBuffer vertices;
    private readonly IndexBuffer indices;
    private readonly int vertexCount;
    private readonly int primitives;

    private CapsuleLights(GameEffect effect, VertexBuffer vertices, IndexBuffer indices, int vertexCount, int primitives) =>
        (this.effect, this.vertices, this.indices, this.vertexCount, this.primitives) = (effect, vertices, indices, vertexCount, primitives);

    public GameEffect Effect => effect;

    /// <summary>The capsule's vertices (position, normal, texture coordinates: 32 bytes each) and 16-bit triangles, from the model.</summary>
    public static (byte[] Vertices, int Count, byte[] Indices, int Primitives) Capsule(ModelContent model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Meshes is not [{ VertexBuffer: { } buffer, IndexBuffer: { Is16Bit: true } index, Parts: [var part, ..] }, ..]
            || model.VertexDeclarations[part.VertexDeclarationIndex].Elements.Sum(e => e.Format is VertexElementFormatXna31.Vector3 ? 12 : 8) != 32
            || buffer.Data.Length < part.StreamOffset + (part.BaseVertex + part.NumVertices) * 32)
        {
            throw new InvalidDataException($"{Mesh} is not a capsule of positions, normals and texture coordinates");
        }
        var start = part.StreamOffset + part.BaseVertex * 32;
        var count = part.PrimitiveCount * 3;
        if (index.Data.Length < (part.StartIndex + count) * 2)
        {
            throw new InvalidDataException($"{Mesh} has too few indices");
        }
        return (buffer.Data[start..(start + part.NumVertices * 32)], part.NumVertices, index.Data[(part.StartIndex * 2)..((part.StartIndex + count) * 2)],
            part.PrimitiveCount);
    }

    /// <summary>Builds the lights over the capsule (render thread); the effect is the caller's until this succeeds.</summary>
    public static CapsuleLights Create(GraphicsDevice device, GameEffect effect, ModelContent capsule)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(effect);
        EffectChecks.Require(effect, ["Length", "World", "Start", "End", "Radius", "DiffuseColor", "AmbientColor", "NormalMap", "DepthMap", "HalfPixel"]);
        var (data, count, triangles, primitives) = Capsule(capsule);
        var vertices = new VertexBuffer(device, Layout, count, BufferUsage.WriteOnly);
        IndexBuffer? indices = null;
        try
        {
            vertices.SetData(data);
            indices = new IndexBuffer(device, IndexElementSize.SixteenBits, triangles.Length / 2, BufferUsage.WriteOnly);
            indices.SetData(triangles);
            return new CapsuleLights(effect, vertices, indices, count, primitives);
        }
        catch
        {
            vertices.Dispose();
            indices?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds each light whose reach is in view into the bound light target (One / InvSrcColor, no depth test), its
    /// outer side, or its inner one when the camera is near it.
    /// </summary>
    public void Draw(GraphicsDevice device, IReadOnlyList<CapsuleLighting> lights, BoundingFrustum view, Vector3 camera, Texture2D normal, Texture2D depth)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(lights);
        if (lights.Count == 0)
        {
            return;
        }
        device.BlendState = LightPass.LightBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.SetVertexBuffer(vertices);
        device.Indices = indices;
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("NormalMap", normal);
        parameters.SetValue("DepthMap", depth);
        parameters.SetValue("HalfPixel", Vector2.Zero);                         // as the other lights (LightPass)
        effect.Effect.CurrentTechnique = effect.Effect.Techniques[0];
        foreach (var lamp in lights)
        {
            var length = Vector3.Distance(lamp.Start, lamp.End);
            var middle = (lamp.Start + lamp.End) / 2;
            if (!(lamp.Radius > 0) || !float.IsFinite(length) || !view.Intersects(new BoundingSphere(middle, length + lamp.Radius)))
            {
                continue;
            }
            parameters.SetValue("Length", length);
            parameters.SetValue("World", lamp.World);
            parameters.SetValue("Start", lamp.Start);
            parameters.SetValue("End", lamp.End);
            parameters.SetValue("Radius", lamp.Radius);
            parameters.SetValue("DiffuseColor", lamp.Diffuse);
            parameters.SetValue("AmbientColor", lamp.Ambient);
            foreach (var pass in effect.Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.RasterizerState = LightPass.Cull(camera, middle, length + lamp.Radius);
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertexCount, 0, primitives);
            }
        }
        device.RasterizerState = RasterizerState.CullNone;
    }

    public void Dispose()
    {
        vertices.Dispose();
        indices.Dispose();
        effect.Dispose();
    }
}
