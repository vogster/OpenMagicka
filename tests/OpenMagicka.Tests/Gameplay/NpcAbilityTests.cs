using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>What NPCs do with their abilities (ENTITIES_AI.md §7.8).</summary>
public class NpcAbilityTests
{
    private static readonly CharacterTemplate Human = Goblin with { Id = "human", Factions = Factions.Human, MaxHitPoints = 100000 };

    private static CharacterAnimations NpcClips() => ClipsWith([
        new("cast_spell0", new AnimationClip("cast_spell0", 0.6f), 1, 0, false, [new OpenMagicka.Gameplay.Animation.Actions.CastSpell(0.05f, 0.5f, null)]),
        new("steal0", new AnimationClip("steal0", 0.5f), 1, 0, false, []),
    ]);

    private static Character Npc(GameWorld world, AbilityTemplate ability, Vector3 at) =>
        Animated(world, Orc with { Ai = Mind(ability) }, at: at, clips: NpcClips());

    private static MeleeAbility Melee(float cooldown = 1) => new(cooldown, AbilityTarget.Enemy, null, ["attack_melee0"], 0, 1.2f, 1, [0], false);

    [Fact]
    public void AMeleeFighter_StrikesAFoeInReach_AndItsCooldownRuns()
    {
        var world = World();
        using var _ = world;
        var orc = Npc(world, Melee(), new Vector3(0, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(1.2f, 0.9f, 0));

        Assert.True(RunUntil(world, () => orc.State == CharacterStates.Attack, 2), $"{orc.Mind!.State} {orc.State}");

        Assert.Same(human, orc.Mind!.CurrentTarget);
        Assert.True(orc.Mind.Cooldown(0) > 0);
        Assert.True(RunUntil(world, () => orc.Mind.BusyAbility is null, 2));
    }

    [Fact]
    public void AFighterOutOfReach_ClosesIn_ThenStrikes()
    {
        var world = World();
        using var _ = world;
        var orc = Npc(world, Melee(), new Vector3(0, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(6, 0.9f, 0));

        Assert.True(RunUntil(world, () => orc.State == CharacterStates.Attack, 8), $"{orc.Mind!.State} {orc.State} at {orc.Position}");

        Assert.True(Vector3.Distance(orc.Position with { Y = 0 }, human.Position with { Y = 0 }) < 2.5f);
    }

    [Fact]
    public void ACaster_ChantsItsElements_ThenCasts()
    {
        var world = World();
        using var _ = world;
        var fireball = new CastSpellAbility(3, AbilityTarget.Enemy, FuzzyExpression.Parse("1"), ["cast_spell0"], 0, 10, 1, 0.2f, 1, CastType.Force,
            [Elements.Fire, Elements.Fire]);
        var orc = Npc(world, fireball, new Vector3(0, 0.9f, 0));
        Animated(world, Human, at: new Vector3(5, 0.9f, 0));

        Assert.True(RunUntil(world, () => orc.SpellQueue.Count == 2, 3), $"chanted {orc.SpellQueue.Count}");
        Assert.True(RunUntil(world, () => orc.CurrentSpell is not null || orc.State == CharacterStates.Cast, 3), $"{orc.State}");

        Assert.Equal(CastType.Force, orc.CastType);
        Assert.True(orc.Mind!.Cooldown(0) > 0);
    }

    [Fact]
    public void AJump_IsForWizardsOnly()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var leap = new JumpAbility(1, AbilityTarget.Enemy, null, ["move_jump_begin"], 2, 8, 1, 0.6f);
        var orc = Npc(world, leap, wizard.Position + new Vector3(5, 0, 0));
        var human = Animated(world, Human, at: wizard.Position + new Vector3(5, 0, 5));
        Run(world, Tick);

        var ability = orc.Mind!.Abilities[0];
        Assert.Equal(float.MaxValue, ability.Desirability(orc.Mind, new FuzzyArguments(orc, wizard, 10)));
        Assert.Equal(float.MinValue, ability.Desirability(orc.Mind, new FuzzyArguments(orc, human, 10)));
    }

    [Fact]
    public void AnElementThief_EmptiesTheFoesQueue_OnceItsClipPlays()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var steal = new ElementStealAbility(5, AbilityTarget.Enemy, FuzzyExpression.Parse("t.isc"), ["steal0"], 4, 0);
        var orc = Npc(world, steal, wizard.Position + new Vector3(2, 0, 0));
        Conjure(wizard, Elements.Fire, Elements.Water);

        Assert.True(RunUntil(world, () => wizard.SpellQueue.Count == 0, 3));
    }

    [Fact]
    public void AGuard_IsUpForASecond()
    {
        var world = World();
        using var _ = world;
        var guard = new BlockAbility(5, AbilityTarget.Enemy, FuzzyExpression.Parse("1"), ["block"], 3, 0);
        var orc = Npc(world, guard, new Vector3(0, 0.9f, 0));
        Animated(world, Human, at: new Vector3(3, 0.9f, 0));

        Assert.True(RunUntil(world, () => orc.IsBlocking, 2));
        Assert.True(RunUntil(world, () => !orc.IsBlocking, 1.3f));
    }

    [Fact]
    public void AnArcher_HoldsItsShot_WithAFellowOnTheLine_AndShootsOnceClear()
    {
        var world = World();
        using var _ = world;
        var bow = new RangedAbility(1, AbilityTarget.Enemy, FuzzyExpression.Parse("1"), ["attack_melee0"], 0, 20, 0, MathF.PI, 1, []);
        var orc = Npc(world, bow, new Vector3(0, 0.9f, 0));
        var fellow = Animated(world, Orc, at: new Vector3(4, 0.9f, 0));
        var human = Animated(world, Human, at: new Vector3(8, 0.9f, 0));
        orc.Mind!.SetOrder(Order.Idle);
        Run(world, Tick);
        orc.Mind.AddTarget(human, orc.Mind.Abilities[0]);

        Assert.False(orc.Mind.Abilities[0].Execute(orc.Mind));

        fellow.Body.Body.Position += new Vector3(0, 0, 5);
        Run(world, Tick);
        Assert.True(orc.Mind.Abilities[0].Execute(orc.Mind));
    }

    [Fact]
    public void ALeapNoSpeedMakes_IsNotUnderWay()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var flat = new JumpAbility(1, AbilityTarget.Enemy, null, ["move_jump_begin"], 2, 8, 1, Elevation: 0);   // no arc reaches level ground
        var orc = Npc(world, flat, wizard.Position + new Vector3(5, 0, 0));
        Run(world, Tick);
        orc.Mind!.SetOrder(Order.Idle);
        orc.Mind.AddTarget(wizard, orc.Mind.Abilities[0]);

        Assert.False(orc.Mind.Abilities[0].Execute(orc.Mind));

        Assert.Null(orc.Mind.BusyAbility);
    }

    [Fact]
    public void TwoOnOneSpot_StillShyAndFlock()
    {
        var world = World();
        using var _ = world;
        var orc = Animated(world, Orc with { Ai = Mind() with { FriendlyAvoidance = 1, GroupSeparation = 1, GroupCohesion = 1 } }, at: new Vector3(0, 0.9f, 0));
        var twin = Animated(world, Orc, at: new Vector3(0, 0.9f, 0));
        Run(world, Tick);
        twin.Body.Body.Position = orc.Body.Body.Position;
        twin.Conjure(Elements.Fire);
        Run(world, Tick);
        twin.Body.Body.Position = orc.Body.Body.Position;

        var shy = orc.Mind!.Avoidance();

        Assert.True(float.IsFinite(shy.X + shy.Y + shy.Z), $"{shy}");
    }
}
