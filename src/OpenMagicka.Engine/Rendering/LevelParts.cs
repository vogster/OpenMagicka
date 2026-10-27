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
/// from the part's mesh settings, which must name every mesh. A part's liquids and visual effects
/// are placed with it (at rest, like everything here).
/// A part whose rest pose is not finite (six energy rings of ch_end_of_world start with an all-NaN
/// keyframe) is skipped with its children. Each batch names its part by its place in <see cref="Order"/>,
/// so a frame can draw it where the part is now (<see cref="DeferredFrame.Parts"/>). docs/formats/XNB_READERS.md §4.3.
/// </summary>
public static class LevelParts
{
    /// <param name="shared">The level file's shared resources, where the parts' mesh effects are.</param>
    public static IReadOnlyList<LevelBatch> Batches(IReadOnlyList<AnimatedLevelPartContent> parts, IReadOnlyList<object?> shared)
    {
        ArgumentNullException.ThrowIfNull(shared);
        var batches = new List<LevelBatch>();
        foreach (var (part, index, at) in Placed(parts))
        {
            if (part.Model is { } model)
            {
                foreach (var mesh in model.Meshes)
                {
                    var setting = part.MeshSettings.FirstOrDefault(s => s.MeshName == mesh.Name)
                        ?? throw new InvalidDataException($"animated part {part.Name}: no mesh settings for mesh {mesh.Name}");
                    if (setting.Visible)
                    {
                        batches.AddRange(ModelMeshes.MeshBatches(model, mesh, at, shared).Select(b => b with { Part = index }));
                    }
                }
            }
            batches.AddRange(LevelLiquids.Batches(part.Liquids, at).Select(b => b with { Part = index, Liquid = LevelLiquids.Key(index, b.Liquid) }));
        }
        return batches;
    }

    /// <summary>The visual effects of the parts and their children, placed at the parts' rest pose.</summary>
    public static IReadOnlyList<Particles.PlacedEffect> Effects(IReadOnlyList<AnimatedLevelPartContent> parts) =>
        [.. Placed(parts).SelectMany(p => p.Part.Effects.Select(e => Particles.PlacedEffect.At(e, p.At)))];

    /// <summary>
    /// The parts and those under them in the order the batches number them (<see cref="LevelBatch.Part"/>): each
    /// part, then its children in the file's order.
    /// </summary>
    public static IReadOnlyList<AnimatedLevelPartContent> Order(IReadOnlyList<AnimatedLevelPartContent> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var order = new List<AnimatedLevelPartContent>();
        foreach (var part in parts)
        {
            Walk(part, order);
        }
        return order;
    }

    /// <summary>Each part's rest pose in the level, in <see cref="Order"/>; not finite where the file's is not.</summary>
    public static Matrix[] Rests(IReadOnlyList<AnimatedLevelPartContent> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var rests = new List<Matrix>();
        foreach (var part in parts)
        {
            AddRests(part, Matrix.Identity, rests);
        }
        return [.. rests];
    }

    /// <summary>
    /// How far each part has moved from its rest pose (the way back from rest, then to where it is now), in
    /// <see cref="Order"/>, into <paramref name="moves"/>; identity for a part the frame lacks.
    /// </summary>
    /// <param name="unrests">The way back from each part's rest pose (<see cref="GpuLevel.PartUnrests"/>).</param>
    public static void Moves(IReadOnlyList<Matrix> unrests, IReadOnlyList<Matrix>? parts, Matrix[] moves)
    {
        ArgumentNullException.ThrowIfNull(unrests);
        ArgumentNullException.ThrowIfNull(moves);
        for (var i = 0; i < moves.Length; i++)
        {
            var move = parts is not null && i < parts.Count && i < unrests.Count ? unrests[i] * parts[i] : Matrix.Identity;
            moves[i] = IsFinite(move) ? move : Matrix.Identity;
        }
    }

    /// <summary>XNAnimation's pose matrix: scale, then rotation, then translation.</summary>
    public static Matrix PoseMatrix(BonePose pose) =>
        Matrix.CreateScale(pose.Scale) * Matrix.CreateFromQuaternion(pose.Orientation) * Matrix.CreateTranslation(pose.Translation);

    /// <summary>The parts with a finite rest pose, numbered in <see cref="Order"/>.</summary>
    private static IEnumerable<(AnimatedLevelPartContent Part, int Index, Matrix At)> Placed(IReadOnlyList<AnimatedLevelPartContent> parts)
    {
        var rests = Rests(parts);
        return Order(parts).Select((part, index) => (part, index, rests[index])).Where(p => IsFinite(p.Item3));
    }

    private static void Walk(AnimatedLevelPartContent part, List<AnimatedLevelPartContent> order)
    {
        order.Add(part);
        foreach (var child in part.Children)
        {
            Walk(child, order);
        }
    }

    private static void AddRests(AnimatedLevelPartContent part, Matrix parent, List<Matrix> rests)
    {
        var world = (part.Animation.Count > 0 ? PoseMatrix(part.Animation[0].Pose) : Matrix.Identity) * parent;
        rests.Add(world);
        foreach (var child in part.Children)
        {
            AddRests(child, world, rests);
        }
    }

    internal static bool IsFinite(Matrix m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
        && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
