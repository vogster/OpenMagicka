using System.Numerics;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// A scripted step of an order (ENTITIES_AI.md §7.6, Idle): each has a trigger to fire (0 for none)
/// and a delay after it (for a clip, none waits for the clip to end).
/// </summary>
public abstract record AiEvent(int Trigger, float Delay);

/// <summary>
/// Walk to a point (a locator's, or a random one in a zone, the level resolves), at a share of the
/// speed; with a fixed direction, turn that way on arrival.
/// </summary>
public sealed record MoveAiEvent(int Trigger, float Delay, Vector3 Waypoint, float Speed = 1, bool FixedDirection = false, Vector3 Direction = default)
    : AiEvent(Trigger, Delay);

/// <summary>Play a clip (and, when given, take an idle clip of its own).</summary>
public sealed record AnimationAiEvent(int Trigger, float Delay, string Animation, float BlendTime = 0, string? IdleAnimation = null)
    : AiEvent(Trigger, Delay);

/// <summary>Turn toward an entity or locator (by its script name), or along a direction.</summary>
public sealed record FaceAiEvent(int Trigger, float Delay, int TargetId = 0, Vector3 Direction = default, float Speed = 1)
    : AiEvent(Trigger, Delay);

/// <summary>Die (or, removing, be taken out of the level).</summary>
public sealed record KillAiEvent(int Trigger, float Delay, bool Remove = true) : AiEvent(Trigger, Delay);

/// <summary>Start the events over (reversed: the ones before this turned around first).</summary>
public sealed record LoopAiEvent(int Trigger, float Delay, bool Reverse = false) : AiEvent(Trigger, Delay);

/// <summary>A level script's <c>giveOrder</c> (§7.10), as the triggers hand it to the minds.</summary>
public sealed record GiveOrderSpec(
    Order Order,
    ReactTo ReactTo = ReactTo.None,
    Order Reaction = Order.None,
    int PriorityTarget = 0,
    int PriorityAbility = -1,
    int Trigger = 0,
    IReadOnlyList<AiEvent>? Events = null,
    int TargetArea = 0,
    float WanderSpeed = 1,
    string? Animation = null,
    string? IdleAnimation = null);
