using System.Numerics;

namespace OpenMagicka.Gameplay.Animation;

/// <summary>Presentation an animation asks for (docs/engine/ENTITIES_AI.md §4.2); the engine plays it.</summary>
public abstract record AnimationCue;

/// <param name="Banks">The original's sound bank flags.</param>
/// <param name="Magnitude">The cue's magnitude variable, when the source sets one.</param>
public sealed record SoundCue(string Cue, int Banks, Vector3 Position, Vector3 Facing, float? Magnitude = null) : AnimationCue;

/// <summary>
/// An effect at a bone; an attached one follows the bone until its <see cref="EffectEndCue"/> (same
/// character and source). A looping clip can start one again before it ended: the new one replaces it.
/// </summary>
public sealed record EffectCue(string Effect, string Bone, bool Attached, AnimationAction Source) : AnimationCue;

/// <summary>The attached effect of this source ends (one never started may end too).</summary>
public sealed record EffectEndCue(AnimationAction Source) : AnimationCue;

/// <summary>A step on the ground: the sound and dust come from the material.</summary>
public sealed record FootstepCue(int Material, float Mass, Vector3 Feet, Vector3 Facing) : AnimationCue;

public sealed record CameraShakeCue(Vector3 Position, float Magnitude, float Duration) : AnimationCue;

/// <summary>An item's effect (a strike's sparks); a following one stays on the item until its <see cref="ItemEffectEndCue"/>.</summary>
public sealed record ItemEffectCue(string Effect, Vector3 Position, Vector3 Direction, bool Follow, Items.Item Source) : AnimationCue;

/// <summary>The item's following effects end (with the strike).</summary>
public sealed record ItemEffectEndCue(Items.Item Source) : AnimationCue;

/// <summary>A gun starts or stops its burst: its sound, and its shells at attach1.</summary>
public sealed record GunfireCue(Items.Item Source, bool Firing) : AnimationCue;

/// <summary>A gun's muzzle flash at attach0.</summary>
public sealed record MuzzleCue(Items.Item Source) : AnimationCue;

/// <summary>
/// A tracer along a shot, drawn after <paramref name="Delay"/> seconds (none or less: now). The
/// sprite is drawn as a byte, as the original, so 256 and up wrap.
/// </summary>
public sealed record TracerCue(Vector3 From, Vector3 To, float Speed, int Sprite, float Delay) : AnimationCue;
