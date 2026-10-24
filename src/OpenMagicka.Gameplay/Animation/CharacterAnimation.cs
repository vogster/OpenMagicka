using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Animation;

/// <summary>
/// A command tied to a window of a clip, from <see cref="Start"/> to <see cref="End"/> as shares of
/// its duration (docs/engine/ENTITIES_AI.md §4). Shared by every character of a template: what a
/// character has run is kept by its <see cref="CharacterAnimator"/>.
/// </summary>
public abstract class AnimationAction(float start, float end)
{
    public float Start { get; } = start;

    public float End { get; } = end;

    /// <summary>Runs in a frame of the window; <paramref name="first"/> on the first one.</summary>
    public abstract void Run(Character owner, bool first);

    /// <summary>
    /// The window is over, or the clip was left (the original's Kill). As the original, leaving a clip
    /// stops every action that had not stopped, run or not, and a loop wrapping on the frame its blend
    /// ends stops them all again: a stop must be safe to repeat.
    /// </summary>
    public virtual void Stop(Character owner)
    {
    }
}

/// <summary>An animation of a character: a clip played at a speed, with a blend time, looping or not, and its actions.</summary>
/// <param name="BlendTime">Not above 0 lets the caller choose the blend (one vanilla animation gives −0.2).</param>
public sealed record CharacterAnimation(string Name, AnimationClip Clip, float Speed, float BlendTime, bool Loop, IReadOnlyList<AnimationAction> Actions);

/// <summary>A template's animations for one weapon class, by name (ignoring case).</summary>
public sealed class AnimationSet
{
    private readonly Dictionary<string, CharacterAnimation> byName;

    public AnimationSet(IEnumerable<CharacterAnimation> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);
        byName = new Dictionary<string, CharacterAnimation>(StringComparer.OrdinalIgnoreCase);
        foreach (var animation in animations)
        {
            animation.Clip.Validate();
            if (!float.IsFinite(animation.Speed) || !float.IsFinite(animation.BlendTime))
            {
                throw new ArgumentOutOfRangeException(nameof(animations), animation.Name, "the speed and blend time must be finite");
            }
            byName[animation.Name] = animation;
        }
    }

    public int Count => byName.Count;

    public CharacterAnimation? Find(string name) => byName.GetValueOrDefault(name);
}

/// <summary>A template's animation sets by weapon class; set 0 is the default one.</summary>
public sealed class CharacterAnimations
{
    public const int Default = 0;

    public const string Idle = "idle";

    public CharacterAnimations(IReadOnlyList<AnimationSet?> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        if (sets.Count == 0 || sets[Default]?.Find(Idle) is null)
        {
            throw new ArgumentException("the default set needs an idle animation", nameof(sets));
        }
        Sets = sets;
    }

    public IReadOnlyList<AnimationSet?> Sets { get; }

    public AnimationSet DefaultSet => Sets[Default]!;
}
