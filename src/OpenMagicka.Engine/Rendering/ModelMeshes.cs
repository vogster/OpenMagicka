using Microsoft.Xna.Framework;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// Batches of XNA 3.1 models: each mesh part becomes a self-contained batch (its slice of the
/// mesh's vertices, from StreamOffset + BaseVertex, and its indices, which are relative to that
/// first vertex and checked against it). Used for animated level parts and for whole models.
/// </summary>
public static class ModelMeshes
{
    /// <summary>
    /// The batches of a model file as it stands at rest: an XNA model's meshes at the absolute
    /// transform of their parent bone; a skinned model's meshes as stored, in their bind pose
    /// (the skeleton is not applied without an animation).
    /// </summary>
    public static IReadOnlyList<LevelBatch> ForModel(XnbContent file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ForModel(file.Root, file.SharedResources);
    }

    /// <param name="shared">The file's shared resources, where the parts' effects are.</param>
    public static IReadOnlyList<LevelBatch> ForModel(object? root, IReadOnlyList<object?> shared)
    {
        ArgumentNullException.ThrowIfNull(shared);
        var (model, skinned) = root switch
        {
            ModelContent m => (m, false),
            SkinnedModelContent s => (s.Model, true),
            _ => throw new InvalidDataException($"not a model: {root?.GetType().Name ?? "null"}"),
        };
        if (model is null)
        {
            return [];
        }
        var bones = AbsoluteBoneTransforms(model);
        return model.Meshes.SelectMany(mesh => MeshBatches(model, mesh, skinned ? Matrix.Identity : MeshWorld(mesh, bones), shared)).ToArray();
    }

    private static Matrix MeshWorld(ModelMeshContent mesh, Matrix[] bones) => mesh.ParentBone switch
    {
        null => Matrix.Identity,
        { } bone when (uint)bone < (uint)bones.Length => bones[bone],
        { } bone => throw new InvalidDataException($"mesh {mesh.Name}: parent bone {bone} of {bones.Length}"),
    };

    /// <summary>
    /// Each bone's transform relative to the model: its own transform times its parent's absolute
    /// one. Iterative (no recursion on long chains); a cycle or a parent outside the bones is invalid.
    /// </summary>
    public static Matrix[] AbsoluteBoneTransforms(ModelContent model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var count = model.Bones.Count;
        var absolute = new Matrix[count];
        var state = new byte[count];   // 0 to do, 1 on the current chain, 2 done
        var chain = new Stack<int>();
        for (var start = 0; start < count; start++)
        {
            for (var bone = start; bone >= 0 && state[bone] != 2;)
            {
                if (state[bone] == 1)
                {
                    throw new InvalidDataException("model bones form a cycle");
                }
                state[bone] = 1;
                chain.Push(bone);
                bone = model.Bones[bone].Parent is { } parent ? Parent(parent, count) : -1;
            }
            while (chain.TryPop(out var bone))
            {
                absolute[bone] = model.Bones[bone].Parent is { } parent ? model.Bones[bone].Transform * absolute[parent] : model.Bones[bone].Transform;
                state[bone] = 2;
            }
        }
        return absolute;
    }

    private static int Parent(int parent, int count) =>
        (uint)parent < (uint)count ? parent : throw new InvalidDataException($"bone parent {parent} of {count} bones");

    /// <summary>The diffuse texture, colour and blending of a part's material, for the viewer's simple shader.</summary>
    private static (ExternalReference? Texture, Vector3 Color, bool Additive) Material(object? effect) => effect switch
    {
        RenderDeferredEffectContent m => (m.Layer0.DiffuseTexture, m.Layer0.DiffuseColor, false),
        AdditiveEffectContent a => (a.TextureEnabled ? a.Texture : null, a.ColorTint, true),
        SkinnedModelBasicEffectContent s => (s.DiffuseMap0Enabled ? s.DiffuseMap0.External : null, s.DiffuseColor, false),
        SkinnedModelDeferredNormalMappedEffectContent n => (n.DiffuseMap, n.DiffuseColor, false),
        BasicEffectContent b => (b.Texture, b.DiffuseColor, false),
        _ => (null, Vector3.One, false),
    };

    /// <summary>The batches of one mesh drawn with <paramref name="world"/>; liquid materials on a mesh are not drawn (see <see cref="LevelLiquids"/>).</summary>
    public static IReadOnlyList<LevelBatch> MeshBatches(ModelContent model, ModelMeshContent mesh, Matrix world, IReadOnlyList<object?> shared)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(shared);
        var batches = new List<LevelBatch>();
        if (mesh.VertexBuffer is null || mesh.IndexBuffer is null)
        {
            return batches;
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
            if (effect is RenderDeferredLiquidEffectContent or LavaEffectContent)
            {
                continue;   // a water material on a mesh: the original draws only a part's own liquids
            }
            var declaration = model.VertexDeclarations[part.VertexDeclarationIndex];
            var (texture, color, additive) = Material(effect);
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
                Material = effect,
            });
        }
        return batches;
    }

    private static int IndexSize(IndexBufferContent indices) => indices.Is16Bit ? 2 : 4;

    /// <summary>Index values are relative to the part's first vertex and must stay inside its vertices (the GPU would read past them).</summary>
    internal static void CheckIndices(byte[] indices, bool is16Bit, int vertices, string? mesh)
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

    private static byte[] Slice(byte[] data, long start, long length, string? mesh) =>
        start >= 0 && length >= 0 && start + length <= data.Length
            ? data.AsSpan((int)start, (int)length).ToArray()
            : throw new InvalidDataException($"mesh {mesh}: range {start}+{length} outside its {data.Length}-byte buffer");
}
