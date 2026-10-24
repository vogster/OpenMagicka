using System.Numerics;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.AI;

public sealed partial class Agent
{
    /// <summary>
    /// A script's order (§7.10): the wandering speed, the zone, the order with its reaction, targets
    /// and events, and any clips. Only an order to one NPC by name panics it at once, as the original;
    /// an order to a zone's NPCs does not.
    /// </summary>
    public void Give(GiveOrderSpec spec, bool byName)
    {
        ArgumentNullException.ThrowIfNull(spec);
        (WanderSpeed, TargetArea) = (spec.WanderSpeed, spec.TargetArea);
        SetOrder(spec.Order, spec.ReactTo, spec.Reaction, spec.PriorityTarget, spec.PriorityAbility, spec.Trigger, spec.Events);
        if (spec.IdleAnimation is { } idle)
        {
            Owner.SpecialIdleAnimation = idle;
        }
        if (spec.Animation is { } clip)
        {
            Owner.GoToAnimation(clip, 0.2f);
        }
        if (byName && spec.Order == Order.Panic)
        {
            Owner.OrderPanic();
        }
    }

    /// <summary>Every state down to idle, letting go of the target of an attack (a charm, a new order).</summary>
    public void DropToIdle()
    {
        while (State != AiStates.Idle)
        {
            if (State == AiStates.Attack)
            {
                ReleaseTarget();
            }
            PopState();
        }
    }

    internal void Fire(int trigger)
    {
        if (trigger != 0)
        {
            Triggered?.Invoke(this, trigger);
        }
    }

    /// <summary>
    /// The scripted events, run by idle (§7.6): with the delay spent, the current event acts and the
    /// next waits its delay (a clip with none: until it ends); when a delay runs out, the trigger of the
    /// event before fires. True when idle must not go on to stand still (a walk was pushed, a turn is
    /// under way, or the next is a walk).
    /// </summary>
    internal bool RunEvents(float dt)
    {
        if (Events is not { Length: > 0 } events || !(EventIndex < events.Length || EventDelay >= 0 || float.IsNaN(EventDelay)))
        {
            return false;
        }
        if (EventDelay < 0)
        {
            return Act(events, events[EventIndex]);
        }
        if (float.IsNaN(EventDelay))
        {
            if (EventIndex > 0 && events[EventIndex - 1] is AnimationAiEvent clip
                && (Owner.Animator?.Clock.HasFinished != false || Owner.CurrentAnimation != clip.Animation))
            {
                EventDelay = -1;
                Fire(events[EventIndex - 1].Trigger);
            }
            return false;
        }
        EventDelay -= dt;
        if (EventDelay < 0 && EventIndex > 0)
        {
            Fire(events[EventIndex - 1].Trigger);
        }
        return EventIndex < events.Length && events[EventIndex] is MoveAiEvent && EventDelay <= 0;
    }

    private bool Act(AiEvent[] events, AiEvent current)
    {
        switch (current)
        {
            case MoveAiEvent move:
                return Walk(events, move);
            case AnimationAiEvent clip:
                if (clip.IdleAnimation is { } idle)
                {
                    Owner.SpecialIdleAnimation = idle;
                }
                Owner.GoToAnimation(clip.Animation, clip.BlendTime);
                EventDelay = clip.Delay < float.Epsilon ? float.NaN : clip.Delay;
                EventIndex++;
                return false;
            case FaceAiEvent face:
                return Turn(face);
            case KillAiEvent kill:
                if (kill.Remove)
                {
                    Owner.Terminate();
                }
                else
                {
                    Owner.Kill();
                }
                return false;
            case LoopAiEvent loop:
                if (loop.Reverse && events.Length > 2)
                {
                    for (var i = 0; i < (events.Length - 2) / 2; i++)
                    {
                        (events[i], events[events.Length - 2 - i]) = (events[events.Length - 2 - i], events[i]);
                    }
                }
                EventIndex = 0;
                return false;
            default:
                return false;
        }
    }

    /// <summary>To the point (from the leader's place, when it follows one); there, turned when it must be, the next.</summary>
    private bool Walk(AiEvent[] events, MoveAiEvent move)
    {
        var from = Leader?.Owner.Position ?? Owner.Position;
        if (((move.Waypoint - from) with { Y = 0 }).LengthSquared() >= 0.25f)
        {
            WayPoint = move.Waypoint;
            PushState(AiStates.Move);
            return true;
        }
        if (move.FixedDirection && Vector3.Dot(Owner.Body.Facing, move.Direction) < 0.99f)
        {
            Owner.Body.WantedDirection = move.Direction;
            return true;
        }
        EventDelay = move.Delay;
        if (move.Delay < float.Epsilon)
        {
            Fire(move.Trigger);
        }
        EventIndex++;
        return EventIndex < events.Length && events[EventIndex] is MoveAiEvent && EventDelay <= 0;
    }

    /// <summary>Toward an entity or locator by name, else along the direction; done when it faces it.</summary>
    private bool Turn(FaceAiEvent face)
    {
        var way = face.Direction;
        if (face.TargetId != 0)
        {
            var at = Owner.Manager?.FindByUniqueId(face.TargetId)?.Position ?? Owner.Manager?.Places?.Locator(face.TargetId);
            if (at is not { } spot)
            {
                (EventDelay, EventIndex) = (face.Delay, EventIndex + 1);
                return false;
            }
            var flat = (spot - Owner.Position) with { Y = 0 };
            way = flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : Owner.Body.Facing;
        }
        Owner.Body.WantedDirection = way;
        if (Vector3.Dot(way, Owner.Body.Facing) < 0.99f)
        {
            return true;
        }
        (EventDelay, EventIndex) = (face.Delay, EventIndex + 1);
        return false;
    }
}
