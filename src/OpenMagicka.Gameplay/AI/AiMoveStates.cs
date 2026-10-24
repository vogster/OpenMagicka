using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Navigation;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// Moving to the way point (§7.6). On entering it looks for a leader: one of its own kind within a
/// third of its alert radius, ahead of it, moving and leading, after the same target (or, with none,
/// a way point within 1 of its own). With a leader it flocks; leading, it finds a path on the nav
/// mesh. Each time it steps along the path (a Jump corner jumps), turns on a solid barrier or a
/// shield in its way, shies from what it avoids, and keeps to the mesh.
/// </summary>
internal sealed class MoveAiState : IAiState
{
    /// <summary>How long it keeps a plan before it thinks again (on a moving part's path, less).</summary>
    public const float Replan = 1, ReplanMoving = 0.333f;

    public void Enter(Agent agent)
    {
        agent.Path.Clear();
        agent.Leader = FindLeader(agent);
        agent.LeaderAge = 0;
        if (agent.IsLeader && agent.Owner.Manager?.NavMesh is { } nav)
        {
            nav.FindPath(agent.Owner.Position, agent.Goal, agent.MoveAbilities, agent.Path);
        }
    }

    public void Exit(Agent agent) => agent.Path.Clear();

    public void Execute(Agent agent, float dt)
    {
        var owner = agent.Owner;
        var path = agent.Path;
        if (!owner.Body.IsTouchingGround)
        {
            agent.PopState();
            return;
        }
        if (agent.StateAge >= (path.Count > 0 && (path[0].Movement & NavMovement.Dynamic) != 0 ? ReplanMoving : Replan))
        {
            agent.PopState();
            return;
        }
        if (agent.CurrentTarget is { } target && (target.IsDead || agent.NextAbility is not { } next || !next.IsUseful(agent)))
        {
            agent.PopState();
            return;
        }
        var here = owner.Position with { Y = 0 };
        var goal = agent.Goal with { Y = 0 };
        var left = Vector3.DistanceSquared(here, goal);
        if (left <= 0.25f || (path.Count == 0 && left > 9 && agent.IsLeader && owner.Manager?.NavMesh is not null))   // without a mesh: straight
        {
            agent.PopState();
            return;
        }
        Vector3 way;
        if (!agent.IsLeader)
        {
            if (Flock(agent, dt, goal) is not { } flocked)
            {
                agent.PopState();
                return;
            }
            way = flocked;
        }
        else
        {
            if (Walk(agent, here, goal) is not { } walked)
            {
                return;                                                       // a jump
            }
            way = walked;
            if (Obstacle(agent, here, way))
            {
                return;
            }
            way *= 0.9f;
        }
        if (agent.Events is { } events && agent.EventIndex < events.Length && events[agent.EventIndex] is MoveAiEvent walk)
        {
            way *= walk.Speed;                                                // a scripted walk's own pace
        }
        owner.Body.Movement = Shy(agent, way);
    }

    private static Agent? FindLeader(Agent agent)
    {
        var owner = agent.Owner;
        Agent? leader = null;
        foreach (var other in owner.Manager?.Query(owner.Position, agent.AlertRadius * 0.333f).OfType<Character>() ?? [])
        {
            if (ReferenceEquals(other, owner) || other.IsPlayer || other.Mind is not { } mind || other.Template != owner.Template
                || mind.State != AiStates.Move || !mind.IsLeader || !ReferenceEquals(mind.CurrentTarget, agent.CurrentTarget))
            {
                continue;
            }
            var toward = other.Position - owner.Position;
            if (!(Vector3.Dot(owner.Body.Facing, Vector3.Normalize(toward)) > 0))
            {
                continue;
            }
            if (agent.CurrentTarget is not null || Vector3.DistanceSquared(agent.WayPoint, mind.WayPoint) <= 1)
            {
                leader = mind;
            }
        }
        return leader;
    }

    /// <summary>Following the leader as a flock; null when the leader stopped moving, is old news, or has arrived.</summary>
    private static Vector3? Flock(Agent agent, float dt, Vector3 goal)
    {
        var leader = agent.Leader!;
        if (leader.State != AiStates.Move || agent.LeaderAge > 1)
        {
            return null;
        }
        var owner = agent.Owner;
        var group = (owner.Manager?.Query(owner.Position, agent.AlertRadius * 0.333f).OfType<Character>() ?? [])
            .Where(c => !ReferenceEquals(c, owner) && !c.IsPlayer && c.Mind is { } mind && c.Template == owner.Template
                && ReferenceEquals(mind.CurrentTarget, agent.CurrentTarget)
                && (agent.CurrentTarget is not null || Vector3.DistanceSquared(mind.WayPoint with { Y = 0 }, goal) <= 1))
            .ToList();
        if (Vector3.DistanceSquared(leader.Owner.Position, leader.Goal) <= 0.25f)
        {
            return null;
        }
        return agent.FollowGroup(dt, group);
    }

