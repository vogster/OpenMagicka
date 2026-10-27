using System.Numerics;

namespace OpenMagicka.Gameplay.Animation;

/// <summary>Presentation an animation asks for (docs/engine/ENTITIES_AI.md §4.2); the engine plays it.</summary>
public abstract record AnimationCue;

/// <summary>The original's sound banks, as a cue names them (its <c>Banks</c> flags).</summary>
public static class SoundBank
{
    public const int UI = 0x8, Spells = 0x10, Characters = 0x20, Weapons = 0x80, Misc = 0x100, Additional = 0x200;
}

/// <param name="Banks">The original's sound bank flags.</param>
/// <param name="Magnitude">The cue's magnitude variable, when the source sets one.</param>
public sealed record SoundCue(string Cue, int Banks, Vector3 Position, Vector3 Facing, float? Magnitude = null) : AnimationCue
{
    /// <summary>The sound goes with its source while it plays (a spell's, from its caster); else it stays where it rose.</summary>
    public bool Follow { get; init; }
}

/// <summary>A decal on the level: where, its surface's normal, the way it faces and its scale.</summary>
public sealed record DecalCue(string Decal, Vector3 Position, Vector3 Normal, Vector3 Direction, float Scale) : AnimationCue;

/// <summary>The cue's sounds playing from the source stop (a charge's, when the charge ends).</summary>
public sealed record SoundStopCue(string Cue) : AnimationCue;

/// <summary>
/// An effect at a bone; an attached one follows the bone until its <see cref="EffectEndCue"/> (same
/// character and source). A looping clip can start one again before it ended: the new one replaces it.
/// </summary>
public sealed record EffectCue(string Effect, string Bone, bool Attached, AnimationAction Source) : AnimationCue;

/// <summary>The attached effect of this source ends (one never started may end too).</summary>
public sealed record EffectEndCue(AnimationAction Source) : AnimationCue;

/// <summary>
/// An element was conjured: whether the queue took it (it joined, merged or cancelled; a full queue may refuse it), and
/// its changes step by step, for the HUD's sound and animation.
/// </summary>
public sealed record ConjureCue(Spells.Elements Element, IReadOnlyList<Spells.QueueChange> Changes, bool Taken = true) : AnimationCue;

/// <summary>A step on the ground: the sound and dust come from the material.</summary>
public sealed record FootstepCue(int Material, float Mass, Vector3 Feet, Vector3 Facing) : AnimationCue;

public sealed record CameraShakeCue(Vector3 Position, float Magnitude, float Duration) : AnimationCue;

/// <summary>A visual effect started once at a point in the world (a magick's flash, a marker), facing a direction.</summary>
public sealed record WorldEffectCue(string Effect, Vector3 Position, Vector3 Direction) : AnimationCue;

/// <summary>
/// An item's or a missile's effect (a strike's sparks, a missile's trail); a following one stays on
/// its source until its <see cref="ItemEffectEndCue"/>.
/// </summary>
public sealed record ItemEffectCue(string Effect, Vector3 Position, Vector3 Direction, bool Follow, object Source) : AnimationCue;

/// <summary>The source's following effects end (the strike ends, the missile is gone).</summary>
public sealed record ItemEffectEndCue(object Source) : AnimationCue;

/// <summary>A gun starts or stops its burst: its sound, and its shells at attach1.</summary>
public sealed record GunfireCue(Items.Item Source, bool Firing) : AnimationCue;

/// <summary>A gun's muzzle flash at attach0.</summary>
public sealed record MuzzleCue(Items.Item Source) : AnimationCue;

/// <summary>
/// A tracer along a shot, drawn after <paramref name="Delay"/> seconds (none or less: now). The
/// sprite is drawn as a byte, as the original, so 256 and up wrap.
/// </summary>
public sealed record TracerCue(Vector3 From, Vector3 To, float Speed, int Sprite, float Delay) : AnimationCue;
