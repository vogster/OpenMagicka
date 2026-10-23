using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// Batches of a level's animated parts (doors, lifts, machinery) at their rest pose, as the
/// original places them when a level starts: a part's world transform is the pose of its
/// animation's first keyframe times its parent part's world transform, and every mesh of the part's
/// model is drawn with it (the model's bone transforms are not used). A mesh's visibility comes
/// from the part's mesh settings, which must name every mesh. Liquids on parts are not drawn yet.
/// A part whose rest pose is not finite (six energy rings of ch_end_of_world start with an all-NaN
/// keyframe) is skipped with its children. docs/formats/XNB_READERS.md §4.3.
/// </summary>
public static class LevelParts
{
    /// <param name="shared">The level file's shared resources, where the parts' mesh effects are.</param>
    public static IReadOnlyList<LevelBatch> Batches(IReadOnlyList<AnimatedLevelPartContent> parts, IReadOnlyList<object?> shared)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(shared);
        var batches = new List<LevelBatch>();
        foreach (var part in parts)
        {
            Add(part, Matrix.Identity, shared, batches);
        }
        return batches;
    }

    /// <summary>XNAnimation's pose matrix: scale, then rotation, then translation.</summary>
    public static Matrix PoseMatrix(BonePose pose) =>
        Matrix.CreateScale(pose.Scale) * Matrix.CreateFromQuaternion(pose.Orientation) * Matrix.CreateTranslation(pose.Translation);

    private static void Add(AnimatedLevelPartContent part, Matrix parent, IReadOnlyList<object?> shared, List<LevelBatch> batches)
    {
        var world = (part.Animation.Count > 0 ? PoseMatrix(part.Animation[0].Pose) : Matrix.Identity) * parent;
        if (!IsFinite(world))
        {
            return;
        }
        if (part.Model is { } model)
        {
            foreach (var mesh in model.Meshes)
            {
                var setting = part.MeshSettings.FirstOrDefault(s => s.MeshName == mesh.Name)
                    ?? throw new InvalidDataException($"animated part {part.Name}: no mesh settings for mesh {mesh.Name}");
                if (setting.Visible)
                {
                    AddMesh(model, mesh, world, shared, batches);
                }
            }
        }
        foreach (var child in part.Children)
        {
            Add(child, world, shared, batches);
        }
    }

    private static void AddMesh(ModelContent model, ModelMeshContent mesh, Matrix world, IReadOnlyList<object?> shared, List<LevelBatch> batches)
    {
        if (mesh.VertexBuffer is null || mesh.IndexBuffer is null)
        {
            return;
        }
        var center = Vector3.Transform(mesh.Bounds.Center, world);
        // The Frobenius norm bounds the largest stretch of any linear part, also a skewed one.
        var scale = MathF.Sqrt(world.Right.LengthSquared() + world.Up.LengthSquared() + world.Backward.LengthSquared());
        var bounds = BoundingBox.CreateFromSphere(new BoundingSphere(center, mesh.Bounds.Radius * scale));
        foreach (var part in mesh.Parts)
        {
            if (part.PrimitiveCount <= 0)
            {
                continue;
            }
            if (part.NumVertices <= 0)
            {
                throw new InvalidDataException($"mesh {mesh.Name}: {part.PrimitiveCount} triangles over no vertices");
            }
            if (part.VertexDeclarationIndex < 0 || part.VertexDeclarationIndex >= model.VertexDeclarations.Count)
            {
                throw new InvalidDataException($"mesh {mesh.Name}: vertex declaration {part.VertexDeclarationIndex} of {model.VertexDeclarations.Count}");
            }
            var effect = part.Effect is { } reference
                ? (uint)reference.Index < (uint)shared.Count ? shared[reference.Index] : throw new InvalidDataException($"mesh {mesh.Name}: effect {reference.Index} of {shared.Count} shared resources")
                : null;
            if (effect is RenderDeferredLiquidEffectContent)
            {
                continue;   // water on a part: drawn with the liquids (OM-25)
            }
            var declaration = model.VertexDeclarations[part.VertexDeclarationIndex];
            var (texture, color, additive) = effect switch
            {
                RenderDeferredEffectContent m => (m.Layer0.DiffuseTexture, m.Layer0.DiffuseColor, false),
                AdditiveEffectContent a => (a.TextureEnabled ? a.Texture : null, a.ColorTint, true),
                _ => ((ExternalReference?)null, Vector3.One, false),
            };
            var basic = VertexLayouts.Basic(declaration);
            var indices = Slice(mesh.IndexBuffer.Data, (long)part.StartIndex * IndexSize(mesh.IndexBuffer), part.PrimitiveCount * 3L * IndexSize(mesh.IndexBuffer), mesh.Name);
            CheckIndices(indices, mesh.IndexBuffer.Is16Bit, part.NumVertices, mesh.Name);
            batches.Add(new LevelBatch(
                Slice(mesh.VertexBuffer.Data, part.StreamOffset + (long)part.BaseVertex * declaration.Stride, (long)part.NumVertices * declaration.Stride, mesh.Name),
                declaration.Stride, part.NumVertices, declaration, indices,
                mesh.IndexBuffer.Is16Bit, 0, part.PrimitiveCount, bounds, texture, color, additive,
                basic.Any(e => e.VertexElementUsage == Microsoft.Xna.Framework.Graphics.VertexElementUsage.Normal),
                basic.Any(e => e.VertexElementUsage == Microsoft.Xna.Framework.Graphics.VertexElementUsage.TextureCoordinate))
            {
                World = world,
            });
        }
    }

    private static int IndexSize(IndexBufferContent indices) => indices.Is16Bit ? 2 : 4;

    /// <summary>Index values are relative to the part's first vertex and must stay inside its vertices (the GPU would read past them).</summary>
    private static void CheckIndices(byte[] indices, bool is16Bit, int vertices, string? mesh)
    {
        var size = is16Bit ? 2 : 4;
        for (var i = 0; i + size <= indices.Length; i += size)
        {
            var index = is16Bit ? BitConverter.ToUInt16(indices, i) : BitConverter.ToUInt32(indices, i);
            if (index >= (uint)vertices)
            {
                throw new InvalidDataException($"mesh {mesh}: index {index} beyond its {vertices} vertices");
            }
        }
    }

    private static bool IsFinite(Matrix m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
        && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);

    private static byte[] Slice(byte[] data, long start, long length, string? mesh) =>
        start >= 0 && length >= 0 && start + length <= data.Length
            ? data.AsSpan((int)start, (int)length).ToArray()
            : throw new InvalidDataException($"mesh {mesh}: range {start}+{length} outside its {data.Length}-byte buffer");
}
