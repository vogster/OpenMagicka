using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Lightning in the world (SPELLS.md §5.5).</summary>
public class LightningTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static Character Caster(GameWorld world, params Elements[] queue)
    {
        var caster = Animated(world, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        caster.Equip(0, new Item(new ItemTemplate("sword", WeaponClass.SlashMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1))), "hand");
        foreach (var element in queue)
        {
            caster.Conjure(element);
        }
        return caster;
    }

    private static void Cast(Character caster, CastType cast)
    {
        caster.CastType = cast;
        caster.CastSpell(fromStaff: true);
    }

    [Fact]
    public void ForceLightning_FiresEightBoltsInASecond_ChainingOnFromItsTarget()
    {
        using var world = World();
        var caster = Caster(world, Elements.Lightning);
        var first = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var second = Animated(world, Foe, at: new Vector3(0.5f, 0.9f, -6));
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, 3));
        var bolts = new List<LightningCue>();
        world.Entities.Cue += (_, cue) => { if (cue is LightningCue l) bolts.Add(l); };
        Run(world, 0.05f);

        Cast(caster, CastType.Force);
        var effect = Assert.IsType<LightningEffect>(caster.CurrentSpell);
        Run(world, 1.1f);

        Assert.Equal(0, effect.BoltsLeft);
        Assert.False(effect.IsActive);
        Assert.True(first.HitPoints < 300 && second.HitPoints < 300, $"{first.HitPoints} {second.HitPoints}");
        Assert.Equal(300f, behind.HitPoints);
        Assert.Equal(24, bolts.Count);                                               // eight bolts: into the first, on to the second, then fading
        Assert.Equal(8, bolts.Count(b => b.Faded));
    }

    [Fact]
    public void AreaLightning_StrikesAllAround_WithinItsShortRange()
    {
        using var world = World();
        var caster = Caster(world, Elements.Lightning);
        var around = new[] { new Vector3(1.5f, 0.9f, 0), new Vector3(-1.5f, 0.9f, 0), new Vector3(0, 0.9f, 1.5f) }.Select(p => Animated(world, Foe, at: p)).ToList();
        var far = Animated(world, Foe, at: new Vector3(5, 0.9f, 0));
        Run(world, 0.05f);

        Cast(caster, CastType.Area);
        Run(world, 1.1f);

        Assert.All(around, a => Assert.True(a.HitPoints < 300));
        Assert.Equal(300f, far.HitPoints);
    }

    [Fact]
    public void ABlocker_AbsorbsTheBoltIntoItsWeapon_AndTheChainStops()
    {
        using var world = World();
        var caster = Caster(world, Elements.Lightning);
        var blocker = Caster(world);
        var beyond = Animated(world, Foe, at: new Vector3(0, 0.9f, -6));
        Run(world, 0.05f);
        (blocker.IsBlocking, blocker.BlockSlot) = (true, 0);
        var hits = new HashSet<Entity>(ReferenceEqualityComparer.Instance) { caster };

        LightningBolts.Strike(caster, new Vector3(0, 0.9f, 3), -Vector3.UnitZ, 10, [new DamageEntry(AttackProperties.Damage, Elements.Lightning, 100, 1)],
            new Spell(lightning: 1), hits);

        Assert.True(blocker.ItemIn(0)!.IsSpellCharged);
        Assert.Equal(300f, blocker.HitPoints);
        Assert.Equal(300f, beyond.HitPoints);
    }

    [Fact]
    public void AWetCaster_StrikesAllAroundAtRangeTwo()
    {
        using var world = World();
        var caster = Caster(world, Elements.Lightning);
        caster.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, caster.Position);
        var ahead = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var near = Animated(world, Foe, at: new Vector3(1.5f, 0.9f, 0.5f));
        Run(world, 0.05f);
        Assert.True(caster.Statuses.Has(StatusEffects.Wet));

        caster.CastType = CastType.Force;
        caster.CastSpell(fromStaff: true);
        Run(world, 1.1f);

        Assert.Equal(300f, ahead.HitPoints);                                         // beyond the wet range of 2
        Assert.True(near.HitPoints < 300);
    }

    [Fact]
    public void SelfLightning_StrikesTheCaster()
    {
        using var world = World();
        var caster = Caster(world, Elements.Lightning);
        Run(world, 0.05f);

        Cast(caster, CastType.Self);

        Assert.True(caster.HitPoints < 300);
    }
}
