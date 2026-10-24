using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The NPC mind: thinking in turn, grudges, reactions and the choice of target and ability (ENTITIES_AI.md §7.1–7.4).</summary>
public class AgentTests
{
    internal static AiProfile Mind(params AbilityTemplate[] abilities) =>
        new(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, AngerWeight: 1, DistanceWeight: 1, HealthWeight: 1, Flocking: false, BreakFreeStrength: 0, abilities);

    internal static readonly CharacterTemplate Orc = Goblin with { Id = "orc", Factions = Factions.Wild, Ai = Mind() };

    private static readonly CharacterTemplate Human = Goblin with { Id = "human", Factions = Factions.Human };

    private static DamageEntry Blow(float amount) => new(AttackProperties.Damage, Elements.Earth, amount, 1);

    private static CastSpellAbility Spell(string desire, params Elements[] elements) =>
        new(0, AbilityTarget.Enemy, FuzzyExpression.Parse(desire), ["cast_spell0"], 0, 10, 1, 0.2f, 1, CastType.Force, elements);

    /// <summary>Counts how often, and over how long, a mind thinks.</summary>
    private sealed class Counting : IAiState
    {
        public int Times;
        public float Spent;

        public void Enter(Agent agent)
        {
        }

        public void Execute(Agent agent, float dt) => (Times, Spent) = (Times + 1, Spent + dt);

        public void Exit(Agent agent)
        {
        }
    }

    [Fact]
    public void TwentyMindsThinkAFrame_InTurn_EachWithTheTimeItGathered()
    {
        var world = World();
        using var _ = world;
        var states = new List<Counting>();
        for (var i = 0; i < 25; i++)
        {
            var orc = Animated(world, Orc, at: new Vector3(i * 2 - 25, 0.9f, 10));
            var counting = new Counting();
            orc.Mind!.PushState(counting);
            states.Add(counting);
        }

        world.Tick(Tick);
        Assert.Equal(20, states.Sum(s => s.Times));
        for (var i = 0; i < 9; i++)
        {
            world.Tick(Tick);
        }

        Assert.Equal(200, states.Sum(s => s.Times));
        Assert.All(states, s => Assert.InRange(s.Times, 7, 9));
        Assert.All(states, s => Assert.True(s.Spent <= 10 * Tick + 1e-4f && s.Spent >= 7 * Tick - 1e-4f, $"{s.Spent}"));
    }

    [Fact]
    public void AForeignBlow_BecomesAGrudge_ThatFades_AndAFellowsDoesNot()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { MaxHitPoints = 100000 }, at: new Vector3(0, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(3, 0.9f, 0));
        var fellow = Animated(world, Orc, at: new Vector3(-3, 0.9f, 0));

        orc.Damage(Blow(40), human, human.Position);
        orc.Damage(Blow(40), fellow, fellow.Position);
        Assert.Equal(40, orc.Mind!.GrudgeAgainst(human));
        Assert.Equal(0, orc.Mind.GrudgeAgainst(fellow));

