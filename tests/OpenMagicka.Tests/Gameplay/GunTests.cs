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

/// <summary>Guns firing bursts of shots (ENTITIES_AI.md §8.1.3).</summary>
public class GunTests
{
    private static readonly DamageEntry Bullet = new(AttackProperties.Damage, Elements.Earth, 10, 1);

    private static ItemTemplate Rifle(int clip = 3, float accuracy = 1, Vector3? muzzle = null) => new("rifle", WeaponClass.Rifle,
        Attach0: Matrix4x4.CreateTranslation(muzzle ?? new Vector3(0, 0, 1)),
        Gun: new GunTemplate(20, clip, 10, accuracy, 100, [
            new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Bullet)]),
            new EventGroup(new EventCondition(EventTrigger.Collision), [new SoundEvent("ricochet", 4, 1)])],
            TracerSprite: 5, PlainSprite: 1));

    /// <summary>A shooter whose attack fires for <paramref name="window"/> of its second.</summary>
    private static Character Shooter(GameWorld world, ItemTemplate gun, float window = 1)
    {
        var attack = new CharacterAnimation("attack_melee0", new AnimationClip("aim", 1), 1, 0, false, [new Gunfire(0, window, 0, 1)]);
        var shooter = Animated(world, Goblin with { Factions = Factions.Human }, clips: new CharacterAnimations(ClipsWith([attack]).Sets, SkeletonTests.Arm()));
        shooter.Equip(0, new Item(gun), "hand");
        return shooter;
    }

    private static List<AnimationCue> Cues(Character character)
    {
        var cues = new List<AnimationCue>();
        character.Cue += (_, cue) => cues.Add(cue);
        return cues;
    }

    private static void Shoot(GameWorld world, Character shooter, float seconds = 1)
    {
        Run(world, 0.1f);
        shooter.ForceAnimation("attack_melee0");
        Run(world, seconds);
    }

    [Fact]
    public void ABurst_FiresItsClip_AtItsRate()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle());
        var target = Animated(world, at: new Vector3(0.2f, 0.9f, -5));                // off the line: a shot along the axis has no surface side
        var cues = Cues(shooter);

        Shoot(world, shooter);

        Assert.Equal(270f, target.HitPoints);                                        // three rounds of 10
        Assert.Same(shooter, target.LastAttacker);
        var tracers = cues.OfType<TracerCue>().ToList();
        Assert.Equal([5, 1, 1], tracers.Select(t => t.Sprite));                      // the first of every five is a tracer
        Assert.Equal(3, cues.OfType<MuzzleCue>().Count());
        Assert.True(cues.OfType<GunfireCue>().First().Firing);
        Assert.All(tracers, t => Assert.Equal(0.4f, Vector2.Distance(new(t.To.X, t.To.Z), new(target.Position.X, target.Position.Z)), 2));   // on its surface
        Assert.All(tracers, t => Assert.Equal(target.Position.Z, t.To.Z, 1));        // as the original: the side nearest the shot's line, not where it enters
    }

    [Fact]
    public void TheLevel_StopsAShot_AndFiresTheCollisionGroups()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle(clip: 1, muzzle: new Vector3(0, -0.3f, 1)));  // aimed down at the floor
        var behind = Animated(world, at: new Vector3(0, 0.9f, -6));
        var cues = Cues(shooter);

        Shoot(world, shooter);

        Assert.Equal(300f, behind.HitPoints);
        Assert.Contains(cues, c => c is SoundCue { Cue: "ricochet" } s && MathF.Abs(s.Position.Y) < 0.05f);
    }

    [Fact]
    public void ADeflectAura_SendsTheShotBack()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle(clip: 1));
        var mirror = Animated(world, Goblin with { Auras = [new DeflectAura(AuraTarget.Self, 100, 3, 3)] }, at: new Vector3(0, 0.9f, -5));   // head on: the shot comes straight back

        Shoot(world, shooter);

        Assert.Equal(300f, mirror.HitPoints);
        Assert.Equal(290f, shooter.HitPoints);                                       // reflected straight back into the shooter
    }

    [Fact]
    public void AGlancingDeflection_SendsTheShotAside()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle(clip: 1));
        Animated(world, Goblin with { Auras = [new DeflectAura(AuraTarget.Self, 100, 3, 3)] }, at: new Vector3(0.05f, 0.9f, -5));
        var cues = Cues(shooter);

        Shoot(world, shooter);

        Assert.Equal(300f, shooter.HitPoints);
        var back = cues.OfType<TracerCue>().Single(t => t.To.Z > t.From.Z);
        Assert.True(back.To.X < -1, $"to {back.To}");                              // off the entry normal, which leans away
        Assert.True(back.Delay > 0);
    }

    [Fact]
    public void AnNpcsGun_StopsWithItsWindow()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle(clip: 10), window: 0.15f);
        var target = Animated(world, at: new Vector3(0, 0.9f, -5));
        var cues = Cues(shooter);

        Shoot(world, shooter, 2);

        Assert.InRange(target.HitPoints, 270f, 290f);                              // a round or three, not ten
        Assert.Contains(cues, c => c is GunfireCue { Firing: false });
    }

    [Fact]
    public void APoorAim_Scatters()
    {
        using var world = World();
        var shooter = Shooter(world, Rifle(clip: 10, accuracy: 0));
        var cues = Cues(shooter);

        Shoot(world, shooter, 1.2f);

        var ends = cues.OfType<TracerCue>().Select(t => t.To).ToList();
        Assert.Equal(10, ends.Count);
        Assert.True(ends.Select(e => MathF.Round(e.X, 2)).Distinct().Count() > 1, "every shot went the same way");
        Assert.All(ends, e => Assert.InRange(MathF.Atan2(e.X, -e.Z), -0.11f, 0.11f));   // within ±s/2 of ahead, s = 0.2
    }

    [Fact]
    public void ABadGun_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Rifle() with { Gun = Rifle().Gun! with { Rate = 0 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Rifle() with { Gun = Rifle().Gun! with { Range = float.NaN } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Gunfire(0, 1, -1, 1));
    }
}
