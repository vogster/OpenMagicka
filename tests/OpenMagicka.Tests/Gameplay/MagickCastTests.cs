using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Casting magicks: unlocks, the boost button, the cast clip (MAGICKS.md §1–2).</summary>
public class MagickCastTests
{
    private static CharacterAnimation CastClip(string name) =>
        new(name, new AnimationClip(name, 0.6f), 1, 0, false, [new OpenMagicka.Gameplay.Animation.Actions.CastSpell(0.05f, 0.5f, null)]);

    internal static CharacterAnimations Clips() => ClipsWith([
        CastClip("cast_magick_self"), CastClip("cast_magick_direct"), CastClip("cast_magick_sweep"), CastClip("cast_magick_global"),
        new("boost", new AnimationClip("boost", 1), 1, 0, true, []),
    ]);

    /// <summary>A level of a game of the given type with one player, whose wizard stands at the origin.</summary>
    internal static (GameWorld World, Player Player, Character Wizard) NewGame(GameType type = GameType.Campaign, params MagickType[] unlocked)
    {
        var world = World();
        world.Entities.Session = new GameSession(type);
        var player = world.Session!.AddPlayer();
        foreach (var magick in unlocked)
        {
            player.Unlock(magick);
        }
        var wizard = Animated(world, clips: Clips());
        (wizard.IsPlayer, wizard.Player, player.Avatar) = (true, player, wizard);
        Run(world, 0.05f);
        return (world, player, wizard);
    }

    internal static void Conjure(Character wizard, params Elements[] elements)
    {
        foreach (var element in elements)
        {
            wizard.Conjure(element);
        }
    }

    internal static bool RunUntil(GameWorld world, Func<bool> condition, float limit = 1.5f)
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

    [Fact]
    public void InTheCampaign_AnyPlayersMagickServesAll_InVersusEachHasItsOwn()
    {
        var campaign = new GameSession(GameType.Campaign);
        var (a, b) = (campaign.AddPlayer(), campaign.AddPlayer());
        a.Unlock(MagickType.Haste);
        var versus = new GameSession(GameType.Versus);
        var (c, d) = (versus.AddPlayer(), versus.AddPlayer());
        c.Unlock(MagickType.Haste);

        Assert.True(campaign.IsMagickAllowed(b, MagickType.Haste));
        Assert.False(campaign.IsMagickAllowed(b, MagickType.Teleport));
        Assert.True(versus.IsMagickAllowed(c, MagickType.Haste));
        Assert.False(versus.IsMagickAllowed(d, MagickType.Haste));
        Assert.True(versus.IsMagickAllowed(null, MagickType.Teleport));             // an NPC casts any
        a.Playing = false;
        Assert.False(campaign.IsMagickAllowed(b, MagickType.Haste));                 // only playing players count
    }

    [Fact]
    public void Boost_TurnsTheQueueIntoAnUnlockedMagick_AndTheClipCastsIt()
    {
        var (world, _, wizard) = NewGame(unlocked: MagickType.Haste);
        using var _ = world;
        Conjure(wizard, Elements.Lightning, Elements.Arcane, Elements.Fire);

        wizard.Boost();

        Assert.Equal(CastType.Magick, wizard.CastType);
        Assert.Equal(0, wizard.SpellQueue.Count);
        Run(world, Tick);
        Assert.Same(CharacterStates.Cast, wizard.State);
        Assert.Equal("cast_magick_self", wizard.CurrentAnimation);
        Assert.True(RunUntil(world, () => wizard.IsHasted));
        Assert.Equal(CastType.Magick, wizard.CastType);                             // held until the clip ends: no cast starts under it
        Assert.False(wizard.PressCast(CastType.Self));
        Assert.True(RunUntil(world, () => wizard.CastType == CastType.None));
    }

    [Fact]
    public void ALockedMagick_StaysAQueue()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Conjure(wizard, Elements.Lightning, Elements.Arcane, Elements.Fire);

        wizard.Boost();
        Run(world, 1);