        Run(world, 1);
        Assert.InRange(orc.Mind.GrudgeAgainst(human), 13, 17);                          // 25 a second
        orc.Damage(Blow(1000), human, human.Position);
        Run(world, Tick * 2);
        Assert.Equal(Agent.MaxGrudge, orc.Mind.GrudgeAgainst(human), 0);
        Run(world, 25);
        Assert.Empty(orc.Mind.Grudges);
    }

    [Fact]
    public void AWizardsBlow_IsHeldAgainstItByTheVictimsFellows_AndTwiceByTheVictim()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var victim = Animated(world, Orc, at: wizard.Position + new Vector3(5, 0, 0));
        var near = Animated(world, Orc, at: wizard.Position + new Vector3(8, 0, 0));
        var far = Animated(world, Orc, at: wizard.Position + new Vector3(30, 0, 0));
        Run(world, Tick);                                                               // the grid knows them

        victim.Damage(Blow(30), wizard, wizard.Position);

        Assert.Equal(60, victim.Mind!.GrudgeAgainst(wizard));                          // as the original: once its own, once as a fellow
        Assert.Equal(30, near.Mind!.GrudgeAgainst(wizard));
        Assert.Equal(0, far.Mind!.GrudgeAgainst(wizard));
    }

    [Fact]
    public void AFoeNear_SetsOffTheReaction_WhenItReactsToNearness()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        var triggered = 0;
        orc.Mind!.Triggered += (_, trigger) => triggered = trigger;
        orc.Mind.SetOrder(Order.Idle, ReactTo.Proximity, Order.Attack, reactionTrigger: 77);
        Run(world, 0.2f);
        Assert.Equal(Order.Idle, orc.Mind.Order);

        Animated(world, Human, at: new Vector3(5, 0.9f, 0));
        Run(world, 0.2f);

        Assert.Equal(Order.Attack, orc.Mind.Order);
        Assert.Equal(77, triggered);
        Assert.Same(AiStates.Idle, orc.Mind.State);
    }

    [Fact]
    public void TheTarget_IsTheFoeItIsAngriestWith_AndTheAbilityTheMostWanted()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { Ai = Mind(Spell("distExp(0,20)", Elements.Fire), Spell("0.01", Elements.Cold)) }, at: new Vector3(0, 0.9f, 0));
        var calm = Animated(world, Human, at: new Vector3(4, 0.9f, 0));
        var hated = Animated(world, Human, at: new Vector3(6, 0.9f, 0));
        orc.Damage(Blow(200), hated, hated.Position);
        Run(world, Tick);

        var score = orc.Mind!.ChooseTarget(out var target, out var ability);

        Assert.Same(hated, target);
        Assert.Equal(0, ability!.Index);
        Assert.True(score > 1);
        Assert.NotSame(calm, target);
    }

    [Fact]
    public void WithElementsChanted_OnlyTheirSpellIsWanted_OrElseARecovery()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { Ai = Mind(Spell("1", Elements.Fire, Elements.Fire), Spell("0.5", Elements.Cold)) }, at: new Vector3(0, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(4, 0.9f, 0));
        Run(world, Tick);
        orc.Mind!.ChooseTarget(out var _, out var first);
        Assert.Equal(0, first!.Index);

        orc.Conjure(Elements.Cold);
        orc.Mind.ChooseTarget(out var _, out var matching);
        Assert.Equal(1, matching!.Index);

        var other = Animated(world, Orc with { Ai = orc.Template.Ai }, at: new Vector3(0, 0.9f, -4));
        Run(world, Tick);
        other.Mind!.ChooseTarget(out var _, out var _);
        other.Conjure(Elements.Earth);                                                  // begins neither spell
        var score = other.Mind.ChooseAbility(new FuzzyArguments(other, human, 10), out var recovery);
        Assert.IsType<SpellRecovery>(recovery);
        Assert.True(score > 0 && score < 1e-40f);
    }

    [Fact]
    public void ANewOrder_DropsTheStatesToIdle()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(4, 0.9f, 0));
        orc.Mind!.AddTarget(human, null);
        orc.Mind.PushState(AiStates.Attack);
        orc.Mind.PushState(new Counting());

        orc.Mind.SetOrder(Order.Attack);
        Assert.NotSame(AiStates.Idle, orc.Mind.State);                                  // the same order keeps them
        orc.Mind.SetOrder(Order.Wander);

        Assert.Same(AiStates.Idle, orc.Mind.State);
        Assert.Null(orc.Mind.CurrentTarget);
    }

    [Fact]
    public void AWizard_HasNoMindToSpeakOf_AndAPlainTemplateNone()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.Null(wizard.Mind);
        Assert.Null(Animated(world, Goblin).Mind);
        Assert.NotNull(Animated(world, Orc).Mind);
    }
}
