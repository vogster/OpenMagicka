using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Animation;

/// <summary>
/// A character's animation playback (docs/engine/ENTITIES_AI.md §4.1): which animation plays from
/// which set, the clock, and the windows of the current clip's actions. Changes in place.
/// </summary>
public sealed class CharacterAnimator
{
    private readonly Character owner;
    private IReadOnlyList<AnimationAction> actions = [];
    private bool[] started = [];
    private bool[] ended = [];

    public CharacterAnimator(Character owner, CharacterAnimations animations)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Animations = animations ?? throw new ArgumentNullException(nameof(animations));
        Force(CharacterAnimations.Idle);
    }

    public CharacterAnimations Animations { get; }

    public AnimationClock Clock { get; } = new();

    /// <summary>The animation asked for last (the one playing once its crossfade ends).</summary>
    public string Current { get; private set; } = CharacterAnimations.Idle;

    /// <summary>The weapon class whose set <see cref="Current"/> came from.</summary>
    public int CurrentSet { get; private set; }

    /// <summary>The weapon classes of the equipped items other than staffs, in slot order; the first that has an animation plays it.</summary>
    public IReadOnlyList<int> EquippedClasses { get; set; } = [];

    /// <summary>The actions running now: those of the clip whose crossfade ended last.</summary>
    public IReadOnlyList<AnimationAction> Actions => actions;

    public bool HasFinished => Clock.HasFinished;

    /// <summary>
    /// Blends to an animation (the clip's own blend time, or <paramref name="blendTime"/>), unless it
    /// is already playing and not finished. A missing one falls back to idle, not looping (and, being
    /// recorded as idle, starts over at every request, as the original). The actions taken when a
    /// blend ends are those of the animation asked for last, even if it is still queued.
    /// </summary>
    public void GoTo(string name, float blendTime)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Current, StringComparison.OrdinalIgnoreCase) && !Clock.HasFinished)
        {
            return;
        }
        var (animation, set, fallback) = Choose(name);
        (Current, CurrentSet) = (animation.Name, set);
        Clock.ClipSpeed = animation.Speed;
        Clock.CrossFade(animation.Clip, animation.BlendTime > 0 ? animation.BlendTime : blendTime, !fallback && animation.Loop);
    }

    /// <summary>Starts an animation at once, dropping any queued crossfade, and takes its actions.</summary>
    public void Force(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Clock.ClearQueue();
        var (animation, set, _) = Choose(name);
        (Current, CurrentSet) = (animation.Name, set);
        Clock.ClipSpeed = animation.Speed;
        Clock.Start(animation.Clip, animation.Loop);
        TakeActions();
    }

    /// <summary>Whether the default set, or the set of an equipped item, has the animation.</summary>
    public bool Has(string name) => !Choose(name).Fallback;

    private (CharacterAnimation Animation, int Set, bool Fallback) Choose(string name)
    {
        foreach (var weaponClass in EquippedClasses)
        {
            if (weaponClass > CharacterAnimations.Default && weaponClass < Animations.Sets.Count && Animations.Sets[weaponClass]?.Find(name) is { } own)
            {
                return (own, weaponClass, false);
            }
        }
        return Animations.DefaultSet.Find(name) is { } plain
            ? (plain, CharacterAnimations.Default, false)
            : (Animations.DefaultSet.Find(CharacterAnimations.Idle)!, CharacterAnimations.Default, true);
    }

    /// <summary>Moves the clock, then runs the current clip's action windows.</summary>
    public void Update(float dt)
    {
        var events = Clock.Update(dt);
        if (events.HasFlag(ClockEvents.Looped))
        {
            ClearMarks();
        }
        if (events.HasFlag(ClockEvents.CrossfadeFinished))
        {
            TakeActions();
        }
        RunActions();
    }

    /// <summary>The ended crossfade's animation brings its actions; the old ones that had not ended stop.</summary>
    private void TakeActions()
    {
        var next = (Animations.Sets[CurrentSet] ?? Animations.DefaultSet).Find(Current)?.Actions ?? [];
        var (old, oldEnded) = (actions, ended);
        if (!ReferenceEquals(next, old))
        {
            for (var i = 0; i < old.Count; i++)
            {
                if (!oldEnded[i])
                {
                    old[i].Stop(owner);
                }
            }
        }
        actions = next;
        (started, ended) = (new bool[next.Count], new bool[next.Count]);
    }

    private void ClearMarks()
    {
        Array.Clear(started);
        Array.Clear(ended);
    }

    /// <summary>
    /// Each action's window, by index over whatever list is current: as in the original, an action that
    /// forces another animation hands the rest of the loop to the new clip's actions.
    /// </summary>
    private void RunActions()
    {
        var t = Clock.NormalizedTime;
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var (ran, over) = (started[i], ended[i]);
            if (t >= action.Start && (!ran || t <= action.End))
            {
                (ran, over) = (true, false);
                action.Run(owner, !started[i]);
            }
            else if (t >= action.End && ran && !over)
            {
                over = true;
                action.Stop(owner);
            }
            if (i < actions.Count)
            {
                (started[i], ended[i]) = (ran, over);
            }
        }
    }
}
