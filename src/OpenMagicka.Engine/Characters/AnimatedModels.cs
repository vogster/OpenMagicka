using OpenMagicka.Content;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Gameplay.Animation;
using BonePose = OpenMagicka.Gameplay.Animation.BonePose;

namespace OpenMagicka.Engine.Characters;

/// <summary>A skinned model's bones and its clips by their exact name.</summary>
public sealed record AnimatedModel(Skeleton Skeleton, IReadOnlyDictionary<string, AnimationClip> Clips);

/// <summary>
/// The bones and clips of the game's skinned models: a character's skeleton (docs/engine/ENTITIES_AI.md §4), a
/// barrier's or a mine's model and its clips (docs/engine/ARCHITECTURE.md §17).
/// </summary>
public static class AnimatedModels
{
    /// <summary>A skinned model file's bones (parents first, as stored) and clips.</summary>
    /// <param name="what">What the file is, for the errors.</param>
    public static AnimatedModel From(XnbContent file, string what)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Root is not SkinnedModelContent model)
        {
            throw new InvalidDataException($"{what}: the skeleton is not a skinned model");
        }
        var bones = model.Bones.Select(r => r is { } bone ? file.Resolve(bone) as SkinnedModelBoneContent : null).ToList();
        if (bones.Select((b, i) => b?.Index == i).Contains(false))
        {
            throw new InvalidDataException($"{what}: the skeleton's bones are missing or out of order");
        }
        var skeleton = new Skeleton([.. bones.Select(b => new SkeletonBone(
            b!.Name,
            b.Parent is { } parent
                ? (file.Resolve(parent) as SkinnedModelBoneContent)?.Index ?? throw new InvalidDataException($"{what}: bone {b.Name} has a missing parent")
                : -1,
            Pose(b.BindPose),
            XnaConversions.Matrix(b.InverseBindPose)))]);
        var clips = model.Clips
            .Select(r => r is { } clip ? file.Resolve(clip) as AnimationClipContent : null)
            .OfType<AnimationClipContent>()
            .ToDictionary(c => c.Name, Clip, StringComparer.Ordinal);
        return new AnimatedModel(skeleton, clips);
    }

    /// <summary>
    /// The models by asset, each read once (not kept in the loader's cache: only the bones and clips are kept); null for one
    /// the game lacks or that does not read as a skinned model, said once. The assets named are read at once, so their
    /// first use does not stall a tick.
    /// </summary>
    public static Func<string, AnimatedModel?> Loader(ContentLoader loader, IEnumerable<string>? preload = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var known = new Dictionary<string, AnimatedModel?>(StringComparer.OrdinalIgnoreCase);
        Func<string, AnimatedModel?> load = asset =>
        {
            if (!known.TryGetValue(asset, out var model))
            {
                try
                {
                    model = loader.Files.Exists(asset + ".xnb") ? From(loader.LoadUncached(asset), asset) : null;
                }
                catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or FormatException or ArgumentException)
                {
                    Console.Error.WriteLine($"OpenMagicka: {asset} does not read as a skinned model: {e.Message}");
                    model = null;
                }
                known[asset] = model;
            }
            return model;
        };
        foreach (var asset in preload ?? [])
        {
            load(asset);
        }
        return load;
    }

    private static AnimationClip Clip(AnimationClipContent clip)
    {
        var channels = new Dictionary<string, IReadOnlyList<Keyframe>>(StringComparer.Ordinal);
        foreach (var channel in clip.Channels.Where(c => c.Keyframes.Count > 0))
        {
            channels.TryAdd(channel.BoneName, [.. channel.Keyframes.Select(k => new Keyframe(k.Time, Pose(k.Pose)))]);
        }
        try
        {
            return new AnimationClip(clip.Name, clip.Duration, channels);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    private static BonePose Pose(Content.XnAnimation.BonePose pose) => new(
        new System.Numerics.Vector3(pose.Translation.X, pose.Translation.Y, pose.Translation.Z),
        new System.Numerics.Quaternion(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W),
        new System.Numerics.Vector3(pose.Scale.X, pose.Scale.Y, pose.Scale.Z));
}
