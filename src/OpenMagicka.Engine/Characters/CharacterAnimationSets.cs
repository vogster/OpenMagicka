using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Gameplay.Animation;
using BonePose = OpenMagicka.Gameplay.Animation.BonePose;

namespace OpenMagicka.Engine.Characters;

/// <summary>
/// A character template's animation sets from the game files: each animation's clip is looked up by
/// name in the template's skeleton for its duration (docs/engine/ENTITIES_AI.md §4).
/// </summary>
public static class CharacterAnimationSets
{
    /// <param name="templateAsset">The template's asset path, which its skeleton reference is relative to.</param>
    /// <param name="action">Makes an action from its content (null from it leaves the action out); by default <see cref="CharacterAnimationActions.Create"/>.</param>
    /// <param name="missing">Collects the animations left out because their clip is not in the skeleton.</param>
    public static CharacterAnimations From(ContentLoader loader, string templateAsset, CharacterTemplateContent template,
        Func<AnimationActionContent, AnimationAction?>? action = null, ICollection<string>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(templateAsset);
        ArgumentNullException.ThrowIfNull(template);
        action ??= CharacterAnimationActions.Create;
        var (skeleton, clips) = LoadSkeleton(loader, templateAsset, template);
        var sets = template.AnimationSets.Select(set => set.Animations.Count == 0 ? null : new AnimationSet(set.Animations
            .Where(named => clips.ContainsKey(named.Clip.ClipName) || Missing(missing, $"{named.Animation} ({named.Clip.ClipName})"))
            .Select(named => new CharacterAnimation(
                named.Animation,
                clips[named.Clip.ClipName],
                named.Clip.Speed,
                named.Clip.BlendTime,
                named.Clip.Loop,
                [.. named.Clip.Actions.Select(action).OfType<AnimationAction>()]))))
            .ToList();
        return new CharacterAnimations(sets, skeleton);
    }

    private static bool Missing(ICollection<string>? missing, string animation)
    {
        missing?.Add(animation);
        return false;
    }

    /// <summary>The template's bones, and its skeleton's clips by their exact name, as the original's clip dictionary.</summary>
    private static (Skeleton Skeleton, Dictionary<string, AnimationClip> Clips) LoadSkeleton(ContentLoader loader, string templateAsset, CharacterTemplateContent template)
    {
        var reference = template.Skeleton ?? throw new InvalidDataException($"{template.Id} has no skeleton");
        var file = loader.LoadReference(templateAsset, reference);
        if (file.Root is not SkinnedModelContent model)
        {
            throw new InvalidDataException($"{template.Id}: the skeleton is not a skinned model");
        }
        var bones = model.Bones.Select(r => r is { } bone ? file.Resolve(bone) as SkinnedModelBoneContent : null).ToList();
        if (bones.Select((b, i) => b?.Index == i).Contains(false))
        {
            throw new InvalidDataException($"{template.Id}: the skeleton's bones are missing or out of order");
        }
        var skeleton = new Skeleton([.. bones.Select(b => new SkeletonBone(
            b!.Name,
            b.Parent is { } parent
                ? (file.Resolve(parent) as SkinnedModelBoneContent)?.Index ?? throw new InvalidDataException($"{template.Id}: bone {b.Name} has a missing parent")
                : -1,
            Pose(b.BindPose),
            XnaConversions.Matrix(b.InverseBindPose)))]);
        var clips = model.Clips
            .Select(r => r is { } clip ? file.Resolve(clip) as AnimationClipContent : null)
            .OfType<AnimationClipContent>()
            .ToDictionary(c => c.Name, Clip, StringComparer.Ordinal);
        return (skeleton, clips);
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
