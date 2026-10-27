using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Navigation;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Gameplay.AI;

/// <summary>One of a mind's states (ENTITIES_AI.md §7.6): the top of its stack acts each time it thinks.</summary>
public interface IAiState
{
    void Enter(Agent agent);

    void Execute(Agent agent, float dt);

    void Exit(Agent agent);
}

/// <summary>Where a level's zones and locators are, for minds that defend or wander in one (the level answers).</summary>
public interface IAiPlaces
{
    /// <summary>The point of the zone nearest the given one; null when there is no such zone.</summary>
    Vector3? ClosestInArea(int area, Vector3 point);

    /// <summary>A locator's position; null when there is none.</summary>
    Vector3? Locator(int locator);
}

/// <summary>The AI states, one of each.</summary>
public static class AiStates
{
    public static readonly IAiState Idle = new IdleAiState();

    public static readonly IAiState Attack = new AttackAiState();

    public static readonly IAiState Move = new MoveAiState();

    public static readonly IAiState Wander = new WanderAiState();

    public static readonly IAiState Panic = new PanicAiState();

    public static readonly IAiState BreakFree = new BreakFreeAiState();

    /// <summary>Held in vines (or, with grips, a grip) and strong enough to struggle.</summary>
    internal static bool MustBreakFree(Agent agent) => (agent.Owner.IsEntangled || agent.Owner.IsGripped) && agent.Profile.BreakFreeStrength > float.Epsilon;
}

/// <summary>
/// Idle (§7.6): it struggles free, flees what it shies from too much, and takes up its order: an
/// attack chooses a target, a defence goes back to its zone or locator first, a wander or panic
/// begins. Then the order's scripted events run; otherwise it stands.
/// </summary>
internal sealed class IdleAiState : IAiState
{
    /// <summary>
    /// How long elements may wait chanted with no target before they are let go. As the original, the
    /// count is one for every mind (ours: of the level).
    /// </summary>
    public const float FreezeLimit = 2;

    public void Enter(Agent agent)
    {
    }

    public void Exit(Agent agent)
    {
    }

    public void Execute(Agent agent, float dt)
    {
        if (AiStates.MustBreakFree(agent))
        {
            agent.PushState(AiStates.BreakFree);
            return;
        }
        if (agent.Order != Order.Defend && agent.Avoidance() is var shy && shy.Length() > Agent.FleeAvoidance)
        {
            agent.WayPoint = agent.Owner.Position + shy;
            agent.PushState(AiStates.Move);
            return;
        }
        switch (agent.Order)
        {
            case Order.Attack:
                var minds = agent.Owner.Manager?.Minds;
                if (Engage(agent))
                {
                    minds?.ResetStuck();
                    return;
                }
                if (agent.Owner.SpellQueue.Count > 0)
                {
                    if (minds is not null && minds.Stuck(dt) > FreezeLimit)
                    {
                        minds.ResetStuck();
                        agent.Owner.ClearQueue();
                    }
                }
                else
                {
                    minds?.ResetStuck();
                }
                break;
            case Order.Defend:
                if (BackToPost(agent) || Engage(agent))
                {
                    return;
                }
                break;
            case Order.Wander:
                agent.PushState(AiStates.Wander);
                break;
            case Order.Panic:
                agent.PushState(AiStates.Panic);
                break;
        }
        if (agent.RunEvents(dt))
        {
            return;
        }
        agent.Owner.Body.Movement = Vector3.Zero;
    }

    private static bool Engage(Agent agent)
    {
        agent.ChooseTarget(out var target, out var ability);
        if (target is null)
        {
            return false;
        }
        agent.AddTarget(target, ability);
        agent.PushState(AiStates.Attack);
        return true;
    }

