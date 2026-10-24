using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Props;

/// <summary>A prop's box: a corner, its sides along its own axes, and its turn, in the prop's space.</summary>
public sealed record PropBox(Vector3 Corner, Vector3 Sides, Quaternion Orientation)
{
    public static PropBox None { get; } = new(Vector3.Zero, Vector3.Zero, Quaternion.Identity);

    public Vector3 Centre => Corner + Vector3.Transform(Sides / 2, Orientation);
}

/// <summary>A piece a broken prop flies apart into (its model, for the renderer).</summary>
public sealed record PropGib(string Model, float Mass, float Scale);

/// <summary>An effect a prop carries, placed in its space.</summary>
public sealed record PropEffect(string Effect, Matrix4x4 Transform);

/// <summary>
/// A physics prop's template (docs/engine/ENTITIES_AI.md §8.4, <c>Data/PhysicsEntities</c>): its box and
/// mass; whether it moves (else it stands for good), is pushed by characters, and stops entities (else
/// they pass through, setting off its hit events); its hit points (none: it cannot be hurt), its
/// resistances, the pieces it breaks into, the effects it carries and its event groups.
/// </summary>
public sealed record PropTemplate(
    string Id,
    bool Movable,
    bool Pushable,
    bool Solid,
    float Mass,
    float MaxHitPoints,
    ResistanceTable Resistances,
    PropBox Box,
    IReadOnlyList<PropGib> Gibs,
    string GibTrailEffect,
    IReadOnlyList<PropEffect> Effects,
    IReadOnlyList<EventGroup> Events,
    bool CanHaveStatus = true,
    string HitEffect = "",
    string HitSound = "")
{
    /// <summary>A prop with hit points takes damage and breaks.</summary>
    public bool IsDamageable => MaxHitPoints > 0;

    /// <summary>Throws on a template no prop could be made of (loaders call it as they read).</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrEmpty(Id);
        if (!(Mass > 0) || !float.IsFinite(Mass))
        {
            throw new ArgumentOutOfRangeException(nameof(Mass), Mass, $"{Id}: the mass must be positive");
        }
        if (!float.IsFinite(MaxHitPoints) || MaxHitPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHitPoints), MaxHitPoints, $"{Id}: the hit points must not be negative");
        }
        var s = Box.Sides;
        if (!float.IsFinite(s.X + s.Y + s.Z + Box.Corner.X + Box.Corner.Y + Box.Corner.Z) || s.X < 0 || s.Y < 0 || s.Z < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Box), Box, $"{Id}: the box must be finite, its sides not negative");
        }
        var turn = Box.Orientation;
        if (!float.IsFinite(turn.X + turn.Y + turn.Z + turn.W) || turn.LengthSquared() < 1e-12f)
        {
            throw new ArgumentOutOfRangeException(nameof(Box), Box, $"{Id}: the box's turn must be a finite rotation");
        }
        foreach (var group in Events)
        {
            group.Validate();
        }
    }
}
