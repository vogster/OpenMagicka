using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Magicka;

/// <summary>Animations available for one weapon class, in file order.</summary>
public sealed record AnimationSetContent(IReadOnlyList<NamedAnimation> Animations);

/// <summary><see cref="Animation"/> is an Animations enum name, e.g. "idle" or "attack_melee0".</summary>
public sealed record NamedAnimation(string Animation, AnimationClipActionContent Clip);

/// <summary>A clip of the skinned model played as an animation, with timed actions.</summary>
public sealed record AnimationClipActionContent(
    string ClipName, float Speed, float BlendTime, bool Loop, IReadOnlyList<AnimationActionContent> Actions);

public enum GripType : byte
{
    Pickup,
    Ride,
    Hold,
}

[Flags]
public enum DamageTargets : byte
{
    Target = 0,
    Friendly = 1,
    Enemy = 2,
    NonCharacters = 4,
    All = 255,
}

/// <summary>
/// Something an animation does between <see cref="StartTime"/> and <see cref="EndTime"/>
/// (fractions of the clip duration). One subtype per action class of the original.
/// </summary>
public abstract record AnimationActionContent(float StartTime, float EndTime);

public sealed record BlockAction(float StartTime, float EndTime, int Weapon) : AnimationActionContent(StartTime, EndTime);
public sealed record BreakFreeAction(float StartTime, float EndTime, float Magnitude, int Weapon) : AnimationActionContent(StartTime, EndTime);
public sealed record CameraShakeAction(float StartTime, float EndTime, float Duration, float Magnitude) : AnimationActionContent(StartTime, EndTime);

/// <summary><see cref="Source"/> is the casting bone, null when casting from the staff.</summary>
public sealed record CastSpellAction(float StartTime, float EndTime, string? Source) : AnimationActionContent(StartTime, EndTime);

/// <summary>The original raises a length of 0 or less to 0.01; the raw value is kept here.</summary>
public sealed record CrouchAction(float StartTime, float EndTime, float Radius, float Length) : AnimationActionContent(StartTime, EndTime);
public sealed record DamageGripAction(float StartTime, float EndTime, bool DamageToOwner, IReadOnlyList<DamageContent> Damages) : AnimationActionContent(StartTime, EndTime);
public sealed record DealDamageAction(float StartTime, float EndTime, int Weapon, DamageTargets Targets) : AnimationActionContent(StartTime, EndTime);
public sealed record DetachItemAction(float StartTime, float EndTime, int Item, Vector3 Velocity) : AnimationActionContent(StartTime, EndTime);
public sealed record EtherealAction(float StartTime, float EndTime, bool Ethereal, float Alpha, float Speed) : AnimationActionContent(StartTime, EndTime);
public sealed record FootstepAction(float StartTime, float EndTime) : AnimationActionContent(StartTime, EndTime);
public sealed record GripAction(float StartTime, float EndTime, GripType Type, float Radius, float BreakFreeTolerance,
    string GripAttachBone, string TargetAttachBone, bool FinishOnGrip) : AnimationActionContent(StartTime, EndTime);
public sealed record GunfireAction(float StartTime, float EndTime, int Weapon, float Accuracy) : AnimationActionContent(StartTime, EndTime);
public sealed record ImmortalAction(float StartTime, float EndTime, bool Collide) : AnimationActionContent(StartTime, EndTime);
public sealed record InvisibleAction(float StartTime, float EndTime, bool NoEffect) : AnimationActionContent(StartTime, EndTime);

/// <summary>Elevation in degrees; the ranges are optional.</summary>
public sealed record JumpAction(float StartTime, float EndTime, float Elevation, float? MinRange, float? MaxRange) : AnimationActionContent(StartTime, EndTime);

public sealed record MoveAction(float StartTime, float EndTime, Vector3 Velocity) : AnimationActionContent(StartTime, EndTime);
public sealed record OverkillGripAction(float StartTime, float EndTime) : AnimationActionContent(StartTime, EndTime);
public sealed record PlayEffectAction(float StartTime, float EndTime, string Bone, bool Attach, string Effect) : AnimationActionContent(StartTime, EndTime);
public sealed record PlaySoundAction(float StartTime, float EndTime, string Cue, SoundBanks Banks) : AnimationActionContent(StartTime, EndTime);
public sealed record ReleaseGripAction(float StartTime, float EndTime) : AnimationActionContent(StartTime, EndTime);
public sealed record RemoveStatusAction(float StartTime, float EndTime, string StatusEffect) : AnimationActionContent(StartTime, EndTime);
public sealed record SetItemAttachAction(float StartTime, float EndTime, int Item, string Bone) : AnimationActionContent(StartTime, EndTime);
public sealed record SpawnMissileAction(float StartTime, float EndTime, int Weapon, Vector3 Velocity, bool ItemAligned) : AnimationActionContent(StartTime, EndTime);

/// <summary>Uses the special ability of a weapon, or the inline <see cref="Ability"/> when the weapon index is negative.</summary>
public sealed record SpecialAbilityAction(float StartTime, float EndTime, int Weapon, SpecialAbilityContent? Ability) : AnimationActionContent(StartTime, EndTime);

public sealed record SuicideAction(float StartTime, float EndTime, bool Overkill) : AnimationActionContent(StartTime, EndTime);
public sealed record ThrowGripAction(float StartTime, float EndTime) : AnimationActionContent(StartTime, EndTime);
public sealed record TongueAction(float StartTime, float EndTime, float MaxLength) : AnimationActionContent(StartTime, EndTime);
public sealed record WeaponVisibilityAction(float StartTime, float EndTime, int Weapon, bool Visible) : AnimationActionContent(StartTime, EndTime);
