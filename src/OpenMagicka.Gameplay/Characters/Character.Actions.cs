using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics.Characters;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>What the animation actions change on a character (docs/engine/ENTITIES_AI.md §4.2).</summary>
public sealed partial class Character
{
    /// <summary>Ethereal characters come out of these statuses.</summary>
    private const StatusEffects EtherealCures = StatusEffects.Burning | StatusEffects.Wet | StatusEffects.Frozen | StatusEffects.Cold
        | StatusEffects.Poisoned | StatusEffects.Healing | StatusEffects.Greased | StatusEffects.Steamed;

    private float invisibleTime;
    private float intangibleTime;
    private float alphaTarget = 1;
    private float alphaSpeed = 1;
    private float alpha = 1;

    /// <summary>Presentation the animation asks for (sounds, effects, footsteps, camera shakes), for the engine.</summary>
    public event Action<Character, AnimationCue>? Cue;

    /// <summary>What the character is after (the AI's target; a jump leaps at it).</summary>
    public Entity? Target { get; set; }

    /// <summary>The equipment slot blocking this frame, or −1; a block action sets it for its window.</summary>
    public int BlockSlot { get; set; } = -1;

    public bool IsInvisible => invisibleTime > 0;

    /// <summary>Not drawn at all (an action's "no effect" invisibility).</summary>
    public bool IsHidden { get; set; }

    /// <summary>The transparency the renderer draws with, easing towards its target; setting it sets both.</summary>
    public float EtherealAlpha
    {
        get => alpha;
        set => alpha = alphaTarget = Finite(value, nameof(EtherealAlpha));
    }

    /// <summary>Passes through everything but the level: ethereal, or briefly after an immortal action.</summary>
    public bool IsIntangible => IsEthereal || intangibleTime > 0;

    /// <summary>Becomes ethereal or solid, easing the transparency to <paramref name="target"/>; ethereal ends most statuses.</summary>
    public void Ethereal(bool ethereal, float target, float speed)
    {
        (alphaTarget, alphaSpeed) = (Finite(target, nameof(target)), Finite(speed, nameof(speed)));
        IsEthereal = ethereal;
        if (ethereal)
        {
            StopStatuses(EtherealCures);
        }
    }

    /// <summary>Invisible for this long (0 ends it); turning invisible drops the self-shield.</summary>
    public void SetInvisible(float seconds)
    {
        invisibleTime = MathF.Max(0, NotNaN(seconds, nameof(seconds)));
        if (invisibleTime > 0)
        {
            RemoveSelfShield();
        }
    }

    public void RemoveInvisibility() => invisibleTime = 0;

    /// <summary>An item's Invisibility was just used: its own cast animation does not end it (§8.1.7).</summary>
    public bool JustCastInvisible { get; set; }

    /// <summary>Ends the given statuses at once.</summary>
    public void StopStatuses(StatusEffects types) => Statuses = Statuses.Without(types);

    /// <summary>Immune for exactly this long (an action's window: set, not added; 0 ends it).</summary>
    internal void SetImmortalTime(float seconds) => immortalTime = Finite(seconds, nameof(seconds));

    /// <summary>Passes through others for this long.</summary>
    internal void IgnoreCollisions(float seconds) => intangibleTime = Finite(seconds, nameof(seconds));

    /// <summary>A crouch: the capsule changes (each size at least 0.01), the feet stay.</summary>
    public void SetCapsule(float radius, float length)
    {
        (radius, length) = (MathF.Max(radius, CharacterShape.MinLength), MathF.Max(length, CharacterShape.MinLength));
        if (radius != Body.Shape.Radius || length != Body.Shape.Length)
        {
            Body.Reshape(radius, length);
        }
    }

    /// <summary>Back to the template's capsule.</summary>
    public void ResetCapsule() => SetCapsule(Template.Radius, Template.Length);

    /// <summary>
    /// A ballistic jump to land <paramref name="delta"/> away at <paramref name="elevation"/> radians;
    /// nothing when entangled, dead, or when no speed reaches it.
    /// </summary>
    public void Jump(Vector3 delta, float elevation)
    {
        if (IsEntangled || Dead)
        {
            return;
        }
        var flat = delta with { Y = 0 };
        var distance = flat.Length();
        if (distance < float.Epsilon)
        {
            return;
        }
        var (sin, cos) = MathF.SinCos(elevation);
        var direction = (flat / distance * cos) with { Y = sin };
        var gravity = MathF.Abs(world.Settings.Gravity.Y);
        var speed = MathF.Sqrt(gravity * distance * distance / (2 * (distance * sin / cos - delta.Y) * cos * cos));
        if (float.IsFinite(speed))
        {
            Body.Jump(direction * speed);
        }
    }

    internal void RaiseCue(AnimationCue cue)
    {
        Cue?.Invoke(this, cue);
        Manager?.RaiseCue(this, cue);
    }

    /// <summary>The timers the actions set count down before them, at the start of the frame.</summary>
    private void CountDownActionTimers(float dt)
    {
        intangibleTime = MathF.Max(0, intangibleTime - dt);
        invisibleTime = MathF.Max(0, invisibleTime - dt);
    }

    /// <summary>The transparency eases after the actions, as the original (a step never overshoots).</summary>
    private void UpdateLooks(float dt) => alpha += (alphaTarget - alpha) * Math.Clamp(alphaSpeed * dt, 0, 1);

    private static float NotNaN(float value, string name) =>
        float.IsNaN(value) ? throw new ArgumentOutOfRangeException(name, value, "must be a number") : value;
}
