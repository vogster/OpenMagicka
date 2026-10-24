using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>What the animation actions do to a character (ENTITIES_AI.md §4.2).</summary>
public class AnimationActionTests
{
    internal const string Special = "special0";

    /// <summary>A goblin standing idle, whose <see cref="Special"/> clip carries the actions.</summary>
    internal static Character Acting(GameWorld world, float duration, params AnimationAction[] actions) =>
        Acting(world, Goblin, duration, actions);

    internal static Character Acting(GameWorld world, CharacterTemplate template, float duration, params AnimationAction[] actions)
    {
        var goblin = Animated(world, template, clips: ClipsWith([new CharacterAnimation(Special, new AnimationClip(Special, duration), 1, 0, false, actions)]));
        Run(world, 0.2f);
        return goblin;
    }

    private static DamageEntry Status(Elements element) => new(AttackProperties.Status, element, 0, 1);

    [Fact]
    public void Move_PushesTheBodyAlongItsFacing()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Move(0, 1, new Vector3(0, 0, -5)));

        goblin.ForceAnimation(Special);
        Run(world, 0.6f);

        Assert.True(goblin.Position.Z < -1.5f, $"z {goblin.Position.Z}");        // facing −Z: forward
        Assert.Equal(0, goblin.Position.X, 1);
    }

    [Fact]
    public void Jump_LeapsAtTheTarget_ByTheSquaredDistanceClamped()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Jump(0, 0, 60, 10, 16));
        var target = Animated(world, at: new Vector3(0, 0.9f, -3));
        goblin.Target = target;

        goblin.ForceAnimation(Special);
        Run(world, Tick * 3);
        Assert.True(goblin.Body.IsJumping);

        Run(world, 4);
        Assert.InRange(goblin.Position.Z, -11.5f, -8.5f);                        // 3 m away: 9 × 1.1 = 9.9, raised to the 10 m minimum
    }

    [Fact]
    public void Jump_IsForNonPlayersOnly()
    {
        using var world = World();
        var wizard = Acting(world, 1, new Jump(0, 0, 60, 10, 16));
        wizard.IsPlayer = true;

        wizard.ForceAnimation(Special);
        Run(world, 0.3f);

        Assert.False(wizard.Body.IsJumping);
    }

    [Fact]
    public void Crouch_LowersTheCapsule_ForItsWindow()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Crouch(0, 0.5f, 0.5f, 0));

        goblin.ForceAnimation(Special);
        Run(world, 0.2f);
        Assert.Equal((0.5f, 0.01f), (goblin.Body.Shape.Radius, goblin.Body.Shape.Length));
        Assert.Equal(0, goblin.Position.Y + goblin.Body.HeightOffset, 1);

        Run(world, 0.6f);
        Assert.Equal((0.4f, 1f), (goblin.Body.Shape.Radius, goblin.Body.Shape.Length));
        Assert.Equal(0, goblin.Position.Y + goblin.Body.HeightOffset, 1);
    }

    [Fact]
    public void Ethereal_EndsStatuses_PassesThrough_AndFades()
    {
        using var world = World();
        var ghost = Acting(world, 1, new Ethereal(0, 0, true, 0.2f, 2));
        ghost.Damage(Status(Elements.Water), null, Vector3.Zero);
        Assert.True(ghost.Statuses.Has(StatusEffects.Wet));

        ghost.ForceAnimation(Special);
        Run(world, Tick * 2);
        Assert.True(ghost.IsEthereal);
        Assert.False(ghost.Statuses.Has(StatusEffects.Wet));
        Assert.True(ghost.Body.Conditions.IsIntangible);

        Run(world, 3);
        Assert.Equal(0.2f, ghost.EtherealAlpha, 2);
    }

    [Fact]
    public void Ethereal_WithNoSpeed_OnlySetsTheTransparency()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Ethereal(0, 0, true, 0.5f, 0));

        goblin.ForceAnimation(Special);
        Run(world, Tick * 2);

        Assert.False(goblin.IsEthereal);
        Assert.Equal(0.5f, goblin.EtherealAlpha, 3);
    }

    [Fact]
    public void Invisible_LastsTheWindow_AndDamageEndsIt()
    {
        using var world = World();
        var rogue = Acting(world, 1, new Invisible(0, 0.5f, false));

        rogue.ForceAnimation(Special);
        Run(world, 0.2f);
        Assert.True(rogue.IsInvisible);
        Run(world, 0.5f);
        Assert.False(rogue.IsInvisible);

        rogue.SetInvisible(10);
        rogue.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 1, 1), null, Vector3.Zero);
        Assert.False(rogue.IsInvisible);
    }

    [Fact]
    public void Invisible_WithNoEffect_OnlyHides()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Invisible(0, 0.5f, true));

        goblin.ForceAnimation(Special);
        Run(world, 0.2f);
        Assert.True(goblin.IsHidden);
        Assert.False(goblin.IsInvisible);
        Run(world, 0.5f);
        Assert.False(goblin.IsHidden);
    }

    [Fact]
    public void Immortal_HoldsForTheWindow_ThenEndsAtOnce()
    {
        using var world = World();
        var slime = Acting(world, 1, new Immortal(0, 0.5f, false));

        slime.ForceAnimation(Special);
        Run(world, 0.2f);
        Assert.True(slime.IsImmortal);
        Assert.True(slime.IsIntangible);
        slime.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, Vector3.Zero);
        Assert.Equal(300f, slime.HitPoints);

        Run(world, 0.5f);
        Assert.False(slime.IsImmortal);
        Assert.False(slime.IsIntangible);
    }

    [Fact]
    public void RemoveStatus_EndsIt()
    {
        using var world = World();
        var orc = Acting(world, 1, new RemoveStatus(0, 0, StatusEffects.Wet));
        orc.Damage(Status(Elements.Water), null, Vector3.Zero);

        orc.ForceAnimation(Special);
        Run(world, Tick * 2);

        Assert.False(orc.Statuses.Has(StatusEffects.Wet));
    }

    [Fact]
    public void Suicide_DiesOnTheSpot()
    {
        using var world = World();
        var bomber = Acting(world, 1, new Suicide(0, 0, false));
        var slime = Acting(world, 1, new Suicide(0, 0, true));

        bomber.ForceAnimation(Special);
        slime.ForceAnimation(Special);
        Run(world, Tick * 2);

        Assert.True(bomber.IsDead);                                               // no death clip to wait for
        Assert.False(bomber.IsOverkilled);
        Assert.True(slime.IsDead && slime.IsOverkilled);
        Run(world, Tick);
        Assert.Same(CharacterStates.Dead, bomber.State);                          // the state machine follows the death
    }

    [Fact]
    public void Block_BlocksOnlyInItsWindow()
    {
        using var world = World();
        var knight = Acting(world, 1, new Block(0, 0.5f, 0));
        EquipmentTests.Shield(knight, 1000);

        Assert.False(Hit(knight).HasFlag(DamageResult.Deflected));               // a blocker, but no block window
        knight.ForceAnimation(Special);
        Run(world, 0.2f);
        Assert.Equal(0, knight.BlockSlot);
        Assert.True(Hit(knight).HasFlag(DamageResult.Deflected));

        Run(world, 0.5f);
        Assert.Equal(-1, knight.BlockSlot);
        Assert.False(Hit(knight).HasFlag(DamageResult.Deflected));
    }

    [Fact]
    public void BreakFree_TearsAtTheVines()
    {
        using var world = World();
        var cultist = Acting(world, 1, new BreakFree(0, 0, 1));
        cultist.Entangle(5);
        var before = cultist.Entanglement;

        cultist.ForceAnimation(Special);
        Run(world, Tick * 2);

        Assert.True(cultist.Entanglement < before);
    }

    [Fact]
    public void Attacking_MakesTheCharacterSolidAndVisible()
    {
        using var world = World();
        var ghost = Acting(world, 1);
        ghost.Ethereal(true, 0.2f, 1);
        ghost.SetInvisible(10);

        (ghost.Attacking, ghost.NextAttackAnimation) = (true, "attack_melee0");
        Run(world, 0.1f);

        Assert.Same(CharacterStates.Attack, ghost.State);
        Assert.False(ghost.IsEthereal);
        Assert.False(ghost.IsInvisible);
    }

    private static DamageResult Hit(Character character) =>
        character.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1), null, character.Position - Vector3.UnitZ);
}
