using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The sounds the original's code raises (0.0.3): blood on blows, barriers and mines going, self-shields.</summary>
public class GameSoundsTests
{
    private static List<SoundCue> Heard(GameWorld world)
    {
        var heard = new List<SoundCue>();
        world.Entities.Cue += (_, cue) =>
        {
            if (cue is SoundCue sound)
            {
                heard.Add(sound);
            }
        };
        return heard;
    }

    private static DamageEntry Blow(Elements element, float amount = 50) => new(AttackProperties.Damage, element, amount, 1);

    [Fact]
    public void APhysicalBlow_DrawsBlood_ByItsAmount()
    {
        using var world = World();
        var goblin = Spawn(world, Goblin, new Vector3(0, 0.9f, 0));
        var heard = Heard(world);

        goblin.Damage(Blow(Elements.Earth), null, Vector3.UnitX);

        var blood = Assert.Single(heard, s => s.Cue == Character.GoreSound);
        Assert.InRange(blood.Magnitude!.Value, 1, 50);
    }

    [Fact]
    public void FireAFrozenOrABloodlessOne_DrawNoBlood()
    {
        using var world = World();
        var goblin = Spawn(world, Goblin, new Vector3(0, 0.9f, 0));
        var doll = Spawn(world, Goblin with { Bleeds = false }, new Vector3(5, 0.9f, 0));
        var heard = Heard(world);

        goblin.Damage(Blow(Elements.Fire), null, Vector3.UnitX);
        doll.Damage(Blow(Elements.Earth), null, Vector3.UnitX);

        Assert.DoesNotContain(heard, s => s.Cue == Character.GoreSound);
    }

    [Fact]
    public void AFrozenOne_AndOneWithGoreOff_DrawNoBlood()
    {
        using var world = World();
        var frozen = Spawn(world, Goblin, new Vector3(0, 0.9f, 0));
        var spared = Spawn(world, Goblin, new Vector3(5, 0.9f, 0));
        spared.BloodAndGore = false;
        frozen.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.UnitX);   // wet,
        frozen.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.UnitX);    // then frozen
        Assert.True(frozen.Statuses.Has(StatusEffects.Frozen));
        var heard = Heard(world);

        frozen.Damage(Blow(Elements.Earth, 10), null, Vector3.UnitX);
        spared.Damage(Blow(Elements.Earth), null, Vector3.UnitX);

        Assert.DoesNotContain(heard, s => s.Cue == Character.GoreSound);
    }

    [Fact]
    public void ASelfShield_SoundsAsItIsPutOn()
    {
        using var world = World();
        var wizard = Animated(world);
        var heard = Heard(world);

        wizard.AddSelfShield(Spell.Of(Elements.Shield | Elements.Earth));

        Assert.Contains(heard, s => s.Cue == "spell_shield_self_earth");
    }

    [Fact]
    public void ABarriersAndAMinesBlasts_SoundTheirElements()
    {
        Assert.Equal(["spell_arcane_area"], SpellSounds.Blast(Spell.Of(Elements.Shield | Elements.Arcane)));
        Assert.Equal(["spell_fire_spray", "spell_steam_spray"], SpellSounds.ExtraSprays(Elements.Arcane | Elements.Fire | Elements.Steam));
        Assert.Equal("spell_ice_earth_barrier_break", Barrier.BreakSound(Spell.Of(Elements.Shield | Elements.Ice | Elements.Earth)));
        Assert.Equal("spell_earth_barrier_break", Barrier.BreakSound(Spell.Of(Elements.Shield | Elements.Earth)));
        Assert.Null(Barrier.BreakSound(Spell.Of(Elements.Shield | Elements.Arcane)));
    }

    [Fact]
    public void AMineGoingOff_SoundsItsElements()
    {
        using var world = World();
        var mine = SpellMine.Lay(world.Entities, world.Physics, null, Vector3.Zero, Vector3.UnitX, Spell.Of(Elements.Shield | Elements.Arcane), default);
        var heard = Heard(world);

        mine.Detonate();

        Assert.Contains(heard, s => s.Cue == "spell_arcane_area");
    }
}
