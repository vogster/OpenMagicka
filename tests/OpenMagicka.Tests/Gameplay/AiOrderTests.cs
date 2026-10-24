using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Script orders and their events, charms and summons (ENTITIES_AI.md §7.6, §7.10; MAGICKS.md §6.2, §7).</summary>
public class AiOrderTests
{
    [Fact]
    public void TheEvents_WalkPlayAndTurn_FiringTheirTriggers_ThenLoop()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        var fired = new List<int>();
        orc.Mind!.Triggered += (_, trigger) => fired.Add(trigger);
        Run(world, Tick);

        orc.Mind.SetOrder(Order.Idle, events: [
            new MoveAiEvent(11, 0, new Vector3(3, 0, 0)),
            new AnimationAiEvent(22, 0.2f, "spec_alert0"),
            new FaceAiEvent(33, 0, Direction: Vector3.UnitZ),
            new LoopAiEvent(0, 0),
        ]);

        Assert.True(RunUntil(world, () => fired.Contains(11), 6), $"at {orc.Position}");
        Assert.InRange(orc.Position.X, 2.4f, 3.6f);
        Assert.True(RunUntil(world, () => fired.Contains(22), 2));
        Assert.True(RunUntil(world, () => fired.Contains(33), 3), $"facing {orc.Body.Facing}");
        Assert.True(Vector3.Dot(orc.Body.Facing, Vector3.UnitZ) > 0.98f);
        fired.Clear();
        Assert.True(RunUntil(world, () => fired.Contains(11), 3), "loops back to the first");
    }

    [Fact]
    public void AnAnimationWithNoDelay_WaitsForItsClip()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        var fired = new List<int>();
        orc.Mind!.Triggered += (_, trigger) => fired.Add(trigger);
        Run(world, Tick);

        orc.Mind.SetOrder(Order.Idle, events: [new AnimationAiEvent(5, 0, "spec_alert0"), new KillAiEvent(0, 0, Remove: false)]);
        Run(world, 0.3f);
        Assert.Empty(fired);                                                            // the clip lasts 0.5 s

        Assert.True(RunUntil(world, () => orc.IsDead, 2));
        Assert.Equal([5], fired);
    }

    [Fact]
    public void APanicOrder_PanicsAtOnce_OnlyWhenGivenByName()
    {
        var world = World();
        using var _ = world;
        var named = Animated(world, Orc with { MaxPanic = 0.5f }, at: new Vector3(0, 0.9f, 0));
        var zoned = Animated(world, Orc with { MaxPanic = 0.5f }, at: new Vector3(5, 0.9f, 0));
        Run(world, Tick);

        named.Mind!.Give(new GiveOrderSpec(Order.Panic, WanderSpeed: 0.5f), byName: true);
        zoned.Mind!.Give(new GiveOrderSpec(Order.Panic), byName: false);
        Run(world, 0.2f);

        Assert.True(named.IsPanicking);
        Assert.Equal(0.5f, named.Mind.WanderSpeed);
        Assert.False(zoned.IsPanicking);                                                 // as the original: a zone's order sets the order alone
    }

    [Fact]
    public void ACharmedNpc_TurnsFriendly_AndStartsOver()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var orc = Animated(world, Orc, at: wizard.Position + new Vector3(4, 0, 0));
        var human = Animated(world, Goblin with { Factions = Factions.Human }, at: wizard.Position + new Vector3(8, 0, 0));
        Run(world, Tick);
        orc.Mind!.AddTarget(human, null);
        orc.Mind.PushState(AiStates.Attack);

        orc.Charm(wizard, 10);

        Assert.Equal(Factions.Friendly, orc.Factions);
        Assert.Same(AiStates.Idle, orc.Mind.State);
        Assert.Null(orc.Mind.CurrentTarget);
    }

    [Fact]
    public void Zombies_AreRaisedToAttack_AndNoticeWithinTwelve()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        world.Entities.Bestiary = new Bestiary().Add(UndeadRising.Asset, Orc with { Id = "zombie", Factions = Factions.Undead }, Clips());
        Assert.True(MagickEffects.For(MagickType.SUndead)!.Execute(wizard));
        Run(world, Tick);

        var zombie = Assert.Single(world.Entities.Entities.OfType<Character>(), c => c.SummonMaster == wizard);

        Assert.Equal(Order.Attack, zombie.Mind!.Order);
        Assert.Equal(ReactTo.Attack | ReactTo.Proximity, zombie.Mind.ReactsTo);
        Assert.Equal(12, zombie.Mind.AlertRadius);
    }
}
