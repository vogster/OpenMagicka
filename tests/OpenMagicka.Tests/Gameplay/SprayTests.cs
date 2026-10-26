using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Sprays in the world (SPELLS.md §5.2).</summary>
public class SprayTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    /// <summary>A cast clip that loops (a Force spray is held), its CastSpell action at the start.</summary>
    private static CharacterAnimation CastClip(string name, bool loop = false) =>
        new(name, new AnimationClip(name, 0.6f), 1, 0, loop, [new Actions.CastSpell(0, 0.5f, null)]);

    private static Character Wizard(GameWorld world, params Elements[] queue)
    {
        var clips = new CharacterAnimations(ClipsWith([CastClip("cast_force_spray", loop: true), CastClip("cast_area_blast"), CastClip("cast_self"), CastClip("cast_sword"), CastClip("cast_sword_spray")]).Sets,
            SkeletonTests.Arm());
        var wizard = Animated(world, clips: clips);
        wizard.IsPlayer = true;
        wizard.Equip(1, new Item(new ItemTemplate("staff", WeaponClass.Staff)), "hand");
        wizard.Equip(0, new Item(new ItemTemplate("sword", WeaponClass.SlashMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1))), "hand");
        Run(world, 0.05f);
        foreach (var element in queue)
        {
            wizard.Conjure(element);
        }
        return wizard;
    }

    private static bool RunUntil(GameWorld world, Func<bool> condition, float limit = 1.5f)
    {
        for (var t = 0f; t < limit && !condition(); t += Tick)
        {
            world.Tick(Tick);
        }
        return condition();
    }

    [Fact]
    public void AForceSpray_HitsWhatIsAhead_EveryQuarterSecond_WhileHeld()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Cold, Elements.Cold);
        var ahead = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, 3));

        Assert.True(wizard.PressCast(CastType.Force));
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is SprayEffect));
        var spray = (SprayEffect)wizard.CurrentSpell!;
        Assert.True(RunUntil(world, () => spray.HitList.ContainsKey(ahead)));
        var first = ahead.HitPoints;
        Run(world, 0.1f);
        Assert.Equal(first, ahead.HitPoints);                                        // waits its quarter second
        Run(world, 0.3f);

        Assert.True(ahead.HitPoints < first, $"{first} → {ahead.HitPoints}");
        Assert.True(ahead.Statuses.Has(StatusEffects.Cold));
        Assert.Equal(300f, behind.HitPoints);
        Assert.Same(CharacterStates.Cast, wizard.State);
    }

    [Fact]
    public void AForceSpray_StopsWhenLetGo_AfterItsMinimum()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Fire);
        Assert.True(wizard.PressCast(CastType.Force));
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is SprayEffect));
        var spray = wizard.CurrentSpell!;

        wizard.ReleaseCast(CastType.Force);
        Assert.True(RunUntil(world, () => !spray.IsActive, 1));

        Assert.True(RunUntil(world, () => wizard.State != CharacterStates.Cast, 0.2f));
        Assert.Null(wizard.CurrentSpell);
    }

    [Fact]
    public void AForceSpray_PlaysItsElementsSpraySounds_AndStopsThemWhenItEnds()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Fire, Elements.Fire);
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        Assert.True(wizard.PressCast(CastType.Force));
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is SprayEffect));
        var spray = wizard.CurrentSpell!;

        var sound = Assert.Single(cues.OfType<SoundCue>(), c => c.Cue.StartsWith("spell_", StringComparison.Ordinal));
        Assert.Equal(("spell_fire_spray", 2f), (sound.Cue, sound.Magnitude));
        Assert.Empty(cues.OfType<SoundStopCue>());

        wizard.ReleaseCast(CastType.Force);
        Assert.True(RunUntil(world, () => !spray.IsActive, 1));
        Assert.Equal("spell_fire_spray", Assert.Single(cues.OfType<SoundStopCue>()).Cue);
    }

    [Fact]
    public void AForceSpray_LastsFourSecondsAtMost()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Water);
        Assert.True(wizard.PressCast(CastType.Force));
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is SprayEffect));
        var spray = wizard.CurrentSpell!;

        Run(world, SprayRules.MaxDuration - 0.2f);
        Assert.True(spray.IsActive);
        Run(world, 0.3f);

        Assert.False(spray.IsActive);
    }

    [Fact]
    public void AnAreaSpray_BlastsAroundTheCaster_SparingIt()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Fire, Elements.Fire);
        var near = Animated(world, Foe, at: new Vector3(2, 0.9f, 0));

        Assert.True(wizard.PressCast(CastType.Area));
        Assert.True(RunUntil(world, () => near.HitPoints < 300 || near.Statuses.Has(StatusEffects.Burning)));

        Assert.Equal(300f, wizard.HitPoints);
        Assert.True(near.Statuses.Has(StatusEffects.Burning));
    }

    [Fact]
    public void ASelfSpray_LandsOnTheCaster()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Water);

        Assert.True(wizard.PressCast(CastType.Self));

        Assert.True(RunUntil(world, () => wizard.Statuses.Has(StatusEffects.Wet)));
    }

    [Fact]
    public void AWeaponSpray_StrikesAnArcFromTheBlade()
    {
        using var world = World();
        var wizard = Wizard(world, Elements.Fire);
        var near = Animated(world, Foe, at: new Vector3(0.5f, 0.9f, -2.5f));
        var aside = Animated(world, Foe, at: new Vector3(4, 0.9f, 2));
        var sword = wizard.ItemIn(0)!;
        Assert.True(wizard.PressCast(CastType.Weapon));
        Assert.True(RunUntil(world, () => sword.IsSpellCharged));
        wizard.ReleaseCast(CastType.Weapon);
        Assert.True(RunUntil(world, () => wizard.State == CharacterStates.Idle));

        Assert.True(wizard.PressCast(CastType.Weapon));
        Assert.True(RunUntil(world, () => near.Statuses.Has(StatusEffects.Burning)));

        Assert.False(aside.Statuses.Has(StatusEffects.Burning));
    }

    [Fact]
    public void ASpray_ShowsItsElementsEffects_AndEndsThem()
    {
        using var world = World();
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        var wizard = Wizard(world, Elements.Water, Elements.Fire, Elements.Fire);  // steam and fire
        Assert.True(wizard.PressCast(CastType.Force));
        Assert.True(RunUntil(world, () => wizard.CurrentSpell is SprayEffect));
        var spray = wizard.CurrentSpell!;

        wizard.ReleaseCast(CastType.Force);
        Assert.True(RunUntil(world, () => !spray.IsActive, 1));

        Assert.Equal(["spray_fire1", "spray_steam1"], cues.OfType<ItemEffectCue>().Where(c => ReferenceEquals(c.Source, spray)).Select(c => c.Effect).Order());
        Assert.Contains(cues.OfType<ItemEffectEndCue>(), c => ReferenceEquals(c.Source, spray));
    }
}
