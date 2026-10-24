using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A shield standing in the world: a disc, a sphere or a wall that blocks characters, missiles and
/// spells and wears away (docs/gameplay/SPELLS.md §5.6). A simulation object.
/// </summary>
public sealed class SpellShield : Entity, IShield
{
    public const float MaxHitPoints = ShieldRules.ShieldMaxHp;

    /// <summary>A disc is the shell within this angle of its facing.</summary>
    public const float DiscAngle = MathF.PI / 4;

    /// <summary>A wall's box: half its thickness, height and length.</summary>
    public static readonly Vector3 WallHalfSize = new(0.2f, 3, 6);

    /// <summary>A shield that overlaps a new one goes at once; the new one this much later.</summary>
    public const float OverlapKillDelay = 0.25f;

    private const int Slices = 16, Stacks = 8;

    private readonly PhysicsWorld world;
    private StaticMesh? mesh;
    private float decayTimer;
    private float killTimer;
    private bool dead;

    public SpellShield(PhysicsWorld world, Character? owner, ShieldShape shape, Vector3 position, Vector3 facing, float radius, float hitPoints)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        if (!float.IsFinite(hitPoints) || !(radius > 0) || !float.IsFinite(radius) || !float.IsFinite(position.X + position.Y + position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(hitPoints), "a shield needs finite hit points, position and a positive radius");
        }
        var flat = facing with { Y = 0 };
        (Owner, Shape, Centre, Facing, ShellRadius, HitPoints) =
            (owner, shape, position, flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : -Vector3.UnitZ, radius, MathF.Min(hitPoints, MaxHitPoints));
    }

    public Character? Owner { get; }

    public ShieldShape Shape { get; }

    public Vector3 Facing { get; }

    public float ShellRadius { get; }

    public float HitPoints { get; private set; }

    private Vector3 Centre { get; }

    public override Vector3 Position => Centre;

    public override float Radius => Shape == ShieldShape.Wall ? WallHalfSize.Z : ShellRadius;

    public bool IsSphere => Shape == ShieldShape.Sphere;

    public override bool IsDamageable => true;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    /// <summary>What it lets through right now (going down: everything).</summary>
    public bool IsPassable => killTimer > 0 || HitPoints <= 0;

    private float CapAngle => Shape == ShieldShape.Disc ? DiscAngle : MathF.PI;

    /// <summary>Takes attacks that carry Damage or Status (amount × magnitude, when not negative); deflects the rest.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if ((damage.Attack & (AttackProperties.Damage | AttackProperties.Status)) == 0)
        {
            return DamageResult.Deflected;
        }
        if (damage.Amount * damage.Magnitude >= 0)
        {
            HitPoints -= damage.Amount * damage.Magnitude;
        }
        return DamageResult.Hit;
    }

    /// <summary>Mends it (a boost); its most comes back with its next frame.</summary>
    public void Repair(float amount)
    {
        if (!dead && HitPoints > 0 && float.IsFinite(amount) && amount > 0)
        {
            HitPoints += amount;
        }
    }

    /// <summary>Ends it: it lets characters through at once and goes on its next frame.</summary>
    public void Kill()
    {
        HitPoints = 0;
        if (mesh is not null)
        {
            mesh.Passable = true;
        }
    }

    /// <summary>Its hit points wear away by 10 every 0.1 s; an overlap's delay runs out; gone at no hit points.</summary>
    public override void Update(float dt)
    {
        if (dead)
        {
            return;
        }
        if (killTimer > 0)
        {
            killTimer -= dt;
            if (killTimer <= 0)
            {
                HitPoints = 0;
            }
        }
        decayTimer -= dt;
        while (decayTimer < 0)
        {
            decayTimer += 0.1f;
            HitPoints -= 10;
        }
        HitPoints = MathF.Min(HitPoints, MaxHitPoints);
        if (mesh is not null)
        {
            mesh.Passable = IsPassable;
        }
        dead = HitPoints <= 0;
    }

    public bool Blocks(Vector3 from, Vector3 to, float tolerance, out Vector3 hit) => Crossing(new Segment(from, to - from), tolerance, out hit) is not null;

    public override bool SegmentIntersect(Segment segment, float radius, out Vector3 hit) => Crossing(segment, radius, out hit) is not null;

    public override float? Entry(Segment segment) => Crossing(segment, 0, out _);

    /// <summary>An arc test is a segment along the direction for the range, thickened by 0.5.</summary>
    public override bool ArcIntersect(Vector3 origin, Vector3 direction, float range, float angle, float heightTolerance, out Vector3 hit) =>
        Crossing(new Segment(origin, direction * range), 0.5f, out hit) is not null;

    /// <summary>Where on the shell the segment first meets it within the cap, or where it enters the wall's box.</summary>
    private float? Crossing(Segment segment, float tolerance, out Vector3 hit)
    {
        hit = default;
        if (Shape == ShieldShape.Wall)
        {
            return BoxEntry(segment, tolerance, out hit);
        }
        var to = Centre - segment.Origin;
        var (a, b, c) = (segment.Delta.LengthSquared(), Vector3.Dot(segment.Delta, to), to.LengthSquared() - ShellRadius * ShellRadius);
        var root = b * b - a * c;
        if (a <= 1e-12f || root < 0)
        {
            return null;
        }
        var sqrt = MathF.Sqrt(root);
        float? best = null;
        foreach (var t in new[] { (b - sqrt) / a, (b + sqrt) / a })
        {
            if (t is > 0 and < 1 && Angle(segment.At(t) - Centre) <= CapAngle && (best is null || t < best))
            {
                best = t;
            }
        }
        if (best is { } first)
        {
            hit = segment.At(first);
        }
        return best;
    }

    private float Angle(Vector3 off) =>
        off.LengthSquared() <= 1e-12f ? 0 : MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(off), Facing), -1, 1));

    /// <summary>The slab test against the wall's box (inflated by the tolerance), in the wall's own axes.</summary>
    private float? BoxEntry(Segment segment, float tolerance, out Vector3 hit)
    {
        hit = default;
        var (right, up, along) = Axes();
        var origin = segment.Origin - Centre;
        var o = new Vector3(Vector3.Dot(origin, right), Vector3.Dot(origin, up), Vector3.Dot(origin, along));
        var d = new Vector3(Vector3.Dot(segment.Delta, right), Vector3.Dot(segment.Delta, up), Vector3.Dot(segment.Delta, along));
        var half = WallHalfSize + new Vector3(tolerance);
        var (enter, leave) = (0f, 1f);
        for (var i = 0; i < 3; i++)
        {
            var (oi, di, hi) = (o[i], d[i], half[i]);
            if (MathF.Abs(di) < 1e-9f)
            {
                if (MathF.Abs(oi) > hi)
                {
                    return null;
                }
                continue;
            }
            var (t0, t1) = ((-hi - oi) / di, (hi - oi) / di);
            (enter, leave) = (MathF.Max(enter, MathF.Min(t0, t1)), MathF.Min(leave, MathF.Max(t0, t1)));
            if (enter > leave)
            {
                return null;
            }
        }
        hit = segment.At(enter);
        return enter;
    }

    private (Vector3 Right, Vector3 Up, Vector3 Along) Axes() => (Vector3.Normalize(Vector3.Cross(Facing, Vector3.UnitY)), Vector3.UnitY, Facing);

    /// <summary>Stands in the world; an older shield it overlaps goes at once, and this one after a delay.</summary>
    protected internal override void OnAdded()
    {
        if (Manager is { } manager)
        {
            foreach (var other in manager.Shields.OfType<SpellShield>().Where(s => !ReferenceEquals(s, this) && s.HitPoints > 0 && Overlaps(s)).ToList())
            {
                other.Kill();
                killTimer = OverlapKillDelay;
            }
        }
        if (!world.IsDisposed && !world.IsStepping)
        {
            var (vertices, triangles) = Shape == ShieldShape.Wall ? WallMesh() : ShellMesh();
            mesh = world.AddStaticMesh(0, vertices, triangles, isLevel: false);
            (mesh.Owner, mesh.Passable) = (this, IsPassable);
        }
    }

    protected internal override void OnRemoved()
    {
        if (mesh is not null && !world.IsDisposed && !world.IsStepping)
        {
            world.RemoveStaticMesh(mesh);
        }
        mesh = null;
    }

    /// <summary>Overlap on the flat: shells' circles within both caps; walls' 12-long lines within 0.5; a wall's line across a shell.</summary>
    private bool Overlaps(SpellShield other)
    {
        if (Shape != ShieldShape.Wall && other.Shape != ShieldShape.Wall)
        {
            return CirclesCross(this, other);
        }
        if (Shape == ShieldShape.Wall && other.Shape == ShieldShape.Wall)
        {
            return Segment.DistanceSquared(Flat(Line()), Flat(other.Line()), out _, out _) <= 0.25f;
        }
        var (wall, shell) = Shape == ShieldShape.Wall ? (this, other) : (other, this);
        return shell.Crossing(wall.Line(), 0, out _) is not null;
    }

    private Segment Line() => new(Centre - Facing * WallHalfSize.Z, Facing * WallHalfSize.Z * 2);

    private static Segment Flat(Segment s) => new(s.Origin with { Y = 0 }, s.Delta with { Y = 0 });

    private static bool CirclesCross(SpellShield a, SpellShield b)
    {
        var (ca, cb) = (new Vector2(a.Centre.X, a.Centre.Z), new Vector2(b.Centre.X, b.Centre.Z));
        var (da, db) = (new Vector2(a.Facing.X, a.Facing.Z), new Vector2(b.Facing.X, b.Facing.Z));
        var (ra, rb) = (a.ShellRadius, b.ShellRadius);
        var distance = Vector2.Distance(ca, cb);
        if (distance + MathF.Abs(ra - rb) <= float.Epsilon)
        {
            return Angle2(da, db) < a.CapAngle + b.CapAngle;
        }
        if (!(distance < ra + rb))
        {
            return false;
        }
        var along = (ra * ra - rb * rb + distance * distance) / (2 * distance);
        var across = MathF.Sqrt(MathF.Max(0, ra * ra - along * along));
        var mid = ca + (cb - ca) * along / distance;
        var offset = new Vector2(cb.Y - ca.Y, -(cb.X - ca.X)) * across / distance;
        var (p, q) = (mid + offset, mid - offset);
        return (Angle2(db, p - cb) <= b.CapAngle || Angle2(db, q - cb) <= b.CapAngle) && (Angle2(da, p - ca) <= a.CapAngle || Angle2(da, q - ca) <= a.CapAngle);
    }

    private static float Angle2(Vector2 a, Vector2 b) =>
        a.LengthSquared() <= 1e-12f || b.LengthSquared() <= 1e-12f ? 0 : MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1, 1));

    /// <summary>The shell within its cap, both sides solid.</summary>
    private (List<Vector3>, List<MeshTriangle>) ShellMesh()
    {
        var (right, up, along) = Axes();
        var vertices = new List<Vector3> { Centre + along * ShellRadius };
        for (var stack = 1; stack <= Stacks; stack++)
        {
            var polar = CapAngle * stack / Stacks;
            for (var slice = 0; slice < Slices; slice++)
            {
                var azimuth = MathF.Tau * slice / Slices;
                var radial = right * MathF.Cos(azimuth) + up * MathF.Sin(azimuth);
                vertices.Add(Centre + (along * MathF.Cos(polar) + radial * MathF.Sin(polar)) * ShellRadius);
            }
        }
        var triangles = new List<MeshTriangle>();
        void Both(int x, int y, int z)
        {
            triangles.Add(new MeshTriangle(x, y, z));
            triangles.Add(new MeshTriangle(x, z, y));
        }
        for (var slice = 0; slice < Slices; slice++)
        {
            Both(0, 1 + slice, 1 + (slice + 1) % Slices);
        }
        for (var stack = 1; stack < Stacks; stack++)
        {
            var (row, next) = (1 + (stack - 1) * Slices, 1 + stack * Slices);
            for (var slice = 0; slice < Slices; slice++)
            {
                var (s0, s1) = (slice, (slice + 1) % Slices);
                Both(row + s0, next + s0, next + s1);
                Both(row + s0, next + s1, row + s1);
            }
        }
        return (vertices, triangles);
    }

    /// <summary>The wall's box, its faces outward.</summary>
    private (List<Vector3>, List<MeshTriangle>) WallMesh()
    {
        var (right, up, along) = Axes();
        var h = WallHalfSize;
        var vertices = new List<Vector3>();
        for (var i = 0; i < 8; i++)
        {
            vertices.Add(Centre + right * ((i & 1) == 0 ? -h.X : h.X) + up * ((i & 2) == 0 ? -h.Y : h.Y) + along * ((i & 4) == 0 ? -h.Z : h.Z));
        }
        int[][] faces = [[0, 4, 6, 2], [1, 3, 7, 5], [0, 1, 5, 4], [2, 6, 7, 3], [0, 2, 3, 1], [4, 5, 7, 6]];
        var triangles = new List<MeshTriangle>();
        foreach (var f in faces)
        {
            triangles.Add(new MeshTriangle(f[0], f[1], f[2]));
            triangles.Add(new MeshTriangle(f[0], f[2], f[3]));
            triangles.Add(new MeshTriangle(f[0], f[2], f[1]));                // both windings: the wall stops from either side
            triangles.Add(new MeshTriangle(f[0], f[3], f[2]));
        }
        return (vertices, triangles);
    }
}
