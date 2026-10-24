using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

public sealed class CharacterAnimatorTests : IDisposable
{
    private readonly List<GameWorld> worlds = [];

    public void Dispose() => worlds.ForEach(w => w.Dispose());

    /// <summary>Notes every run and stop.</summary>
    internal sealed class Tracer(float start, float end, string name, List<string> log) : AnimationAction(start, end)
    {
        public override void Run(Character owner, bool first) => log.Add(first ? $"{name}:first" : $"{name}:run");

        public override void Stop(Character owner) => log.Add($"{name}:stop");
    }

    private static CharacterAnimations Animations(List<string> log, params (string Name, float Duration, bool Loop, AnimationAction[] Actions)[] extra)
    {
        CharacterAnimation[] plain =
        [
            new("idle", new AnimationClip("idle_clip", 2), 1, 0, true, [new Tracer(0, 1, "breathe", log)]),
            .. extra.Select(e => new CharacterAnimation(e.Name, new AnimationClip(e.Name + "_clip", e.Duration), 1, 0, e.Loop, e.Actions)),
        ];
        CharacterAnimation[] sword = [new("attack_melee0", new AnimationClip("sword_slash", 1), 2, 0.1f, false, [])];
        return new CharacterAnimations([new AnimationSet(plain), null, new AnimationSet(sword)]);
    }

    private (Character Character, CharacterAnimator Animator) Make(CharacterAnimations animations)
    {
        var world = World();
        worlds.Add(world);
        var character = Spawn(world);
        return (character, new CharacterAnimator(character, animations));
    }

