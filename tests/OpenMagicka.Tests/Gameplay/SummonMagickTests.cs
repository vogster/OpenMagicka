using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Raise Dead, Summon Elemental, Summon Phoenix, Summon Death and Portal (MAGICKS.md §7).</summary>
public class SummonMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    private static List<Character> SummonsOf(GameWorld world, Character master) =>
        [.. world.Entities.Entities.OfType<Character>().Where(c => c.SummonMaster == master)];

    private static DamageEntry Blow(Elements element, float amount, float magnitude = 1) => new(AttackProperties.Damage, element, amount, magnitude);

    [Fact]
    public void RaiseDead_RaisesAZombieEveryTwoSeconds_FiveInAll_OnTheCastersSide()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        world.Entities.Bestiary = new Bestiary().Add(UndeadRising.Asset, Goblin with { Id = "zombie", Factions = Factions.Undead }, Clips());

        Assert.True(Cast(wizard, MagickType.SUndead));
        Run(world, Tick);
        Assert.Single(SummonsOf(world, wizard));
        Run(world, 4.1f);
        Assert.Equal(3, SummonsOf(world, wizard).Count);
        Run(world, 4.5f);

        var zombies = SummonsOf(world, wizard);
        Assert.Equal(5, zombies.Count);
        Assert.Empty(world.Entities.Entities.OfType<UndeadRising>());
        Assert.All(zombies, z =>
        {
            Assert.True(z.IsSummoned);
            Assert.Equal(wizard.Factions, z.Factions);
            Assert.InRange(Vector3.Distance(z.Position with { Y = 0 }, (wizard.Position + wizard.Body.Facing * SummonUndead.Ahead) with { Y = 0 }), 0, 3);
        });
    }

    [Fact]
    public void AnEgg_HatchesTheElementOfItsStrongestBlow_AndIgnoresTheOthers()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        world.Entities.Bestiary = new Bestiary().Add("data/characters/elemental_cold", Goblin with { Id = "elemental_cold" }, Clips());

        Assert.True(Cast(wizard, MagickType.SElemental));
        var egg = Assert.Single(world.Entities.Entities.OfType<ElementalEgg>());
        Assert.Equal(DamageResult.None, egg.Damage(Blow(Elements.Earth, 500), null, egg.Position));
        Assert.Equal(DamageResult.None, egg.Damage(Blow(Elements.Shield, 500), null, egg.Position));
        Run(world, Tick);
        Assert.Equal(Elements.None, egg.Hatch);

        egg.Damage([Blow(Elements.Fire, 10), Blow(Elements.Cold, 20, 2)], wizard, egg.Position);
        Run(world, Tick * 3);

        Assert.DoesNotContain(egg, world.Entities.Entities);
        var elemental = Assert.Single(SummonsOf(world, wizard));
        Assert.Equal("elemental_cold", elemental.Template.Id);
        Assert.Equal("special0", elemental.SpawnAnimation);
    }

    [Fact]
    public void AnEggKilledOutright_PicksAgainFromItsBlows()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Cast(wizard, MagickType.SElemental);
        var egg = Assert.Single(world.Entities.Entities.OfType<ElementalEgg>());

        egg.Kill();
        Assert.Equal(Elements.Shield, egg.Hatch);
        Run(world, Tick);

        Assert.Equal(Elements.None, egg.Hatch);                                       // as the original: its update picks again
        Assert.Contains(egg, world.Entities.Entities);
    }

    [Fact]
    public void ALevelsEgg_HatchesAtRandom_WhenAWizardComesNear()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var egg = new ElementalEgg(world.Physics, wizard.Position with { Y = 0 } + new Vector3(10, 0, 0), -Vector3.UnitZ) { Proximity = 3 };
        world.Entities.Add(egg);
        Run(world, 1.5f);
        Assert.Equal(Elements.None, egg.Hatch);

        var near = new ElementalEgg(world.Physics, wizard.Position with { Y = 0 } + new Vector3(2, 0, 0), -Vector3.UnitZ) { Proximity = 3 };
        world.Entities.Add(near);
        Assert.True(RunUntil(world, () => !world.Entities.Entities.Contains(near), 1.2f));
        Assert.Contains(egg, world.Entities.Entities);
    }

    [Fact]
    public void ThePhoenix_BurnsAroundItself_AndRaisesTheDeadWithAllTheirHitPoints()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var fallen = world.Session!.AddPlayer();
        var corpse = Animated(world, at: new Vector3(10, 0.9f, 0), clips: Clips());
        (corpse.IsPlayer, corpse.Player, fallen.Avatar) = (true, fallen, corpse);
        corpse.Kill();
        var foe = Animated(world, Foe, at: wizard.Position + new Vector3(0, 0, 3.5f));
        Run(world, 1);

        Assert.True(Cast(wizard, MagickType.SPhoenix));
        Assert.True(Cast(wizard, MagickType.SPhoenix));                                // one at a time; the cast counts
        Assert.Single(world.Entities.Entities.OfType<PhoenixDescent>());
        Run(world, PhoenixDescent.Descent - 0.1f);
        Assert.Equal(foe.MaxHitPoints, foe.HitPoints);
        Run(world, 0.5f);

        Assert.True(foe.HitPoints < foe.MaxHitPoints || foe.IsDead);
        var risen = Assert.IsType<Character>(fallen.Avatar);
        Assert.NotSame(corpse, risen);
        Assert.False(risen.IsDead);
        Assert.Equal(risen.MaxHitPoints, risen.HitPoints, 1);
    }

    [Fact]
    public void ThePhoenix_IndoorsOnlyCries()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        world.Entities.Indoors = true;

        Assert.True(Cast(wizard, MagickType.SPhoenix));

        Assert.Empty(world.Entities.Entities.OfType<PhoenixDescent>());
    }

    [Fact]
    public void Death_ComesForTheWeakest_SlowingTime_AndTimeComesBackAfter()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var strong = Animated(world, Foe, at: wizard.Position + new Vector3(-6, 0, 0));
        var weak = Animated(world, Foe, at: wizard.Position + new Vector3(6, 0, 0));
        var dead = Animated(world, Foe with { Factions = Factions.Undead }, at: wizard.Position + new Vector3(0, 0, 6));
        weak.Damage(Blow(Elements.Earth, 200), null, weak.Position);
        dead.Damage(Blow(Elements.Earth, 250), null, dead.Position);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.SDeath));
        var reaper = Assert.Single(world.Entities.Entities.OfType<Reaper>());
        Assert.Same(weak, reaper.Target);
        Assert.False(Cast(wizard, MagickType.SDeath));                                 // one at a time
        Run(world, 0.5f);
        Assert.True(world.Entities.TimeModifier < 0.5f);
        Assert.True(reaper.IsEthereal);
        Assert.Equal(DamageResult.None, reaper.Damage(Blow(Elements.Life, 100), null, reaper.Position));

        Assert.True(RunUntil(world, () => weak.HitPoints <= 0, 6), "cut down");
        Assert.False(strong.IsDead || dead.IsDead);
        Assert.True(RunUntil(world, () => !world.Entities.Entities.Contains(reaper), 6));
        Assert.Equal(1f, world.Entities.TimeModifier);
        Assert.True(RunUntil(world, () => weak.IsDead, 2));
    }

    [Fact]
    public void Death_StruckWithLifeOnceSolid_Goes_AndNullifyEndsIt()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var weak = Animated(world, Foe, at: wizard.Position + new Vector3(6, 0, 0));
        weak.Damage(Blow(Elements.Earth, 200), null, weak.Position);
        Run(world, Tick);

        Cast(wizard, MagickType.SDeath);
        var reaper = Assert.Single(world.Entities.Entities.OfType<Reaper>());
        Assert.True(RunUntil(world, () => !reaper.IsEthereal, 6));
        Assert.Equal(DamageResult.Hit, reaper.Damage(Blow(Elements.Life, 100), null, reaper.Position));
        Assert.True(RunUntil(world, () => reaper.Now == Reaper.Phase.Vanishing, 1));
        Assert.False(weak.IsDead);

        Assert.True(Cast(wizard, MagickType.Nullify));
        Run(world, Tick);
        Assert.Empty(world.Entities.Entities.OfType<Reaper>());
        Assert.Equal(1f, world.Entities.TimeModifier);
    }

    [Fact]
    public void Death_WithNoOneElse_ComesForTheCaster()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.SDeath));

        Assert.Same(wizard, Assert.Single(world.Entities.Entities.OfType<Reaper>()).Target);
    }

    [Fact]
    public void ThePortals_ConnectInPairs_SendWhatTouchesOneOutOfTheOther_AndTheOlderMoves()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.Portal));
        var first = Assert.Single(PortalGate.Of(world.Entities));
        Assert.Equal(PortalColour.Orange, first.Colour);
        Run(world, Tick);
        Assert.False(first.Link.Connected);

        wizard.Body.Body.Position += new Vector3(10, 0, 0);
        Run(world, Tick);
        Assert.True(Cast(wizard, MagickType.Portal));
        var second = PortalGate.Of(world.Entities)[1];
        Assert.Equal(PortalColour.Blue, second.Colour);
        Run(world, Tick);
        Assert.True(first.Link.Connected);

        var traveller = Animated(world, Foe, at: first.Position with { Y = 0.9f } + new Vector3(0.3f, 0, 0));
        Run(world, Tick * 3);
        Assert.True(Vector3.Distance(traveller.Position with { Y = 0 }, second.Position with { Y = 0 }) < 1, "came out of the other");
        Run(world, 0.5f);
        Assert.True(Vector3.Distance(traveller.Position with { Y = 0 }, second.Position with { Y = 0 }) < 1, "and stayed");

        wizard.Body.Body.Position += new Vector3(0, 0, 10);
        Run(world, Tick);
        Assert.True(Cast(wizard, MagickType.Portal));
        Run(world, Tick);
        var gates = PortalGate.Of(world.Entities);
        Assert.Equal(2, gates.Count);
        Assert.DoesNotContain(first, gates);
        Assert.Equal([PortalColour.Blue, PortalColour.Orange], gates.Select(g => g.Colour));

        Assert.True(Cast(wizard, MagickType.Nullify));
        Run(world, Tick);
        Assert.Empty(PortalGate.Of(world.Entities));
    }

    [Fact]
    public void ThePortals_TooFarApart_DoNotConnect()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Cast(wizard, MagickType.Portal);
        wizard.Body.Body.Position += new Vector3(35, 0, 0);
        Run(world, Tick);
        Cast(wizard, MagickType.Portal);
        Run(world, Tick);

        Assert.False(PortalGate.Of(world.Entities)[0].Link.Connected);
    }

    [Fact]
    public void Nullify_KillsTheSummoned_AndEndsTheRising()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        world.Entities.Bestiary = new Bestiary().Add(UndeadRising.Asset, Goblin with { Id = "zombie", Factions = Factions.Undead }, Clips());
        Cast(wizard, MagickType.SUndead);
        Run(world, 2.5f);
        Assert.Equal(2, SummonsOf(world, wizard).Count);

        Assert.True(Cast(wizard, MagickType.Nullify));
        Run(world, 3);

        Assert.All(SummonsOf(world, wizard), z => Assert.True(z.IsDead));
        Assert.Empty(world.Entities.Entities.OfType<UndeadRising>());
    }
}
