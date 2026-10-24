using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The corners of gunfire (ENTITIES_AI.md §8.1.3).</summary>
public class GunEdgeTests
{
    private static ItemTemplate Gun(WeaponClass weaponClass = WeaponClass.Rifle, int clip = 3, float range = 20, bool events = true) =>
        new("gun", weaponClass, Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
            Gun: new GunTemplate(range, clip, 10, 1, 100,
                events ? [new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1))])] : [],
                PlainSprite: 1));

    private static Character Shooter(GameWorld world, ItemTemplate gun, float window = 0.1f)
    {
        var attack = new CharacterAnimation("attack_melee0", new AnimationClip("aim", 1), 1, 0, false, [new Gunfire(0, window, 0, 1)]);
        var pickup = new CharacterAnimation("pickup_weapon", new AnimationClip("pickup", 1), 1, 0, false, [new Gunfire(0, window, 0, 1)]);
        var shooter = Animated(world, Goblin with { Factions = Factions.Human }, clips: new CharacterAnimations(ClipsWith([attack, pickup]).Sets, SkeletonTests.Arm()));
        shooter.Equip(0, new Item(gun), "hand");
        return shooter;
    }

    private static int Tracers(Character character, Action act)
    {
        var count = 0;
        character.Cue += (_, cue) => count += cue is TracerCue ? 1 : 0;
        act();
        return count;
    }

    [Theory]
    [InlineData(WeaponClass.Rifle, false, 1)]                                     // a player's rifle stops with its window
    [InlineData(WeaponClass.Handgun, false, 1)]
    [InlineData(WeaponClass.Machinegun, false, 1)]                                // let go
    [InlineData(WeaponClass.Machinegun, true, 3)]                                 // still attacking: the whole clip
    [InlineData(WeaponClass.Heavy, false, 3)]
    public void APlayersGun_StopsByItsClass(WeaponClass weaponClass, bool attacking, int shots)
    {
        using var world = World();
        var shooter = Shooter(world, Gun(weaponClass), window: 0.05f);
        shooter.IsPlayer = true;
        Run(world, 0.1f);

        var fired = Tracers(shooter, () =>
        {
            shooter.ForceAnimation("attack_melee0");
            shooter.Attacking = attacking;
            Run(world, 1);
        });

        Assert.Equal(shots, fired);
    }

    [Fact]
    public void WhilePickingUp_TheGunSpendsRounds_ButShootsNothing()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(), window: 1);
        var muzzles = 0;
        shooter.Cue += (_, cue) => muzzles += cue is MuzzleCue ? 1 : 0;
        Run(world, 0.1f);

        var fired = Tracers(shooter, () =>
        {
            shooter.ForceAnimation("pickup_weapon");
            Run(world, 1);
        });

        Assert.Equal(0, fired);
        Assert.Equal(3, muzzles);
        Assert.Equal(0, shooter.ItemIn(0)!.Rounds);
    }

    [Fact]
    public void AGunWithNoEvents_NeverStarts()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(events: false));
        var cues = new List<AnimationCue>();
        shooter.Cue += (_, cue) => cues.Add(cue);
        Run(world, 0.1f);

        shooter.ForceAnimation("attack_melee0");
        Run(world, 1);

        Assert.DoesNotContain(cues, c => c is GunfireCue or TracerCue);
    }

    [Fact]
    public void AShot_ReachesItsRange_AndStopsAtTheNearerOfTwo()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(clip: 1, range: 6));
        var near = Animated(world, at: new Vector3(0.1f, 0.9f, -4));
        var far = Animated(world, at: new Vector3(0.1f, 0.9f, -5.5f));
        var beyond = Animated(world, at: new Vector3(3, 0.9f, -9));
        Run(world, 0.1f);

        shooter.ForceAnimation("attack_melee0");
        Run(world, 1);

        Assert.Equal((290f, 300f, 300f), (near.HitPoints, far.HitPoints, beyond.HitPoints));
    }

    [Fact]
    public void AHeldDeflectAura_TurnsNoShot()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(clip: 1));
        var guard = Animated(world, at: new Vector3(0, 0.9f, -5));
        guard.Equip(1, new Item(new ItemTemplate("staff_of_deflection", WeaponClass.Staff, Auras: [new DeflectAura(AuraTarget.Self, 100, 3, 3)])), "hand2");
        Run(world, 0.1f);

        shooter.ForceAnimation("attack_melee0");
        Run(world, 1);

        Assert.Equal(290f, guard.HitPoints);
    }

    [Fact]
    public void FacingDeflectors_DoNotBounceForever()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(clip: 1, range: 10000));
        Animated(world, Goblin with { Auras = [new DeflectAura(AuraTarget.Self, 100, 3, 3)] }, at: new Vector3(0, 0.9f, -5));
        Animated(world, Goblin with { Auras = [new DeflectAura(AuraTarget.Self, 100, 3, 3)] }, at: new Vector3(0, 0.9f, -5.3f));
        Run(world, 0.1f);

        var fired = Tracers(shooter, () =>
        {
            shooter.ForceAnimation("attack_melee0");
            Run(world, 1);
        });

        Assert.InRange(fired, 1, Guns.MaxBounces + 1);
    }

    [Fact]
    public void AGunLeavingTheHand_StopsItsBurst()
    {
        using var world = World();
        var shooter = Shooter(world, Gun(clip: 100), window: 1);
        var gun = shooter.ItemIn(0)!;
        Run(world, 0.1f);
        shooter.ForceAnimation("attack_melee0");
        Run(world, 0.2f);

        shooter.Unequip(0);

        Assert.False(gun.Firing);
        Assert.Equal(0, gun.Rounds);
        Assert.Throws<ArgumentException>(() => new Item(Gun() with { WeaponClass = WeaponClass.SlashMedium }));
    }
}