    /// <summary>
    /// Along the path: corners passed (within 0.5, or once the next is nearer than this one to it) are
    /// dropped; the way is toward the first left, or the goal. A Jump corner jumps there (null).
    /// </summary>
    private static Vector3? Walk(Agent agent, Vector3 here, Vector3 goal)
    {
        var path = agent.Path;
        if (path.Count == 0)
        {
            return Unit(goal - here);
        }
        var corner = path[0].Position with { Y = 0 };
        var after = (path.Count > 1 ? path[1].Position : goal) with { Y = 0 };
        while (path.Count > 0 && (Vector3.DistanceSquared(here, corner) <= 0.25f
            || Vector3.DistanceSquared(here, after) - Vector3.DistanceSquared(corner, after) < 0))
        {
            path.RemoveAt(0);
            if (path.Count > 1)
            {
                (corner, after) = (after, path[1].Position with { Y = 0 });
            }
            else
            {
                (corner, after) = (goal, goal);
            }
        }
        var way = corner - here;
        if (path.Count > 0 && path[0].Movement == NavMovement.Jump)
        {
            agent.Owner.Jump(way, MathF.PI / 3);
            return null;
        }
        return Unit(way);
    }

    /// <summary>A solid barrier ahead (or a shield across the next 4) is attacked; true when it turned to it.</summary>
    private static bool Obstacle(Agent agent, Vector3 here, Vector3 way)
    {
        var owner = agent.Owner;
        foreach (var entity in owner.Manager?.Query(owner.Position, 4) ?? [])
        {
            if (entity is Barrier { IsSolid: true } barrier)
            {
                var toward = (barrier.Position - here) with { Y = 0 };
                if (toward.LengthSquared() > 0 && Vector3.Dot(Vector3.Normalize(toward), way) > 0.5f)
                {
                    if (agent.CurrentTarget is Barrier)
                    {
                        agent.PopState();
                        return true;
                    }
                    Assault(agent, barrier);
                    return true;
                }
            }
            else if (entity is SpellShield shield && shield.SegmentIntersect(new Entities.Segment(here, owner.Body.Facing * 4), 0.5f))
            {
                Assault(agent, shield);
                return true;
            }
        }
        return false;
    }

    private static void Assault(Agent agent, Entities.Entity obstacle)
    {
        agent.ChooseAbility(new FuzzyArguments(agent.Owner, obstacle, agent.AlertRadius), out var ability);
        agent.AddTarget(obstacle, ability);
        agent.PushState(AiStates.Attack);
    }

    /// <summary>The avoidance, as much as it goes along the way (plus a half), added; then kept to the nav mesh, on the flat.</summary>
    internal static Vector3 Shy(Agent agent, Vector3 way)
    {
        var avoidance = agent.Avoidance();
        var size = avoidance.LengthSquared();
        if (!(size > 1e-6f))
        {
            return way;
        }
        var share = Math.Clamp(Vector3.Dot(way, avoidance / MathF.Sqrt(size)) + 0.5f, 0, 1);
        way += avoidance * share;
        var here = agent.Owner.Position;
        return (agent.OnMesh(here + way, agent.MoveAbilities) - here) with { Y = 0 };
    }

    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
}

/// <summary>
/// Wandering (§7.6): it walks 10–20 s and stands 2–5 s by turns, at the script's speed, along a
/// drifting heading put on the nav mesh; in a zone, it turns back at the edge. It stops when the
/// order changes or it leaves the ground.
/// </summary>
internal sealed class WanderAiState : IAiState
{
    public void Enter(Agent agent)
    {
        agent.WanderPause = 10 + (float)agent.Owner.NextRandom() * 10;
        (agent.WanderTimer, agent.WanderPaused) = (0, false);
    }

    public void Exit(Agent agent)
    {
    }

    public void Execute(Agent agent, float dt)
    {
        var owner = agent.Owner;
        if (!owner.Body.IsTouchingGround || agent.Order != Order.Wander)
        {
            agent.PopState();
            return;
        }
        var speed = Pace(agent, dt);
        agent.WanderAngle = Agent.WrapAngle(agent.WanderAngle + ((float)owner.NextRandom() - 0.5f) * 60 * dt);
        var here = owner.Position with { Y = 0 };
        var way = owner.Body.Facing * 4 + new Vector3(MathF.Cos(agent.WanderAngle), 0, MathF.Sin(agent.WanderAngle));
        var onMesh = agent.OnMesh(here + way, agent.MoveAbilities) with { Y = 0 };
        if (Vector3.DistanceSquared(here, onMesh) > owner.Radius * owner.Radius)
        {
            way = onMesh - here;
        }
        if (owner.Manager?.Places?.ClosestInArea(agent.TargetArea, here + way) is { } edge)
        {
            var inside = edge with { Y = 0 };
            if (Vector3.DistanceSquared(here, inside) > owner.Radius * owner.Radius)
            {
                way = inside - here;
            }
            else
            {
                agent.WanderAngle = Agent.WrapAngle(agent.WanderAngle + MathF.PI);   // at the edge: about turn
                way = owner.Body.Facing * 1.5f + new Vector3(MathF.Cos(agent.WanderAngle), 0, MathF.Sin(agent.WanderAngle));
            }
        }
        owner.Body.Movement = way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) * speed : Vector3.Zero;
    }

    /// <summary>The walk-and-pause rhythm: the script's speed while walking, nothing while standing.</summary>
    private static float Pace(Agent agent, float dt)
    {
        if (agent.WanderPaused)
        {
            agent.WanderTimer -= dt;
            if (agent.WanderTimer <= 0)
            {
                agent.WanderPaused = false;
            }
            return 0;
        }
        agent.WanderTimer += dt;
        if (agent.WanderTimer >= agent.WanderPause)
        {
            (agent.WanderTimer, agent.WanderPaused) = (2 + 3 * (float)agent.Owner.NextRandom(), true);
            return 0;
        }
        return agent.WanderSpeed;
    }
}
