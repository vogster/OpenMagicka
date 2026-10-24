using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.AnimationActionTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The presentation the animation actions ask the engine for.</summary>
public class AnimationCueTests
{
    private static List<AnimationCue> Listen(Character character)
    {
        var cues = new List<AnimationCue>();
        character.Cue += (owner, cue) =>
        {
            Assert.Same(character, owner);
            cues.Add(cue);
        };
        return cues;
    }

    [Fact]
    public void ASound_PlaysOnce_AtTheCharacter()
    {
        using var world = World();
        var goblin = Acting(world, 1, new PlaySound(0.1f, 0, "chr_goblin_pain", 2));
        var cues = Listen(goblin);

        goblin.ForceAnimation(Special);
        Run(world, 0.5f);

        var sound = Assert.IsType<SoundCue>(Assert.Single(cues));
        Assert.Equal(("chr_goblin_pain", 2), (sound.Cue, sound.Banks));
        Assert.Equal(goblin.Position.X, sound.Position.X, 2);
        Assert.Equal(-Vector3.UnitZ, sound.Facing);
    }

    [Fact]
    public void AnAttachedEffect_EndsWithItsWindow_AFreeOneDoesNot()
    {
        using var world = World();
        var sonic = new PlayEffect(0, 0.5f, "brute_sonic", "root", attach: true);
        var dust = new PlayEffect(0.2f, 0, "brute_dust", "LeftFootEnd", attach: false);
        var brute = Acting(world, 1, sonic, dust);
        var cues = Listen(brute);

        brute.ForceAnimation(Special);
        Run(world, 0.8f);

        Assert.Equal<AnimationCue>(
            [new EffectCue("brute_sonic", "root", true, sonic), new EffectCue("brute_dust", "LeftFootEnd", false, dust), new EffectEndCue(sonic)],
            cues);
    }

    [Fact]
    public void AFootstep_TellsTheGroundAndTheWeight()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Footstep(0, 0));
        var cues = Listen(goblin);

        goblin.ForceAnimation(Special);
        Run(world, 0.2f);

        var step = Assert.IsType<FootstepCue>(Assert.Single(cues));
        Assert.Equal(2, step.Material);                                            // the test floor's material
        Assert.Equal(75f, step.Mass, 1);
        Assert.Equal(0, step.Feet.Y, 1);
    }

    [Fact]
    public void NoFootstep_InTheAir()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Footstep(0, 0));
        var cues = Listen(goblin);
        goblin.Body.Jump(new Vector3(0, 7, 0));
        Run(world, 0.2f);

        goblin.ForceAnimation(Special);
        Run(world, Tick * 2);

        Assert.Empty(cues);
    }

    [Fact]
    public void ACameraShake_ComesFromTheCharacter()
    {
        using var world = World();
        var chieftain = Acting(world, 1, new CameraShake(0.4f, 0, 0.3f, 0.7f));
        var cues = Listen(chieftain);

        chieftain.ForceAnimation(Special);
        Run(world, 0.6f);

        var shake = Assert.IsType<CameraShakeCue>(Assert.Single(cues));
        Assert.Equal((0.7f, 0.3f), (shake.Magnitude, shake.Duration));
    }
}
