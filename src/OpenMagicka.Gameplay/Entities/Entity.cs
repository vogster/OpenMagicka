using System.Numerics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// The network name of an entity: the server gives it when the entity is added, and sends it in
/// the spawn message, so client and server agree without creating entities in the same order.
/// </summary>
public readonly record struct NetId(uint Value)
{
    public static NetId None => default;

    public bool IsNone => Value == 0;

    public override string ToString() => $"#{Value}";
}

/// <summary>
/// Anything that lives in the world (docs/engine/ENTITIES_AI.md §1). Entities are simulation
/// objects: they change in place while the manager updates them.
/// </summary>
public abstract class Entity
{
    /// <summary>Given by the manager when the entity is added; <see cref="NetId.None"/> outside one.</summary>
    public NetId Id { get; internal set; }

    /// <summary>The manager the entity is in, or null.</summary>
    public EntityManager? Manager { get; internal set; }

    /// <summary>The hash of the entity's name in the level script; 0 for none. A new name moves the manager's entry.</summary>
    public int UniqueId
    {
        get => uniqueId;
        set
        {
            var old = uniqueId;
            uniqueId = value;
            Manager?.Rename(this, old);
        }
    }

    private int uniqueId;

    public abstract Vector3 Position { get; }

    public abstract float Radius { get; }

    /// <summary>How fast the entity moves (a jump at it leads it); still things stand still.</summary>
    public virtual Vector3 Velocity => Vector3.Zero;

    public abstract bool IsDead { get; }

    /// <summary>The manager takes the entity out at the end of the frame.</summary>
    public abstract bool IsRemovable { get; }

    /// <summary>Whether the entity can take damage (protected queries keep only these).</summary>
    public virtual bool IsDamageable => false;

    /// <summary>How much the AI shies from it, and counts it in its own danger (ENTITIES_AI.md §7.7).</summary>
    public virtual float Danger => 0;

    /// <summary>Takes a hit from <paramref name="attacker"/>, struck from <paramref name="attackPosition"/>; things that cannot be hurt ignore it.</summary>
    public virtual Combat.DamageResult Damage(Combat.DamageEntry damage, Entity? attacker, Vector3 attackPosition) => Combat.DamageResult.None;

    /// <summary>A whole attack, entry by entry; Deflected only if every entry was.</summary>
    public virtual Combat.DamageResult Damage(IReadOnlyList<Combat.DamageEntry> entries, Entity? attacker, Vector3 attackPosition)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Combat.DamageRules.Combine([.. entries.Select(e => Damage(e, attacker, attackPosition))]);
    }

    /// <summary>Whether a segment, thickened by <paramref name="radius"/>, touches the entity (a melee sweep).</summary>
    public bool SegmentIntersect(Segment segment, float radius) => SegmentIntersect(segment, radius, out _);

    /// <summary>
    /// Whether a segment, thickened by <paramref name="radius"/>, touches the entity, and the point on
    /// its surface facing the segment's nearest point (a shot).
    /// </summary>
    public virtual bool SegmentIntersect(Segment segment, float radius, out Vector3 hit)
    {
        if (!(radius >= 0) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "must not be negative");
        }
        var centre = new Segment(Position, Vector3.Zero);
        var touches = Segment.DistanceSquared(segment, centre, out var s, out _) <= (radius + Radius) * (radius + Radius);
        var towards = segment.At(s) - Position;
        hit = Position + (towards.LengthSquared() > 1e-12f ? Vector3.Normalize(towards) * Radius : Vector3.Zero);
        return touches;
    }

    /// <summary>
    /// Where along the segment (0 to 1) it first enters the entity's shape, a sphere of its radius by
    /// default; null when it never does.
    /// </summary>
    public virtual float? Entry(Segment segment)
    {
        var from = segment.Origin - Position;
        var (a, b, c) = (segment.Delta.LengthSquared(), Vector3.Dot(from, segment.Delta), from.LengthSquared() - Radius * Radius);
        if (c <= 0)
        {
            return 0;
        }
        var root = b * b - a * c;
        if (a <= 1e-12f || root < 0)
        {
            return null;
        }
        var t = (-b - MathF.Sqrt(root)) / a;
        return t is >= 0 and <= 1 ? t : null;
    }

    /// <summary>One frame of the entity's own logic.</summary>
    public virtual void Update(float dt)
    {
    }

    /// <summary>Called when the manager takes the entity in.</summary>
    protected internal virtual void OnAdded()
    {
    }

    /// <summary>Called when the manager lets the entity go: its body should leave the physics world.</summary>
    protected internal virtual void OnRemoved()
    {
    }

    /// <summary>
    /// Thrown away from a point by the ballistic rule (ENTITIES_AI.md §1) for a strength and a distance;
    /// false when it did not move (a body too heavy, or none).
    /// </summary>
    public virtual bool Impulse(Vector3 from, float elevation, float power, float distance) => false;

    /// <summary>
    /// The arc test of melee and area spells (§1.6): within <paramref name="heightTolerance"/> in
    /// height, within <paramref name="range"/> of the origin counting the radius, and within
    /// <paramref name="angle"/> of <paramref name="direction"/> counting the half-width. The hit is on
    /// the surface facing the origin. The direction is flattened but not renormalised, and point blank
    /// (closer than r/2, where the half-width is undefined) misses, as in the original.
    /// </summary>
    public virtual bool ArcIntersect(Vector3 origin, Vector3 direction, float range, float angle, float heightTolerance, out Vector3 hit)
    {
        hit = default;
        var position = Position;
        if (MathF.Abs(position.Y - origin.Y) > heightTolerance)
        {
            return false;
        }
        var toOrigin = new Vector3(origin.X - position.X, 0, origin.Z - position.Z);
        var distance = toOrigin.Length();
        if (distance - Radius > range || distance <= 0)
        {
            return false;
        }
        var away = toOrigin / distance;
        var off = MathF.Acos(-Vector3.Dot(away, new Vector3(direction.X, 0, direction.Z)));
        var halfWidth = MathF.Acos(1 - Radius * Radius / (2 * distance * distance));
        if (!(off - halfWidth < angle))
        {
            return false;
        }
        hit = position + away * Radius;
        return true;
    }
}

/// <summary>A shield that hides what is behind it from protected queries (docs/engine/ENTITIES_AI.md §1.6).</summary>
public interface IShield
{
    Vector3 Position { get; }

    float Radius { get; }

    /// <summary>Only sphere shields protect what stands inside them.</summary>
    bool IsSphere { get; }

    /// <summary>Whether the segment hits the shield, within <paramref name="tolerance"/>, and where.</summary>
    bool Blocks(Vector3 from, Vector3 to, float tolerance, out Vector3 hit);
}
