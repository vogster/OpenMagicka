using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Navigation;

namespace OpenMagicka.Gameplay.AI;

public sealed partial class Agent
{
    /// <summary>How far it looks around for what to shy from (§7.7), and the shying that makes it flee.</summary>
    public const float AvoidanceRadius = 12, FleeAvoidance = 20, MaxAvoidance = 1e5f;

    private readonly List<PathNode> path = [];

    /// <summary>
    /// Where it is going: in an attack, an offset from its target; otherwise a point in the world.
    /// </summary>
    public Vector3 WayPoint { get; set; }

    /// <summary>The path to the way point, corner by corner (the first is where it set out).</summary>
    internal List<PathNode> Path => path;

    /// <summary>The one of its kind it follows as a flock; none when it leads (or goes alone).</summary>
    public Agent? Leader { get; internal set; }

    public bool IsLeader => Leader is null;

    /// <summary>What its kind of walker may cross (its template's movement).</summary>
    public NavMovement MoveAbilities => Owner.Template.MoveAbilities;

    /// <summary>The script's wandering speed (1 by default) and its walk-and-pause rhythm (§7.6).</summary>
    public float WanderSpeed { get; set; } = 1;

    internal float WanderPause { get; set; }

    internal float WanderTimer { get; set; }

    internal bool WanderPaused { get; set; }

    /// <summary>The script's zone or locator it defends or wanders in (0 for none); the level answers where it is.</summary>
    public int TargetArea { get; set; }

    /// <summary>
    /// What it shies from within 12 (§7.7), each kind by its template's weight: its fellows, by
    /// their danger and nearness; its foes, by theirs, and out of their sight; and anything else
    /// dangerous (mines, puddles, missiles). The invisible are not shied from.
    /// </summary>
    public Vector3 Avoidance()
    {
        if (Owner.Manager is not { } manager)
        {
            return Vector3.Zero;
        }
        var (sight, friendly, enemy, danger) = (Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero);
        var here = Owner.Position;
        foreach (var entity in manager.Query(here, AvoidanceRadius))
        {
            if (ReferenceEquals(entity, Owner))
            {
                continue;
            }
            if (entity is Character character)
            {
                if (character.IsInvisible)
                {
                    continue;
                }
                var away = here - character.Position;
                var distance = away.Length();
                if ((character.Factions & Owner.Factions) != 0)
                {
                    if (distance >= 1e-6f)
                    {
                        friendly += away / distance * (1 - distance / AvoidanceRadius) * character.Danger;   // together: no way to shy
                    }
                    continue;
                }
                var nearness = 1f;
                if (distance >= 1e-6f)
                {
                    nearness = 1 - distance / AvoidanceRadius;
                    away /= distance;
                    var facing = character.Body.Facing;
                    var side = Vector3.Cross(away, new Vector3(0, Vector3.Cross(away, facing).Y > 0 ? 1 : -1, 0));
                    side = side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) : Vector3.Zero;   // straight above: no side
                    var reach = distance / AvoidanceRadius;
                    reach = reach * reach * (1 - reach) * 6.75f;                    // a bump peaking at two thirds of the radius
                    var looked = MathF.Pow(Math.Clamp(Vector3.Dot(away, facing), 0, 1), 0.25f);
                    sight += side * looked * reach;
                }
                if (distance >= 1e-6f)
                {
                    enemy += away * nearness * character.Danger;                  // away is the unit way here
                }
                continue;
            }
            var off = (here - entity.Position) with { Y = 0 };
            var length = off.Length();
            var weight = 1 - length / AvoidanceRadius;
            off = length > 1e-6f ? off / length : Vector3.Zero;
            danger += off * weight * entity.Danger;
        }
        var avoidance = sight * Profile.SightAvoidance + friendly * Profile.FriendlyAvoidance + enemy * Profile.EnemyAvoidance + danger * Profile.DangerAvoidance;
        var size = avoidance.Length();
        if (float.IsInfinity(size))
        {
            return Vector3.Zero;
        }
        return size > MaxAvoidance ? avoidance / (size / MaxAvoidance) : avoidance;
    }

    /// <summary>Where it is going now: the way point, from the current target when it has one.</summary>
    internal Vector3 Goal => CurrentTarget is { } target ? WayPoint + target.Position : WayPoint;

    /// <summary>
    /// Steering in a flock (§7.7): the leader's facing (alignment), and up to eight of the group at
    /// random, without repeats, to keep from (separation) and keep to (cohesion), plus a wander; at
    /// most a step long, put on the nav mesh.
    /// </summary>
    internal Vector3 FollowGroup(float dt, IReadOnlyList<Character> group)
    {
        var here = Owner.Position;
        var alignment = Leader!.Owner.Body.Facing * Profile.GroupAlignment;
        var (separation, cohesion) = (Vector3.Zero, Vector3.Zero);
        var picked = new List<int>();
        var count = Math.Min(group.Count, 8);
        while (picked.Count < count)
        {
            var pick = (int)(Owner.NextRandom() * group.Count);
            if (!picked.Contains(pick))
            {
                picked.Add(pick);
            }
        }
        foreach (var index in picked)
        {
            var toward = group[index].Position - here;
            var distance = toward.Length();
            if (distance < 1e-6f)
            {
                continue;                                                     // one on the same spot gives no way
            }
            separation += toward * (Profile.GroupSeparation / -distance);
            cohesion += toward * Profile.GroupCohesion;
        }
        WanderAngle = WrapAngle(WanderAngle + ((float)Owner.NextRandom() - 0.5f) * 30 * dt);
        var wander = Vector3.Normalize(Owner.Body.Facing * 1.25f + new Vector3(MathF.Cos(WanderAngle), 0, MathF.Sin(WanderAngle))) * Profile.GroupWander;
        var way = alignment + separation + cohesion + wander;
        var size = way.LengthSquared();
        if (size > 1)
        {
            way /= MathF.Sqrt(size);
        }
        return OnMesh(here + way, NavMovement.Default) - here;
    }

    /// <summary>The nav mesh's nearest point a walker of these abilities may stand on, or the point.</summary>
    internal Vector3 OnMesh(Vector3 point, NavMovement abilities) => Owner.Manager?.Walkable(point, abilities) ?? point;

    internal static float WrapAngle(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
}
