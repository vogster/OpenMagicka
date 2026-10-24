using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Gameplay.Animation;

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
        var clips = Clips(loader, templateAsset, template);
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
        return new CharacterAnimations(sets);
    }

    private static bool Missing(ICollection<string>? missing, string animation)
    {
        missing?.Add(animation);
        return false;
    }

    /// <summary>The skeleton's clips by their exact name, as the original's clip dictionary.</summary>
    private static Dictionary<string, AnimationClip> Clips(ContentLoader loader, string templateAsset, CharacterTemplateContent template)
    {
        var skeleton = template.Skeleton ?? throw new InvalidDataException($"{template.Id} has no skeleton");
        var file = loader.LoadReference(templateAsset, skeleton);
        if (file.Root is not SkinnedModelContent model)
        {
            throw new InvalidDataException($"{template.Id}: the skeleton is not a skinned model");
        }
        return model.Clips
            .Select(r => r is { } reference ? file.Resolve(reference) as AnimationClipContent : null)
            .OfType<AnimationClipContent>()
            .ToDictionary(c => c.Name, c => new AnimationClip(c.Name, c.Duration), StringComparer.Ordinal);
    }
}
