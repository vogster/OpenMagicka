using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// An Earth weapon cast's underground wave: it runs along the floor, hitting each thing near it once,
/// until its range is spent or the floor ends (docs/gameplay/SPELLS.md §5.3). Its looks come with
/// the renderer, which follows its position. A simulation object.
/// </summary>
public sealed class GroundWave : Entity
{
    /// <summary>How near the wave strikes.</summary>
    public const float Reach = 1;

    /// <summary>A floor is no steeper than this (its normal's height).</summary>
    public const float Floor = 0.7f;

    private readonly PhysicsWorld world;
    private readonly IReadOnlyList<DamageEntry> damage;
    private readonly HashSet<Entity> hit = new(ReferenceEqualityComparer.Instance);
    private readonly Vector3 velocity;
    private Vector3 position;
    private float rangeLeft;

    private GroundWave(PhysicsWorld world, Character owner, Vector3 floor, Vector3 velocity, float range, IReadOnlyList<DamageEntry> damage)
    {
        (this.world, Owner, position, this.velocity, rangeLeft, this.damage) = (world, owner, floor, velocity, range, damage);
        hit.Add(owner);
    }

    public Character Owner { get; }

    /// <summary>Whom it reached, the caster first (never struck).</summary>
    public IReadOnlyCollection<Entity> HitList => hit;

    public override Vector3 Position => position;

    public override float Radius => Reach;

    public override bool IsDead => rangeLeft <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Starts a wave from a point along a flat direction; null when there is no floor under it or no room.</summary>
    public static GroundWave? Launch(Character owner, Vector3 from, Vector3 direction, float speed, float range, IReadOnlyList<DamageEntry> damage)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(damage);
        var flat = direction with { Y = 0 };
        if (owner.Manager is not { } manager || owner.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity || flat.LengthSquared() <= 1e-12f
            || owner.World.CastSegment(from + Vector3.UnitY, from - 3 * Vector3.UnitY, levelOnly: true) is not { Normal.Y: >= Floor } floor)
        {
            return null;
        }
        var wave = new GroundWave(owner.World, owner, floor.Position, Vector3.Normalize(flat) * speed, range, damage);
        manager.Add(wave);
        return wave;
    }

    /// <summary>One step along the floor: a drop, a steep slope, a shield or a wall ends it.</summary>
    public override void Update(float dt)
    {
        if (IsDead || world.IsDisposed || Manager is not { } manager)
        {
            return;
        }
        var step = velocity * dt;
        var ahead = position + step;
        if (world.CastSegment(ahead + Vector3.UnitY, ahead - Vector3.UnitY, levelOnly: true) is not { Normal.Y: >= Floor } floor)
        {
            rangeLeft = 0;
            return;
        }
        var move = new Segment(position, floor.Position - position);
        if (manager.Shields.Any(s => s.Blocks(move.Origin, move.Origin + move.Delta, Reach, out _))
            || world.CastSegment(position + Vector3.UnitY, floor.Position + Vector3.UnitY, levelOnly: true) is not null)
        {
            rangeLeft = 0;
            return;
        }
        position = floor.Position;
        rangeLeft -= step.Length();
        foreach (var entity in manager.Query(position, Reach, ignoreProtected: true))
        {
            if (entity.IsDamageable && hit.Add(entity))
            {
                entity.Damage(damage, Owner, position);
            }
        }
    }
}
