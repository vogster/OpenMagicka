using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Fear, Charm, Confuse, Nullify and Corporealize (MAGICKS.md §6).</summary>
public class MindMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    [Fact]
    public void Fear_SendsFoesFleeing_ButNotFriendsTheFearlessOrTheUndead()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: wizard.Position + new Vector3(3, 0, 0));
        var friend = Animated(world, at: wizard.Position + new Vector3(-3, 0, 0));
        var brave = Animated(world, Foe with { Fearless = true }, at: wizard.Position + new Vector3(0, 0, 3));
        var dead = Animated(world, Foe with { Factions = Factions.Undead }, at: wizard.Position + new Vector3(0, 0, -3));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Fear));
        Run(world, Tick);
        Assert.Same(CharacterStates.Panic, foe.State);
        var start = Vector3.Distance(foe.Position, wizard.Position);
        Run(world, 1);

        Assert.True(Vector3.Distance(foe.Position, wizard.Position) > start + 1, "fled");
        Assert.False(friend.IsFeared || brave.IsFeared || dead.IsFeared);
        Run(world, 4.2f);
        Assert.False(foe.IsFeared);
        Assert.NotSame(CharacterStates.Panic, foe.State);
        Assert.Equal(System.Numerics.Vector3.Zero, foe.Body.Movement);                   // where it was going (nowhere) is given back
    }

    [Fact]
    public void Charm_TakesTheFoeAhead_AndFailsWithNone()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Assert.False(Cast(wizard, MagickType.Charm));
        var ahead = Animated(world, Foe, at: wizard.Position + wizard.Body.Facing * 5);
        var aside = Animated(world, Foe, at: wizard.Position + Vector3.Cross(wizard.Body.Facing, Vector3.UnitY) * 5);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Charm));

        Assert.True(ahead.IsCharmed);
        Assert.Same(wizard, ahead.CharmOwner);
        Assert.False(aside.IsCharmed);
        Run(world, 15.1f);
        Assert.False(ahead.IsCharmed);
    }

    [Fact]
    public void Confuse_TakesAnNpcsFaction_ForFifteenSeconds()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var ahead = Animated(world, Foe, at: wizard.Position + wizard.Body.Facing * 5);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Confuse));

        Assert.True(ahead.IsConfused);
        Assert.Equal(Factions.None, ahead.Factions);
        Run(world, 15.1f);
        Assert.Equal(Factions.Human, ahead.Factions);
    }

    [Fact]
    public void Nullify_UndoesSpellsAndMagicks()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: wizard.Position + new Vector3(4, 0, 0));
        foe.Damage(new DamageEntry(AttackProperties.Status, Elements.Poison, 50, 1), null, foe.Position);
        foe.Fear(wizard);
        var shield = new SpellShield(world.Physics, null, ShieldShape.Sphere, new Vector3(20, 1, 20), -Vector3.UnitZ, ShieldRules.ShieldRadius, 5000);
        world.Entities.Add(shield);
        Cast(wizard, MagickType.Rain);
        Cast(wizard, MagickType.Haste);
        wizard.Conjure(Elements.Fire);
        Run(world, Tick);
        Assert.True(foe.Statuses.Has(StatusEffects.Poisoned));

        Assert.True(Cast(wizard, MagickType.Nullify));
        Run(world, 2 * Tick);

        Assert.False(foe.Statuses.Has(StatusEffects.Poisoned));
        Assert.False(foe.IsFeared);
        Assert.False(wizard.IsHasted);
        Assert.Equal(0, wizard.SpellQueue.Count);
        Assert.DoesNotContain(shield, world.Entities.Entities);
        Assert.Empty(world.Entities.Entities.OfType<Weather>());
    }

    [Fact]
    public void Corporealize_MakesTheEtherealSolid()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var ghost = Animated(world, Foe with { IsEthereal = true }, at: wizard.Position + new Vector3(10, 0, 0));
        Run(world, Tick);
        Assert.True(ghost.IsEthereal);

        Assert.True(Cast(wizard, MagickType.Corporealize));

        Assert.False(ghost.IsEthereal);
    }
}
