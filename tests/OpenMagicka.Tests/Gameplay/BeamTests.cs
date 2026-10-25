using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Physics;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Beams in the world (SPELLS.md §5.4).</summary>
public class BeamTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static Character Caster(GameWorld world, Vector3? at = null, CharacterTemplate? template = null, params Elements[] queue)
    {
        var caster = Animated(world, template, at);
        foreach (var element in queue)
        {
            caster.Conjure(element);
        }
        return caster;
    }

    private static BeamEffect Fire(Character caster)
    {
        caster.CastType = CastType.Force;
        caster.CastSpell(fromStaff: true);
        return Assert.IsType<BeamEffect>(caster.CurrentSpell);
    }

    private static void Face(GameWorld world, Character character, Vector3 direction)
    {
        character.Body.Movement = direction;
        Run(world, 0.3f);
        character.Body.Movement = Vector3.Zero;
        Run(world, 0.2f);
    }

    [Fact]
    public void AForwardBeam_Grows_AndHurtsWhatItRestsOn_EveryQuarterSecond()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Arcane);
        var foe = Animated(world, Foe, at: new Vector3(0, 0.9f, -6));
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, -9));
        Run(world, 0.05f);

        var effect = Fire(caster);
        Run(world, 0.05f);
        var beam = effect.Beam!;
        Assert.InRange(beam.Length, 0.1f, 6);
        Run(world, 0.25f);
        var first = foe.HitPoints;
        Assert.InRange(beam.Length, 5, 6);                                           // cut where it meets the foe
        Run(world, 0.6f);

        Assert.True(foe.HitPoints < first || first < 300, $"{first} → {foe.HitPoints}");
        Assert.Equal(300f, behind.HitPoints);
    }

    [Fact]
    public void ABeam_EndsWithItsTime_OrWhenTheCasterStopsCasting()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Life);
        Run(world, 0.05f);
        var effect = Fire(caster);
        var beam = effect.Beam!;

        Run(world, 0.5f);
        caster.CastType = CastType.None;
        Run(world, Tick * 2);

        Assert.False(effect.IsActive);
        Assert.True(beam.IsDead);
    }

    [Fact]
    public void ABeam_ReflectsOffAReflectFace_AndExplodesComingStraightBack()
    {
        using var world = World();
        Vector3[] mirror = [new(-30, 0, -4), new(30, 0, -4), new(30, 4, -4), new(-30, 4, -4)];
        world.Physics.AddStaticMesh(Beam.ReflectMaterial, mirror, [new MeshTriangle(0, 1, 2), new MeshTriangle(0, 2, 3)]);   // facing the caster
        var caster = Caster(world, queue: Elements.Arcane);
        Run(world, 0.05f);

        Face(world, caster, Vector3.Normalize(new Vector3(1, 0, -1)));
        var head = Caster(world, new Vector3(-10, 0.9f, 0), null, Elements.Arcane);
        Run(world, 0.05f);

        var beam = Fire(caster).Beam!;
        var straight = Fire(head).Beam!;
        Run(world, 0.2f);

        var child = Assert.IsType<Beam>(beam.Child);
        Assert.True(child.Direction.Z > 0.6f && child.Direction.X > 0.6f, $"mirrored {child.Direction}");
        Assert.InRange(beam.Length, 1, 6);                                           // cut at the mirror (the caster walked a little to turn)
        Assert.True(straight.IsExploding || straight.IsDead, "straight back on itself: it explodes");
    }

    [Fact]
    public void CrossingBeamsOfArcaneAndLife_Explode()
    {
        using var world = World();
        var arcane = Caster(world, new Vector3(-6, 0.9f, -6), null, Elements.Arcane);
        var life = Caster(world, new Vector3(6, 0.9f, -6), Foe, Elements.Life);
        Face(world, arcane, Vector3.UnitX);
        Face(world, life, -Vector3.UnitX + Vector3.UnitZ * 0.2f);
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);

        var a = Fire(arcane).Beam!;
        Fire(life);
        Run(world, 0.5f);

        Assert.True(a.IsDead || a.IsExploding, "they met and blew up");
        Run(world, 0.5f);
        Assert.Equal(3, cues.OfType<CameraShakeCue>().Count());                      // once: each owner's blast and the knockback
    }

    [Fact]
    public void CrossingArcaneBeamsOfTwoCasters_Merge()
    {
        using var world = World();
        var one = Caster(world, new Vector3(-5, 0.9f, 0), null, Elements.Arcane);
        var two = Caster(world, new Vector3(5, 0.9f, 0), Foe, Elements.Arcane);
        Face(world, one, Vector3.Normalize(new Vector3(1, 0, -1)));
        Face(world, two, Vector3.Normalize(new Vector3(-1, 0, -1)));

        var a = Fire(one).Beam!;
        var b = Fire(two).Beam!;
        Run(world, 0.3f);

        var child = a.Child ?? b.Child;
        Assert.NotNull(child);
        Assert.Equal(2, child!.Parents.Count);
        Assert.Equal(2, child.Owners.Count());
        Assert.True(child.Direction.Z < -0.9f, $"along the mean {child.Direction}");
    }

    [Fact]
    public void ABeam_HasItsSpellsColour_AgesAsItRuns_AndAMergedOne_IsHalfwayBetween_WithMoreRings()
    {
        using var world = World();
        var one = Caster(world, new Vector3(-5, 0.9f, 0), null, Elements.Arcane);
        var two = Caster(world, new Vector3(5, 0.9f, 0), Foe, Elements.Arcane, Elements.Fire);
        Face(world, one, Vector3.Normalize(new Vector3(1, 0, -1)));
        Face(world, two, Vector3.Normalize(new Vector3(-1, 0, -1)));

        var a = Fire(one).Beam!;
        var b = Fire(two).Beam!;
        Run(world, 0.3f);

        Assert.Equal(SpellColours.Arcane, a.Colour);
        Assert.Equal(new Vector3(1.9f, 0.5f, 0.5f), b.Colour, (x, y) => Vector3.Distance(x, y) < 1e-4f);   // arcane once, fire by its magnitude
        Assert.Equal(4, a.Rings);
        Assert.InRange(a.Age, 0.25f, 0.35f);
        var child = (a.Child ?? b.Child)!;
        Assert.Equal(Vector3.Lerp(a.Colour, b.Colour, 0.5f), child.Colour, (x, y) => Vector3.Distance(x, y) < 1e-4f);
        Assert.Equal(6, child.Rings);                                                              // 2, and 2 for each of the two casters
    }

    [Fact]
    public void ACharacterWithGibs_BloatsWhenABeamKillsIt_AndBursts()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Arcane);
        var victim = Animated(world, Foe with { HasGibs = true, MaxHitPoints = 10 }, at: new Vector3(0, 0.9f, -3));
        var near = Animated(world, Foe, at: new Vector3(1.2f, 0.9f, -3.5f));
        Run(world, 0.05f);

        Fire(caster);
        Assert.True(RunUntil(world, () => victim.IsBloating));
        Run(world, Tick);
        Assert.Same(CharacterStates.Bloat, victim.State);
        Run(world, Character.BloatTime + 0.1f);

        Assert.True(victim.IsOverkilled && victim.IsBloatKilled);
        Assert.True(near.HitPoints < 300, "the burst reaches beside it");
    }

    [Fact]
    public void AnAreaBeam_Blasts_ASelfLifeBeamHeals_AndAWeaponBeamStrikesAnArc()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Arcane);
        var near = Animated(world, Foe, at: new Vector3(2, 0.9f, 0));
        Run(world, 0.05f);
        caster.CastType = CastType.Area;
        caster.CastSpell(fromStaff: true);
        Assert.True(near.HitPoints < 300);

        var healer = Caster(world, new Vector3(10, 0.9f, 10), null, Elements.Life);
        healer.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, healer.Position);
        Run(world, 0.05f);
        var hurt = healer.HitPoints;
        healer.CastType = CastType.Self;
        healer.CastSpell(fromStaff: true);
        Run(world, Tick * 2);
        Assert.True(healer.HitPoints > hurt);

        var blade = Caster(world, new Vector3(-10, 0.9f, 10), null, Elements.Arcane);
        var ahead = Animated(world, Foe, at: new Vector3(-10, 0.9f, 8.5f));
        Run(world, 0.05f);
        blade.CastType = CastType.Weapon;
        blade.CastSpell(fromStaff: true);
        Assert.True(ahead.HitPoints < 300);
    }

    private static bool RunUntil(GameWorld world, Func<bool> condition, float limit = 2)
    {
        for (var t = 0f; t < limit && !condition(); t += Tick)
        {
            world.Tick(Tick);
        }
        return condition();
    }
}
