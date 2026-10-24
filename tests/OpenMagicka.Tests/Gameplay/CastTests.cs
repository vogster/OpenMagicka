using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Physics;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Casting in the world, and the push (SPELLS.md §4.1, §5.1).</summary>
public class CastTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static readonly ItemTemplate Staff = new("staff", WeaponClass.Staff);

    private static readonly ItemTemplate Sword = new("sword", WeaponClass.SlashMedium);

    /// <summary>A cast clip: its CastSpell action at 0.05 s.</summary>
    private static CharacterAnimation CastClip(string name, float duration = 0.6f) =>
        new(name, new AnimationClip(name, duration), 1, 0, false, [new Actions.CastSpell(0.05f, 0.5f, null)]);

    private static CharacterAnimation Clip(string name, float duration, bool loop = false) => new(name, new AnimationClip(name, duration), 1, 0, loop, []);

    private static CharacterAnimations Casting() => new(ClipsWith([
        CastClip("cast_force_push"), CastClip("cast_area_push"), CastClip("attack_cast"), CastClip("cast_self"), CastClip("cast_sword"), CastClip("cast_sword_spray"), CastClip("cast_force_spray"),
        Clip("charge_force", 0.5f), Clip("charge_force_loop", 1, loop: true), Clip("charge_area", 0.5f), Clip("charge_area_loop", 1, loop: true),
    ]).Sets, SkeletonTests.Arm());

    private static Character Wizard(GameWorld world, Vector3? at = null)
    {
        var wizard = Animated(world, at: at, clips: Casting());
        wizard.IsPlayer = true;
        wizard.Equip(1, new Item(Staff), "hand");
        wizard.Equip(0, new Item(Sword), "hand");
        Run(world, 0.05f);
        return wizard;
    }

    /// <summary>Runs until the condition holds (the cast clip's blend and action windows decide when); false if it never did.</summary>
    private static bool RunUntil(GameWorld world, Func<bool> condition, float limit = 1.5f)
    {
        for (var t = 0f; t < limit; t += Tick)
        {
            if (condition())
            {
                return true;
            }
            world.Tick(Tick);
        }
        return condition();
    }

    private static void ChargeAndRelease(GameWorld world, Character wizard, CastType cast, float charge)
    {
        Assert.True(wizard.PressCast(cast));
        Run(world, charge);
        wizard.ReleaseCast(cast);
    }

    [Fact]
    public void PressingForce_CombinesTheQueue_AndHoldsTheButton()
    {
        using var world = World();
        var wizard = Wizard(world);
        wizard.Conjure(Elements.Fire);
        wizard.Conjure(Elements.Fire);

        Assert.True(wizard.PressCast(CastType.Force));

        Assert.Equal((CastType.Force, CastType.Force, 0), (wizard.CastType, wizard.HeldCast, wizard.SpellQueue.Count));
        Assert.Equal(2f, wizard.Spell.Fire);
        Assert.False(wizard.PressCast(CastType.Area));                              // one button at a time
    }

    [Fact]
    public void WithoutAStaffInSlotOne_ForceAndAreaDoNotCast()
    {
        using var world = World();
        var wizard = Animated(world, clips: Casting());
        wizard.IsPlayer = true;

        Assert.False(wizard.PressCast(CastType.Force));
        Assert.Equal(CastType.None, wizard.CastType);
    }

    [Fact]
    public void AChargedForcePush_ThrowsWhatIsInFront_ByItsCharge()
    {
        using var world = World();
        var wizard = Wizard(world);
        var ahead = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, 3));

        Assert.True(wizard.PressCast(CastType.Force));
        Run(world, Tick);
        Assert.Same(CharacterStates.Charge, wizard.State);
        Assert.False(wizard.ChargeUnlocked);
        Run(world, 0.25f);
        Assert.InRange(wizard.SpellPower, 0.25f, 0.55f);                            // about half the charge clip, its blend aside
        wizard.ReleaseCast(CastType.Force);
        Run(world, Tick);
        Assert.Same(CharacterStates.Cast, wizard.State);
        Assert.True(wizard.ChargeUnlocked);
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is not null));

        var push = Assert.IsType<PushEffect>(wizard.CurrentSpell);
        Assert.Equal(0f, wizard.SpellPower);                                        // spent on the push
        Run(world, 0.2f);
        Assert.Contains(ahead, push.HitList);
        Assert.DoesNotContain(behind, push.HitList);
        Assert.True(ahead.Body.IsPushed || ahead.Position.Z < -3.2f, $"ahead at {ahead.Position}");
        Assert.Equal(300f, ahead.HitPoints);                                         // a push does not hurt
    }

    [Fact]
    public void AnAreaPush_ReachesAllAroundWithinItsRadius_OnceEach()
    {
        using var world = World();
        var wizard = Wizard(world);
        var near = new[] { new Vector3(2, 0.9f, 0), new Vector3(-2, 0.9f, 0), new Vector3(0, 0.9f, 2) }.Select(p => Animated(world, Foe, at: p)).ToList();
        var far = Animated(world, Foe, at: new Vector3(6, 0.9f, 0));

        ChargeAndRelease(world, wizard, CastType.Area, Tick);
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is not null));
        var push = Assert.IsType<PushEffect>(wizard.CurrentSpell);
        Run(world, 0.3f);

        Assert.All(near, n => Assert.Single(push.HitList, e => ReferenceEquals(e, n)));
        Assert.DoesNotContain(far, push.HitList);
    }

    [Fact]
    public void APush_NeedsALineOfSight()
    {
        using var world = World();
        Vector3[] wall = [new(-3, 0, -1.5f), new(3, 0, -1.5f), new(3, 3, -1.5f), new(-3, 3, -1.5f)];
        world.Physics.AddStaticMesh(1, wall, [new MeshTriangle(0, 1, 2), new MeshTriangle(0, 2, 3), new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);
        var wizard = Wizard(world);
        var hidden = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));

        ChargeAndRelease(world, wizard, CastType.Force, Tick);
        Run(world, 0.4f);

        Assert.False(hidden.Body.IsPushed);
        Assert.InRange(hidden.Position.Z, -3.1f, -2.9f);
    }

    [Fact]
    public void APush_ThrowsAMissile_WithoutHurtingIt()
    {
        using var world = World();
        var wizard = Wizard(world);
        var rock = new Missile(world.Physics, null, new Vector3(0, 1, -3), Vector3.Zero, 0.2f, []);
        world.Entities.Add(rock);
        rock.Start();

        ChargeAndRelease(world, wizard, CastType.Force, 0.5f);
        Run(world, 0.2f);

        Assert.True(rock.Velocity.Z < -2 && rock.Velocity.Y > 0, $"v {rock.Velocity}");
    }

    [Fact]
    public void ThePush_SlowsTurningToNothing_AndTheChargeToAnEighth()
    {
        using var world = World();
        var wizard = Wizard(world);

        Assert.True(wizard.PressCast(CastType.Force));
        Run(world, 0.1f);
        wizard.PrepareStep();
        Assert.Equal(Goblin.TurnSpeed * CastRules.ChargeTurnSpeed, wizard.Body.Conditions.TurnSpeed, 4);
        wizard.ReleaseCast(CastType.Force);
        Run(world, 0.15f);
        wizard.PrepareStep();
        Assert.Equal(0f, wizard.Body.Conditions.TurnSpeed);
        Run(world, 1);
        wizard.PrepareStep();
        Assert.Equal(Goblin.TurnSpeed, wizard.Body.Conditions.TurnSpeed, 4);
    }

    [Fact]
    public void ASelfCast_SpendsTheSpell_AndEndsWithItsClip()
    {
        using var world = World();
        var wizard = Wizard(world);
        wizard.Conjure(Elements.Water);

        Assert.True(wizard.PressCast(CastType.Self));
        Run(world, Tick);
        Assert.Same(CharacterStates.Cast, wizard.State);
        Run(world, 0.2f);
        Assert.Equal(default, wizard.Spell);                                         // cast (water has no effect yet)
        wizard.ReleaseCast(CastType.Self);
        Run(world, 0.6f);

        Assert.Same(CharacterStates.Idle, wizard.State);
        Assert.Equal((CastType.None, CastType.None), (wizard.CastType, wizard.HeldCast));
    }

    [Fact]
    public void AWeaponCast_ImbuesTheWeapon_ThenCastsWhatItHolds()
    {
        using var world = World();
        var wizard = Wizard(world);
        wizard.Conjure(Elements.Fire);
        wizard.Conjure(Elements.Fire);

        Assert.True(wizard.PressCast(CastType.Weapon));
        Assert.Equal(2, wizard.SpellQueue.Count);                                   // a weapon cast keeps the queue
        var sword = wizard.ItemIn(0)!;
        Assert.True(RunUntil(world, () => sword.IsSpellCharged));
        Assert.Equal(0, wizard.SpellQueue.Count);
        wizard.ReleaseCast(CastType.Weapon);
        Run(world, 0.8f);

        Assert.True(wizard.PressCast(CastType.Weapon));                             // charged: the attack casts
        Assert.True(RunUntil(world, () => !sword.IsSpellCharged));
    }

    [Fact]
    public void LightningWhileWet_HurtsInsteadOfQueueing()
    {
        using var world = World();
        var wizard = Wizard(world);
        wizard.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, wizard.Position);
        Assert.True(wizard.Statuses.Has(StatusEffects.Wet));

        Assert.False(wizard.Conjure(Elements.Lightning));

        Assert.Equal(0, wizard.SpellQueue.Count);
        Assert.True(wizard.HitPoints < 300);
    }

    [Fact]
    public void ABotDoesNotCastFromItsButtonsState()
    {
        using var world = World();
        var bot = Animated(world, clips: Casting());
        bot.Equip(1, new Item(Staff), "hand");
        bot.CastType = CastType.Force;

        Run(world, 0.2f);

        Assert.NotSame(CharacterStates.Cast, bot.State);
    }

    [Fact]
    public void TheCastAction_CastsOnce_AndOnlyWithoutARunningEffect()
    {
        using var world = World();
        var wizard = Wizard(world);
        var cast = new Actions.CastSpell(0, 1, null);
        wizard.CastType = CastType.Force;                                            // an empty queue: a push

        cast.Run(wizard, first: false);
        Assert.Null(wizard.CurrentSpell);
        cast.Run(wizard, first: true);
        var push = wizard.CurrentSpell;
        cast.Run(wizard, first: true);

        Assert.Same(push, wizard.CurrentSpell);
        Assert.True(cast.FromStaff);
    }

    [Fact]
    public void AChargedProjectile_IsCastOnRelease_AndThePushClipCastsNothingMore()
    {
        using var world = World();
        var wizard = Wizard(world);
        wizard.Conjure(Elements.Earth);

        ChargeAndRelease(world, wizard, CastType.Force, 0.3f);
        Run(world, Tick * 2);
        Assert.Equal(default, wizard.Spell);                                         // cast as the charge ended
        Assert.Single(world.Entities.Entities.OfType<Missile>());                    // the boulder flies; its one shot ends the cast
        Run(world, 0.8f);

        Assert.Null(wizard.CurrentSpell);                                            // no push from the cast clip
        Assert.Same(CharacterStates.Idle, wizard.State);
    }

    [Fact]
    public void AnAreaCharge_StaysLockedUntilTheForceButtonIsLetGo()
    {
        using var world = World();
        var wizard = Wizard(world);

        ChargeAndRelease(world, wizard, CastType.Area, 0.1f);
        Run(world, 1);
        Assert.False(wizard.ChargeUnlocked);
        Assert.True(wizard.PressCast(CastType.Force));
        Run(world, Tick * 2);
        Assert.Same(CharacterStates.Cast, wizard.State);                              // as the original: no charge
        wizard.ReleaseCast(CastType.Force);

        Assert.True(wizard.ChargeUnlocked);
    }

    [Fact]
    public void ABot_CastsThroughItsAttack_AndTheAttacksEndStopsIt()
    {
        using var world = World();
        var bot = Animated(world, clips: Casting());
        var near = Animated(world, Foe, at: new Vector3(0, 0.9f, -2));
        Run(world, 0.05f);

        (bot.SpellPower, bot.CastType) = (1, CastType.Force);                        // as the AI's cast ability sets them
        bot.PlayAttack("attack_cast", allowRotate: true);
        Assert.True(RunUntil(world, () => bot.CurrentSpell is PushEffect));
        Assert.True(RunUntil(world, () => bot.State != CharacterStates.Attack));

        Assert.Null(bot.CurrentSpell);
        Assert.Equal(CastType.None, bot.CastType);
        Assert.True(near.Body.IsPushed || near.Position.Z < -2.2f, $"at {near.Position}");
    }

    [Fact]
    public void TheDead_NeitherCastNorConjure_AndACharge_IgnoresElements()
    {
        using var world = World();
        var wizard = Wizard(world);
        Assert.True(wizard.PressCast(CastType.Force));
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Charge, wizard.State);

        Assert.False(wizard.Conjure(Elements.Fire));
        wizard.ReleaseCast(CastType.Force);
        Run(world, 1);
        wizard.Kill();
        Run(world, 2);

        Assert.False(wizard.PressCast(CastType.Force));
        Assert.False(wizard.Conjure(Elements.Fire));
    }
}
