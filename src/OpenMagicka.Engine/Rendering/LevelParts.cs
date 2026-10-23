using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// Batches of a level's animated parts (doors, lifts, machinery) at their rest pose, as the
/// original places them when a level starts: a part's world transform is the pose of its
/// animation's first keyframe times its parent part's world transform, and every mesh of the part's
/// model is drawn with it (the model's bone transforms are not used). A mesh's visibility comes
/// from the part's mesh settings, which must name every mesh. A part's liquids move with it.
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
                    batches.AddRange(ModelMeshes.MeshBatches(model, mesh, world, shared));
                }
            }
        }
        batches.AddRange(LevelLiquids.Batches(part.Liquids, world));
        foreach (var child in part.Children)
        {
            Add(child, world, shared, batches);
        }
    }

    private static bool IsFinite(Matrix m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
        && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
