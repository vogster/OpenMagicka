using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Navigation;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.NavMeshTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The AI states: moving, wandering, panicking, struggling free, shying (ENTITIES_AI.md §7.6–7.7).</summary>
public class AiStateTests
{
    /// <summary>A level whose walkable ground is x, z in −10..10 but for a wall along x = 0 from z −10 to 6.</summary>
    private static GameWorld Walled()
    {
        var world = World();
        var wall = Enumerable.Range(0, 16).Select(z => (10, z)).ToArray();
        world.Entities.NavMesh = new NavMesh(Grid(20, 20, origin: new Vector3(-10, 0, -10), holes: wall));
        return world;
    }

    /// <summary>Keeps the mind moving to the point (each move lasts a second, as the original's) until it gets there.</summary>
    private static bool Reach(GameWorld world, Agent mind, Vector3 point, float limit, Action? each = null)
    {
        mind.SetOrder(Order.Idle);
        for (var t = 0f; t < limit; t += Tick)
        {
            if (Vector3.Distance(mind.Owner.Position with { Y = 0 }, point with { Y = 0 }) < 0.6f)
            {
                return true;
            }
            if (mind.State == AiStates.Idle)
            {
                mind.WayPoint = point;
                mind.PushState(AiStates.Move);
            }
            world.Tick(Tick);
            each?.Invoke();
        }
        return false;
    }

    [Fact]
    public void AMove_FollowsTheNavMeshAroundAWall()
    {
        var world = Walled();
        using var _ = world;
        var orc = Animated(world, Orc, at: new Vector3(-5, 0.9f, -5));
        Run(world, Tick);
        var furthest = float.MinValue;

        var reached = Reach(world, orc.Mind!, new Vector3(5, 0, -5), 40, () => furthest = MathF.Max(furthest, orc.Position.Z));

        Assert.True(reached, $"stuck at {orc.Position}");
        Assert.True(furthest > 5, $"round the wall's end, cutting its corner by at most the 0.5 a corner counts as passed within (got to z {furthest})");
    }

    [Fact]
    public void AWanderer_WalksAndPauses_OnTheMesh()
    {
        var world = World();
        using var _ = world;
        world.Entities.NavMesh = new NavMesh(Grid(10, 10, origin: new Vector3(-5, 0, -5)));
        var orc = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        orc.Mind!.SetOrder(Order.Wander);
        Run(world, Tick * 2);
        Assert.Same(AiStates.Wander, orc.Mind.State);
        var start = orc.Position;
        var moved = false;

        for (var t = 0f; t < 25; t += Tick)
        {
            world.Tick(Tick);
            moved |= Vector3.Distance(start, orc.Position) > 2;
            Assert.InRange(orc.Position.X, -5.6f, 5.6f);
            Assert.InRange(orc.Position.Z, -5.6f, 5.6f);
        }

        Assert.True(moved);
        orc.Mind.SetOrder(Order.Idle);
        Run(world, 0.2f);
        Assert.Same(AiStates.Idle, orc.Mind.State);
    }

    [Fact]
    public void APanicOrder_RunsItAbout_UntilAFireBurnsOut()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { MaxPanic = 0.5f }, at: new Vector3(0, 0.9f, 0));
        Run(world, Tick);

        orc.Mind!.SetOrder(Order.Panic);
        orc.OrderPanic();
        Run(world, 0.5f);

        Assert.True(orc.IsPanicking);
        Assert.Same(CharacterStates.Panic, orc.State);
        Assert.Same(AiStates.Panic, orc.Mind.State);
        var start = orc.Position;
        Run(world, 1);
        Assert.True(Vector3.Distance(start, orc.Position) > 1, "runs");
    }

    [Fact]
    public void ABadBurn_Panics_AndItsEndCalms()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { MaxPanic = 0.25f, MaxHitPoints = 100000 }, at: new Vector3(0, 0.9f, 0));
        Run(world, Tick);

        orc.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 10, 2), null, orc.Position);
        Run(world, Tick * 2);
        Assert.True(orc.IsPanicking, "a burn of 2 is past twice 0.25");
        Assert.Same(CharacterStates.Panic, orc.State);

        orc.StopStatuses(StatusEffects.Burning);
        Run(world, 0.2f);
        Assert.False(orc.IsPanicking);
        Assert.NotSame(CharacterStates.Panic, orc.State);
    }

    [Fact]
    public void AStrongMind_StrugglesOutOfVines()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { Ai = Mind() with { BreakFreeStrength = 1 } }, at: new Vector3(0, 0.9f, 0));
        Run(world, Tick);
        orc.Damage(new DamageEntry(AttackProperties.Entanglement, Elements.Earth, 0, 1), null, orc.Position);
        Run(world, Tick);
        Assert.True(orc.IsEntangled);

        Assert.True(RunUntil(world, () => !orc.IsEntangled, 6));
        Assert.Same(AiStates.Idle, orc.Mind!.State);
    }

    [Fact]
    public void Danger_Near_DrivesItAway()
    {
        var world = World();
        using var _ = world;
        world.Entities.NavMesh = new NavMesh(Grid(30, 30, origin: new Vector3(-15, 0, -15)));
        var orc = Animated(world, Orc with { Ai = Mind() with { DangerAvoidance = 100 } }, at: new Vector3(0, 0.9f, 0));
        var shooter = Animated(world, Goblin with { Factions = Factions.Human }, at: new Vector3(14, 0.9f, 14));
        Run(world, Tick);
        var missile = new OpenMagicka.Gameplay.Entities.Missile(world.Physics, shooter, new Vector3(1.5f, 1, 0), Vector3.Zero, 0.2f, [], danger: 5);
        world.Entities.Add(missile);
        Run(world, Tick);

        Assert.True(orc.Mind!.Avoidance().Length() > Agent.FleeAvoidance);
        Run(world, 0.5f);

        Assert.True(orc.Position.X < -0.5f, $"{orc.Position}");
    }

    private static bool RunUntil(GameWorld world, Func<bool> condition, float limit) => MagickCastTests.RunUntil(world, condition, limit);
}
