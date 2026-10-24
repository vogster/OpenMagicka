using System.Numerics;

namespace OpenMagicka.Gameplay.Animation;

/// <summary>A bone's local pose: translation, rotation and scale (docs/engine/ENTITIES_AI.md §4.3).</summary>
public readonly record struct BonePose(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static BonePose Identity { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    /// <summary>The rotation with the translation, then each axis column scaled (not the translation), as XNAnimation.</summary>
    public Matrix4x4 ToMatrix()
    {
        var m = Matrix4x4.CreateFromQuaternion(Rotation);
        m.Translation = Translation;
        (m.M11, m.M21, m.M31) = (m.M11 * Scale.X, m.M21 * Scale.X, m.M31 * Scale.X);
        (m.M12, m.M22, m.M32) = (m.M12 * Scale.Y, m.M22 * Scale.Y, m.M32 * Scale.Y);
        (m.M13, m.M23, m.M33) = (m.M13 * Scale.Z, m.M23 * Scale.Z, m.M33 * Scale.Z);
        return m;
    }

    /// <summary>Lerped translation and scale, a normalised shorter-way lerp of the rotation; the amount is clamped.</summary>
    public static BonePose Lerp(BonePose a, BonePose b, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new(Vector3.Lerp(a.Translation, b.Translation, amount), Quaternion.Lerp(a.Rotation, b.Rotation, amount), Vector3.Lerp(a.Scale, b.Scale, amount));
    }
}

/// <summary>A key of a bone's channel.</summary>
public readonly record struct Keyframe(float Time, BonePose Pose);

/// <param name="Parent">The parent's index, before this bone's; −1 for the root.</param>
public sealed record SkeletonBone(string Name, int Parent, BonePose BindPose, Matrix4x4 InverseBindPose);

/// <summary>A character's bones, parents first.</summary>
public sealed class Skeleton
{
    private readonly Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

    public Skeleton(IReadOnlyList<SkeletonBone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (bones.Count == 0)
        {
            throw new ArgumentException("a skeleton needs a bone", nameof(bones));
        }
        for (var i = 0; i < bones.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(bones[i], nameof(bones));
            if (bones[i].Parent >= i || bones[i].Parent < (i == 0 ? -1 : 0))
            {
                throw new ArgumentException($"bone {bones[i].Name} must follow its parent", nameof(bones));
            }
            byName.TryAdd(bones[i].Name, i);                                      // the first of a name, as the original's search
        }
        Bones = [.. bones];
    }

    public IReadOnlyList<SkeletonBone> Bones { get; }

    /// <summary>A bone by name ignoring case, or −1.</summary>
    public int IndexOf(string name) => byName.GetValueOrDefault(name, -1);

    /// <summary>The local poses for the clock's clip, blended towards the incoming clip's first key during a crossfade.</summary>
    public void Sample(AnimationClock clock, Span<BonePose> poses)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(poses.Length, Bones.Count, nameof(poses));
        var (clip, fade) = (clock.Clip, clock.CrossFadeClip);
        var amount = clock.CrossFadeDuration > float.Epsilon ? clock.CrossFadeTime / clock.CrossFadeDuration : 1;
        for (var i = 0; i < Bones.Count; i++)
        {
            var bind = Bones[i].BindPose;
            var pose = clip?.Channels?.TryGetValue(Bones[i].Name, out var keys) == true ? ChannelAt(keys, clock.Time, clip.Duration, clock.IsLooping) : bind;
            if (fade is not null)
            {
                var incoming = fade.Channels?.TryGetValue(Bones[i].Name, out var first) == true ? first[0].Pose : bind;
                pose = BonePose.Lerp(pose, incoming, amount);
            }
            poses[i] = pose;
        }
    }

    /// <summary>Each bone's skinning transform: its inverse bind pose × its pose placed by its parent (the root by <paramref name="root"/>).</summary>
    public void Place(ReadOnlySpan<BonePose> poses, Matrix4x4 root, Span<Matrix4x4> placed, Span<Matrix4x4> skinned)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(poses.Length, Bones.Count, nameof(poses));
        ArgumentOutOfRangeException.ThrowIfLessThan(placed.Length, Bones.Count, nameof(placed));
        ArgumentOutOfRangeException.ThrowIfLessThan(skinned.Length, Bones.Count, nameof(skinned));
        for (var i = 0; i < Bones.Count; i++)
        {
            placed[i] = poses[i].ToMatrix() * (i == 0 ? root : placed[Bones[i].Parent]);
        }
        for (var i = 0; i < Bones.Count; i++)
        {
            skinned[i] = Bones[i].InverseBindPose * placed[i];
        }
    }

    /// <summary>A channel at a time: the key at or before it, towards the next (the first again after the last when looping).</summary>
    public static BonePose ChannelAt(IReadOnlyList<Keyframe> keys, float time, float duration, bool looping)
    {
        var index = KeyAt(keys, time);
        var next = (index + 1) % keys.Count;
        if (next == 0 && !looping)
        {
            next = index;
        }
        if (next == index)
        {
            return keys[index].Pose;
        }
        var gap = next != 0 ? keys[next].Time - keys[index].Time : duration - keys[index].Time;
        return gap > 0 ? BonePose.Lerp(keys[index].Pose, keys[next].Pose, (time - keys[index].Time) / gap) : keys[index].Pose;
    }

    /// <summary>The original's binary search: a key at the time, else the last before it (the first when before them all).</summary>
    private static int KeyAt(IReadOnlyList<Keyframe> keys, float time)
    {
        var (mid, low, high) = (0, 0, keys.Count - 1);
        while (high >= low)
        {
            mid = (low + high) / 2;
            if (keys[mid].Time < time)
            {
                low = mid + 1;
            }
            else if (keys[mid].Time > time)
            {
                high = mid - 1;
            }
            else
            {
                break;
            }
        }
        return mid > 0 && keys[mid].Time > time ? mid - 1 : mid;
    }
}
