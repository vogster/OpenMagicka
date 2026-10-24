using System.Numerics;
using OpenMagicka.Gameplay.Animation;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>Where a character's bones and held items are (docs/engine/ENTITIES_AI.md §4.3).</summary>
public sealed partial class Character
{
    private readonly Skeleton? skeleton;
    private readonly BonePose[] poses = [];
    private readonly Matrix4x4[] placed = [];
    private readonly Matrix4x4[] skinned = [];
    private Matrix4x4 root = Matrix4x4.Identity;

    public Skeleton? Skeleton => skeleton;

    /// <summary>Each bone's skinning transform this frame (empty without a skeleton).</summary>
    public ReadOnlySpan<Matrix4x4> BoneTransforms => skinned;

    /// <summary>The model's scale × the body's orientation (the facing flattened, −Z when it has none), at the feet, from the body now.</summary>
    public Matrix4x4 RootMatrix()
    {
        var forward = Body.Facing with { Y = 0 };
        forward = forward.LengthSquared() > float.Epsilon ? Vector3.Normalize(forward) : -Vector3.UnitZ;
        var right = Vector3.Cross(forward, Vector3.UnitY);
        var orientation = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            0, 1, 0, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            Position.X, Position.Y + Body.HeightOffset, Position.Z, 1);
        return Matrix4x4.CreateScale(Template.ModelScale) * orientation;
    }

    /// <summary>A bone's place in the world (its bind pose placed by this frame's pose), or the root without a skeleton.</summary>
    public Matrix4x4 BoneWorld(int bone)
    {
        if (skeleton is null)
        {
            return root;
        }
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, skeleton.Bones.Count);
        return placed[bone];
    }

    /// <summary>The slot's attach matrix on its bone (§4.3); without a skeleton the item sits at the root.</summary>
    private (int Bone, Matrix4x4 Attach) AttachPoint(string bone, Vector3 rotation)
    {
        if (skeleton is null)
        {
            return (-1, Matrix4x4.Identity);
        }
        var index = skeleton.IndexOf(bone);
        if (index < 0)
        {
            throw new ArgumentException($"{Template.Id} has no bone {bone} to hold an item", nameof(bone));
        }
        if (!Matrix4x4.Invert(skeleton.Bones[index].InverseBindPose, out var bind))
        {
            throw new ArgumentException($"{Template.Id}: bone {bone} has no bind pose to hold an item by", nameof(bone));
        }
        var unscale = Matrix4x4.CreateScale(1 / Template.ModelScale);
        var turn = Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y + MathF.PI) * Matrix4x4.CreateRotationX(rotation.Z);
        return (index, unscale * turn * bind);
    }

    /// <summary>The pose for this frame's clip time, before the actions run (the animator's call).</summary>
    internal void UpdatePose()
    {
        if (skeleton is null || Animator is null)
        {
            return;
        }
        skeleton.Sample(Animator.Clock, poses);
        skeleton.Place(poses, root, placed, skinned);
    }

    /// <summary>Held items follow their bones, at the end of the frame.</summary>
    private void PlaceItems()
    {
        foreach (var slot in equipment)
        {
            if (slot.Item is { IsAttached: true } item)
            {
                item.Place(slot.Bone >= 0 ? slot.Attach * skinned[slot.Bone] : root);
            }
        }
    }
}