    /// <summary>A defender more than 1 from its zone (then its locator) walks back to it.</summary>
    private static bool BackToPost(Agent agent)
    {
        if (agent.Owner.Manager?.Places is not { } places)
        {
            return false;
        }
        var here = agent.Owner.Position;
        foreach (var post in new[] { places.ClosestInArea(agent.TargetArea, here), places.Locator(agent.TargetArea) })
        {
            if (post is { } spot && Vector3.DistanceSquared(here with { Y = 0 }, spot with { Y = 0 }) >= 1)
            {
                agent.WayPoint = spot;
                agent.PushState(AiStates.Move);
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Going for the current target (§7.6): while an ability is under way it stands and turns to it; it
/// re-chooses the ability when the one it has is no use or the target is over a second old; in reach
/// and facing it, it uses it (and then chooses its next target); out of reach it moves to a spot
/// around the target, unless it defends. With no target, ability or living visible target, or when it
/// must flee, it gives up.
/// </summary>
internal sealed class AttackAiState : IAiState
{
    public void Enter(Agent agent)
    {
    }

    public void Exit(Agent agent) => agent.Owner.StopAttacking();

    public void Execute(Agent agent, float dt)
    {
        var owner = agent.Owner;
        if (AiStates.MustBreakFree(agent))
        {
            agent.PushState(AiStates.BreakFree);
            return;
        }
        if (agent.CurrentTarget is not { } target)
        {
            GiveUp(agent);
            return;
        }
        if (agent.BusyAbility is { } busy)
        {
            owner.Body.Movement = Vector3.Zero;
            owner.Body.WantedDirection = busy.DesiredDirection(agent);
            return;
        }
        if (agent.Avoidance().Length() > Agent.FleeAvoidance)
        {
            GiveUp(agent);
            return;
        }
        var ability = agent.NextAbility;
        if (ability is null || !ability.IsUseful(agent) || agent.TargetAge > 1)
        {
            agent.ChooseAbility(new FuzzyArguments(owner, target, agent.AlertRadius), out ability);
            agent.NextAbility = ability;
        }
        if (ability is null || target.IsDead || (target is Character { HitPoints: <= 0 }) || (target is Character { IsInvisible: true } && !owner.Template.CanSeeInvisible))
        {
            owner.Body.Movement = Vector3.Zero;
            GiveUp(agent);
            return;
        }
        if (ability.InRange(agent))
        {
            owner.Body.Movement = Vector3.Zero;
            owner.Body.WantedDirection = ability.DesiredDirection(agent);
            if (!ability.FacingTarget(agent))
            {
                return;
            }
            if (ability.Execute(agent))
            {
                agent.StartCooldown(ability);
                agent.ChooseTarget(out var next, out var nextAbility);
                agent.ChangeTarget(next, nextAbility);
            }
            else if (!owner.Attacking)
            {
                ability.ChooseAttackAngle(agent, out var around);
                agent.WayPoint = around;
                agent.PushState(AiStates.Move);
            }
            return;
        }
        if (agent.Order == Order.Defend)
        {
            owner.Body.Movement = Vector3.Zero;
            GiveUp(agent);
            return;
        }
        if (owner.Attacking || owner.Dashing)
        {
            owner.Body.Movement = Vector3.Zero;
            owner.Body.WantedDirection = ability.DesiredDirection(agent);
        }
        else if (ability.ChooseAttackAngle(agent, out var spot))
        {
            agent.WayPoint = spot;
            agent.PushState(AiStates.Move);
        }
        else
        {
            owner.Body.Movement = Vector3.Zero;
            GiveUp(agent);
        }
    }

    private static void GiveUp(Agent agent)
    {
        agent.PopState();
        agent.ReleaseTarget();
    }
}

/// <summary>Struggling against what holds it until free (§7.6).</summary>
internal sealed class BreakFreeAiState : IAiState
{
    public void Enter(Agent agent)
    {
    }

    public void Exit(Agent agent)
    {
    }

    public void Execute(Agent agent, float dt)
    {
        agent.Owner.BreakFree();
        if (!agent.Owner.IsEntangled && !agent.Owner.IsGripped)
        {
            agent.PopState();
        }
    }
}

/// <summary>Running in a panic (§7.6): the wander heading drifts, and it runs along it and ahead (behind, stumbling) until the panic is over.</summary>
internal sealed class PanicAiState : IAiState
{
    public void Enter(Agent agent) => agent.WanderAngle = ((float)agent.Owner.NextRandom() - 0.5f) * MathF.Tau;

    public void Exit(Agent agent) => agent.Owner.Body.Movement = Vector3.Zero;

    public void Execute(Agent agent, float dt)
    {
        var owner = agent.Owner;
        if (!owner.IsPanicking && !owner.IsStumbling)
        {
            agent.PopState();
            return;
        }
        agent.WanderAngle = Agent.WrapAngle(agent.WanderAngle + ((float)owner.NextRandom() - 0.5f) * 60 * dt);
        var drift = new Vector3(MathF.Cos(agent.WanderAngle), 0, MathF.Sin(agent.WanderAngle));
        var ahead = owner.Body.Facing * (owner.Body.RunBackward ? -1.25f : 1.25f);
        owner.Body.Movement = Vector3.Normalize(ahead + drift);
    }
}
