using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Characters;
using Actions = OpenMagicka.Gameplay.Animation.Actions;
using GameStatus = OpenMagicka.Gameplay.Combat.StatusEffects;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace OpenMagicka.Tests.Engine;

/// <summary>Template action records made into gameplay actions (ENTITIES_AI.md §4.2).</summary>
public class CharacterAnimationActionsTests
{
    [Fact]
    public void TheWindowAndParameters_CarryOver()
    {
        var move = Assert.IsType<Actions.Move>(CharacterAnimationActions.Create(new MoveAction(0.25f, 0.8f, new XnaVector3(1, 2, -9))));
        Assert.Equal((0.25f, 0.8f), (move.Start, move.End));
        Assert.Equal(new System.Numerics.Vector3(1, 2, -9), move.Velocity);

        var jump = Assert.IsType<Actions.Jump>(CharacterAnimationActions.Create(new JumpAction(0.9f, 0, 60, 10, null)));
        Assert.Equal(MathF.PI / 3, jump.Elevation, 5);                             // degrees in the data
        Assert.Equal((10f, (float?)null), (jump.MinRange!.Value, jump.MaxRange));

        var effect = Assert.IsType<Actions.PlayEffect>(CharacterAnimationActions.Create(new PlayEffectAction(0, 0, "LeftFootEnd", true, "brute_dust")));
        Assert.Equal(("brute_dust", "LeftFootEnd", true), (effect.Effect, effect.Bone, effect.Attach));

        var sound = Assert.IsType<Actions.PlaySound>(CharacterAnimationActions.Create(new PlaySoundAction(0, 0, "chr_goblin_pain", SoundBanks.Characters)));
        Assert.Equal(("chr_goblin_pain", 0x20), (sound.Cue, sound.Banks));

        var shake = Assert.IsType<Actions.CameraShake>(CharacterAnimationActions.Create(new CameraShakeAction(0.4f, 0, 0.3f, 0.7f)));
        Assert.Equal((0.3f, 0.7f), (shake.Duration, shake.Magnitude));
    }

    [Fact]
    public void EveryKindThatWorksNow_IsMade()
    {
        AnimationActionContent[] kinds =
        [
            new CrouchAction(0, 1, 0.5f, 0), new EtherealAction(0, 0, true, 0, 0), new InvisibleAction(0, 1, false),
            new ImmortalAction(0, 0.75f, false), new SuicideAction(0, 0, true), new BlockAction(0, 1, 0),
            new BreakFreeAction(0.8f, 0, 1, 0), new FootstepAction(0.3f, 0), new RemoveStatusAction(0.5f, 0, "wet"), new WeaponVisibilityAction(0, 0, 0, true),
            new DealDamageAction(0, 1, 0, DamageTargets.Enemy), new GunfireAction(0, 1, 0, 1),
            new SpawnMissileAction(0, 0, 0, default, false), new DetachItemAction(0, 0, 0, default),
        ];

        Assert.All(kinds, k => Assert.NotNull(CharacterAnimationActions.Create(k)));
    }

    [Theory]
    [InlineData("wet", GameStatus.Wet)]
    [InlineData("COLD", GameStatus.Cold)]
    [InlineData("life", GameStatus.Healing)]                                      // the data's other name for healing
    public void StatusNames_IgnoreCase(string name, GameStatus status)
    {
        var remove = Assert.IsType<Actions.RemoveStatus>(CharacterAnimationActions.Create(new RemoveStatusAction(0, 0, name)));

        Assert.Equal(status, remove.Status);
    }

    [Fact]
    public void AnUnknownStatus_IsBadData() =>
        Assert.Throws<InvalidDataException>(() => CharacterAnimationActions.Create(new RemoveStatusAction(0, 0, "sleepy")));

    [Fact]
    public void KindsForLaterSystems_AreLeftOut()
    {
        AnimationActionContent[] later =
        [

            new CastSpellAction(0, 0, null), new SpecialAbilityAction(0, 0, 0, null),
            new SetItemAttachAction(0, 0, 0, "hand"), new GripAction(0, 1, GripType.Hold, 1, 1, "a", "b", false),
            new DamageGripAction(0, 0, false, []), new ReleaseGripAction(0, 0), new ThrowGripAction(0, 0), new OverkillGripAction(0, 0),
            new TongueAction(0, 0, 5),
        ];

        Assert.All(later, k => Assert.Null(CharacterAnimationActions.Create(k)));
    }

    [Fact]
    public void ANonFiniteValue_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAnimationActions.Create(new MoveAction(0, 1, new XnaVector3(float.NaN, 0, 0))));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAnimationActions.Create(new FootstepAction(float.PositiveInfinity, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAnimationActions.Create(new EtherealAction(0, 0, true, 1, -1)));
    }
}
