using System.Numerics;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// How a mine or barrier grows the next one of its chain (docs/gameplay/SPELLS.md §5.6): the next
/// stands <see cref="Next"/> away, which turns by <see cref="Turn"/> at each link, while the chain's
/// <see cref="Range"/> lasts; each link spends <see cref="Spacing"/> of it.
/// </summary>
public readonly record struct ChainStep(Vector3 Next, Quaternion Turn, float Range, float Spacing)
{
    /// <summary>What a link placed with this step hands on: its next offset turned once, its range less a spacing.</summary>
    public ChainStep Spent() => this with { Next = Vector3.Transform(Next, Turn), Range = Range - Spacing };

    /// <summary>Whether a next link is still to come.</summary>
    public bool Continues => Range > float.Epsilon;
}

/// <summary>Finding the ground for a chain's link, and clearing its spot.</summary>
public static class ShieldChain
{
    /// <summary>A link's ground is looked for from this high above where it should be, this far down.</summary>
    public const float ProbeUp = 0.75f, ProbeDown = 1.5f;

    /// <summary>A layout's first links are looked for this far down.</summary>
    public const float FirstProbeDown = 4;

    /// <summary>The level under a point, looking down from it; null with no floor within the depth.</summary>
    public static Vector3? Ground(PhysicsWorld world, Vector3 from, float depth) =>
        world.IsDisposed ? null : world.CastSegment(from, from - Vector3.UnitY * depth, levelOnly: true)?.Position;

    /// <summary>
    /// The ground for the link after one standing at <paramref name="position"/>: under
    /// <paramref name="probe"/> (the spot the step points to, <see cref="ProbeUp"/> higher), unless a
    /// shield stands between the two or there is no floor.
    /// </summary>
    public static Vector3? NextGround(EntityManager manager, PhysicsWorld world, Vector3 position, Vector3 probe)
    {
        var path = new Segment(position, probe - position);
        if (manager.Shields.OfType<Entity>().Any(s => !s.IsDead && s.SegmentIntersect(path, 1)))
        {
            return null;
        }
        return Ground(world, probe, ProbeDown);
    }

    /// <summary>
    /// Clears a new link's spot: barriers of other casts within the radius break, mines go off
    /// (<paramref name="hits"/>: the new link's cast, whose barriers stay; null for a mine, before which all go).
    /// </summary>
    public static void Clear(EntityManager manager, Vector3 at, float radius, BarrierHits? hits)
    {
        foreach (var entity in manager.Query(at, radius))
        {
            if (entity is Barrier barrier && (hits is null || !ReferenceEquals(barrier.Hits, hits)))
            {
                barrier.Kill();
            }
            else if (entity is SpellMine mine)
            {
                mine.Detonate();
            }
        }
    }

    /// <summary>A flat direction's quarter turn: the facing of a line's links.</summary>
    public static Vector3 Across(Vector3 facing) => new(facing.Z, 0, -facing.X);

    /// <summary>A turn about the vertical.</summary>
    public static Quaternion Yaw(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
}