        Assert.Equal(3, wizard.SpellQueue.Count);
        Assert.False(wizard.IsHasted);
    }

    [Fact]
    public void Boost_LeavesACastUnderWayAlone()
    {
        var (world, _, wizard) = NewGame(unlocked: MagickType.Haste);
        using var _ = world;
        wizard.CastType = CastType.Self;
        Conjure(wizard, Elements.Lightning, Elements.Arcane, Elements.Fire);

        wizard.Boost();

        Assert.Equal(CastType.Self, wizard.CastType);
        Assert.Equal(3, wizard.SpellQueue.Count);
    }

    [Fact]
    public void AnInstantMagick_IsCastAtOnce_WhileKnockedDown()
    {
        var (world, _, wizard) = NewGame(unlocked: MagickType.Haste);
        using var _ = world;
        wizard.KnockDown();
        Run(world, Tick);
        Assert.Same(CharacterStates.KnockDown, wizard.State);
        Conjure(wizard, Elements.Lightning, Elements.Arcane, Elements.Fire);

        wizard.Boost();

        Assert.True(wizard.IsHasted);
    }

    [Fact]
    public void AFailedMagick_SoundsSo()
    {
        var (world, _, wizard) = NewGame(unlocked: MagickType.Revive);
        using var _ = world;
        var cues = new List<AnimationCue>();
        wizard.Cue += (_, cue) => cues.Add(cue);
        Conjure(wizard, Elements.Life, Elements.Lightning);

        wizard.Boost();
        Run(world, 1);

        Assert.Contains(cues, c => c is SoundCue { Cue: "magick_fail" });           // nobody to revive
    }

    [Fact]
    public void Boosting_MendsAShieldAhead()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var shield = new SpellShield(world.Physics, null, ShieldShape.Sphere, wizard.Position + wizard.Body.Facing * 8, -wizard.Body.Facing, ShieldRules.ShieldRadius, 500);
        world.Entities.Add(shield);
        Run(world, Tick);
        Assert.Same(shield, wizard.ShieldToBoost());
        var before = shield.HitPoints;

        wizard.Boost();
        Run(world, 2 * Tick);

        Assert.Same(CharacterStates.Boost, wizard.State);
        Assert.True(shield.HitPoints > before + 90, $"{before} → {shield.HitPoints}");
        Run(world, 0.5f);
        Assert.NotSame(CharacterStates.Boost, wizard.State);                       // the presses stopped
    }

    [Fact]
    public void Boosting_HealsTheBubble_AndDoesNothingWithoutAShield()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        wizard.Boost();
        Assert.Equal(0, wizard.Boosts);

        wizard.AddSelfShield(Spell.Of(Elements.Shield));
        Run(world, 1);
        var before = ((SelfShield)wizard.SelfShield!).HitPoints;
        wizard.Boost();
        Run(world, 2 * Tick);

        Assert.True(((SelfShield)wizard.SelfShield!).HitPoints > before + 40, $"{before} → {((SelfShield)wizard.SelfShield!).HitPoints}");
    }

    [Fact]
    public void ABook_UnlocksItsMagick_ForEveryone_ButInVersusForThePickerOnly()
    {
        var (world, player, wizard) = NewGame();
        using var _ = world;
        var other = world.Session!.AddPlayer();
        var book = new BookOfMagick(world.Physics, MagickType.Teleport, wizard.Position + Vector3.UnitX, immovable: true);
        world.Entities.Add(book);
        Run(world, Tick);

        Assert.Same(book, wizard.FindBook());
        Assert.True(wizard.PickUp(book));
        Assert.True(player.HasUnlocked(MagickType.Teleport) && other.HasUnlocked(MagickType.Teleport));

        var (versus, picker, duelist) = NewGame(GameType.Versus);
        using var __ = versus;
        var rival = versus.Session!.AddPlayer();
        var dropped = new BookOfMagick(versus.Physics, MagickType.Haste, duelist.Position + Vector3.UnitY * 2, immovable: false, timeout: 20);
        versus.Entities.Add(dropped);
        Run(versus, 0.5f);
        Assert.True(duelist.PickUp(dropped));
        Assert.True(picker.HasUnlocked(MagickType.Haste));
        Assert.False(rival.HasUnlocked(MagickType.Haste));
    }

    [Fact]
    public void ABook_VanishesAfterItsTimeout()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var book = new BookOfMagick(world.Physics, MagickType.Teleport, new Vector3(5, 1, 0), immovable: false, timeout: 0.5f);
        world.Entities.Add(book);

        Run(world, 0.6f);

        Assert.DoesNotContain(book, world.Entities.Entities);
        Assert.False(wizard.PickUp(book));
    }
}