    [Fact]
    public void AnAnimator_StartsIdle_WithItsActions()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log));

        animator.Update(0.1f);
        animator.Update(0.1f);

        Assert.Equal("idle", animator.Current);
        Assert.Equal(["breathe:first", "breathe:run"], log);
    }

    [Fact]
    public void ActionsRunInTheirWindow_AndStopOnceAfterIt()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("hit", 1, false, [new Tracer(0.25f, 0.55f, "strike", log)])));
        animator.GoTo("hit", 0);
        animator.Update(0.01f);                                                    // the crossfade ends: the hit starts
        log.Clear();

        foreach (var _ in Enumerable.Range(0, 11))
        {
            animator.Update(0.1f);
        }

        Assert.Equal(["strike:first", "strike:run", "strike:run", "strike:stop"], log);   // t = 0.3, 0.4, 0.5, then 0.6
        Assert.True(animator.HasFinished);
    }

    [Fact]
    public void ALateStart_StillRunsOnce()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("hit", 1, false, [new Tracer(0.1f, 0.2f, "strike", log)])));
        animator.GoTo("hit", 0);
        animator.Update(0.01f);
        log.Clear();

        animator.Update(0.5f);
        animator.Update(0.1f);

        Assert.Equal(["strike:first", "strike:stop"], log);
    }

    [Fact]
    public void LeavingAClip_StopsItsUnfinishedActions_AndALoopRunsThemAgain()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("walk", 1, true, [new Tracer(0, 0.9f, "step", log)])));
        animator.GoTo("walk", 0);
        animator.Update(0.01f);
        log.Clear();

        animator.Update(0.5f);
        animator.Update(0.6f);                                                     // wraps: the marks clear
        animator.GoTo("idle", 0.2f);
        animator.Update(0.1f);
        animator.Update(0.2f);                                                     // the blend ends: step stops

        Assert.Equal(["step:run", "step:first", "step:run", "step:stop", "breathe:first"], log);   // it started as the walk began
    }

    [Fact]
    public void TheSameAnimation_IsNotRestarted_UntilItFinishes()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("hit", 0.5f, false, [])));
        animator.GoTo("hit", 0);
        animator.Update(0.01f);
        animator.Update(0.2f);

        animator.GoTo("hit", 0);
        Assert.False(animator.Clock.IsCrossFading);
        animator.Update(0.4f);
        animator.GoTo("hit", 0);
        Assert.True(animator.Clock.IsCrossFading);                                // finished: it may start over
    }

    [Fact]
    public void AMissingAnimation_FallsBackToIdle_NotLooping()
    {
        var (_, animator) = Make(Animations([]));

        animator.GoTo("emote_dance", 0.1f);
        animator.Update(0.2f);

        Assert.Equal("idle", animator.Current);
        Assert.False(animator.Clock.IsLooping);
    }

    [Fact]
    public void AnEquippedWeapon_PlaysItsOwnSet_WhenItHasTheAnimation()
    {
        var (_, animator) = Make(Animations([], ("attack_melee0", 1, false, [])));
        animator.EquippedClasses = [2];

        animator.GoTo("attack_melee0", 0.3f);
        Assert.Equal(2f, animator.Clock.ClipSpeed);                               // the sword set's speed
        Assert.Equal(0.1f, animator.Clock.CrossFadeDuration);                     // its own blend time wins
        animator.Update(0.2f);

        Assert.Equal((2, "sword_slash"), (animator.CurrentSet, animator.Clock.Clip!.Name));
        animator.GoTo("idle", 0);
        animator.Update(0.01f);
        Assert.Equal(0, animator.CurrentSet);                                     // the sword set has no idle
    }

    [Fact]
    public void AQueuedRequest_BringsItsActions_WhenTheFirstBlendEnds_AsTheOriginal()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("a", 1, false, [new Tracer(0, 1, "a", log)]), ("b", 1, false, [new Tracer(0, 1, "b", log)])));
        animator.GoTo("a", 0.1f);
        animator.GoTo("b", 0.1f);

        animator.Update(0.2f);

        Assert.Equal("a_clip", animator.Clock.Clip!.Name);
        Assert.Contains("b:first", log);
        Assert.DoesNotContain("a:first", log);
    }

    [Fact]
    public void AnActionForcingAnotherAnimation_HandsTheRestOfTheFrameToTheNewActions()
    {
        var log = new List<string>();
        var forcing = new Forcer("die0", log);
        var (_, animator) = Make(Animations(log,
            ("hit", 1, false, [forcing, new Tracer(0, 1, "after", log)]),
            ("die0", 1, false, [new Tracer(0, 1, "fall0", log), new Tracer(0, 1, "fall1", log)])));
        forcing.Target = animator;
        animator.Force("hit");
        log.Clear();

        animator.Update(0.1f);

        Assert.Equal(["force", "after:stop", "fall1:first"], log);               // the old clip's actions stop (run or not); index 1 of the new list runs
        animator.Update(0.1f);
        Assert.Equal(["force", "after:stop", "fall1:first", "fall0:run", "fall1:run"], log);   // the forcer's mark landed on index 0 of the new list, as the original
    }

    [Fact]
    public void AMissingAnimation_StartsItsBlendOverAtEveryRequest_AsTheOriginal()
    {
        var (_, animator) = Make(Animations([]));
        animator.GoTo("emote_dance", 0.4f);
        animator.Update(0.1f);

        animator.GoTo("emote_dance", 0.4f);

        Assert.Equal(0.1f + 0.3f / 2, animator.Clock.CrossFadeDuration, 4);      // queued again: the blend hurries
    }

    /// <summary>Forces an animation on its first run.</summary>
    private sealed class Forcer(string animation, List<string> log) : AnimationAction(0, 1)
    {
        public CharacterAnimator? Target { get; set; }

        public override void Run(Character owner, bool first)
        {
            if (first)
            {
                log.Add("force");
                Target!.Force(animation);
            }
        }
    }

    [Fact]
    public void ForcingAnAnimation_SwitchesAtOnce()
    {
        var log = new List<string>();
        var (_, animator) = Make(Animations(log, ("die0", 1, false, [new Tracer(0, 1, "fall", log)])));
        animator.Update(0.1f);
        log.Clear();

        animator.Force("die0");
        animator.Update(0.1f);

        Assert.Equal(["breathe:stop", "fall:first"], log);
        Assert.Equal("die0_clip", animator.Clock.Clip!.Name);
    }

    [Fact]
    public void ADefaultSetWithoutIdle_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CharacterAnimations([new AnimationSet([])]));
        Assert.Throws<ArgumentException>(() => new CharacterAnimations([]));
    }
}
