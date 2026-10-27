using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Liquids;

/// <summary>Water (freezing into ice) or lava (cooling into rock).</summary>
public enum LiquidKind : byte { Water, Lava }

/// <summary>
/// A level's (or a moving part's) water or lava (OM-88, docs/engine/ARCHITECTURE.md §7.5), as the original's: its
/// sheet of triangles with one freeze value per vertex, from 0 (liquid) up; a triangle with a corner at 0.5 or more is
/// ice (or rock) and solid, one with all three below is liquid. Cold freezes it and fire melts it (lava: water cools it
/// too) in an arc on the ground plane (<see cref="Freeze"/>); each step it drifts back: down at 0.1 a second (kept in
/// 0…2), or, for a liquid frozen by itself, up (kept in −1…1). One that is not freezable only drifts. It tells the
/// physics which of its triangles are solid (<see cref="IMeshSurface"/>). A simulation object: it changes in place.
/// </summary>
public sealed class Liquid : IMeshSurface
{
    /// <summary>The level's collision materials of water and lava (what a body in it reports).</summary>
    public const int WaterMaterial = 8, LavaMaterial = 9;

    /// <summary>A corner at this or more is frozen.</summary>
    public const float Frozen = 0.5f;

    /// <summary>How fast it drifts back (a second).</summary>
    public const float Drift = 0.1f;

    /// <summary>The most corners one freeze reaches.</summary>
    public const int MaxSelected = 2048;

    private readonly Vector3[] vertices;
    private readonly int[] triangles;
    private readonly float[] freeze;
    private readonly List<int> selected = [];
    private readonly HashSet<int> seen = [];
    private Matrix4x4 world = Matrix4x4.Identity, toLocal = Matrix4x4.Identity;

    /// <param name="vertices">Its corners in its own space.</param>
    /// <param name="triangles">Three corners a triangle, facing up (the original's index order with its second and third swapped).</param>
    public Liquid(LiquidKind kind, Vector3[] vertices, int[] triangles, bool hasCollision, bool freezable, bool autoFreeze)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Length % 3 != 0 || triangles.Any(i => (uint)i >= (uint)vertices.Length))
        {
            throw new ArgumentException("a liquid's triangles must name its corners, three each", nameof(triangles));
        }
        (Kind, this.vertices, this.triangles, HasCollision, Freezable, AutoFreeze) = (kind, vertices, triangles, hasCollision, freezable, autoFreeze);
        freeze = new float[vertices.Length];
        Array.Fill(freeze, autoFreeze ? 1 : 0);
        (low, high) = vertices.Length == 0 ? (Vector3.Zero, Vector3.Zero) : (vertices.Aggregate(Vector3.Min), vertices.Aggregate(Vector3.Max));
    }

    private readonly Vector3 low, high;

    public LiquidKind Kind { get; }

    /// <summary>Which of the level's drawn liquids it is (the render's key for its freeze values); −1: none.</summary>
    public int Key { get; set; } = -1;

    /// <summary>Bodies meet it (else it is only drawn).</summary>
    public bool HasCollision { get; }

    /// <summary>Spells freeze and melt it (else it only drifts).</summary>
    public bool Freezable { get; }

    /// <summary>It starts frozen and freezes again by itself.</summary>
    public bool AutoFreeze { get; }

    public IReadOnlyList<Vector3> Vertices => vertices;

    /// <summary>The triangles' corners, three each.</summary>
    public IReadOnlyList<int> Triangles => triangles;

    public int TriangleCount => triangles.Length / 3;

    /// <summary>Each corner's freeze value (what the render's second stream gets).</summary>
    public ReadOnlySpan<float> FreezeValues => freeze;

    /// <summary>Counts each change of the freeze values (the render uploads them again only then).</summary>
    public int Version { get; private set; }

    /// <summary>Where it is: identity for a level's, a moving part's place for the part's.</summary>
    public Matrix4x4 World
    {
        get => world;
        set
        {
            world = value;
            toLocal = Matrix4x4.Invert(value, out var inverse) ? inverse : Matrix4x4.Identity;
        }
    }

    public int LiquidMaterial => Kind == LiquidKind.Water ? WaterMaterial : LavaMaterial;

    /// <summary>Ice slips; cooled lava does not.</summary>
    public bool Slippery => Kind == LiquidKind.Water;

    public bool IsSolid(int a, int b, int c) => freeze[a] >= Frozen || freeze[b] >= Frozen || freeze[c] >= Frozen;

    /// <summary>A triangle is ice (or rock): a corner frozen.</summary>
    public bool IsIce(int triangle)
    {
        var at = triangle * 3;
        return freeze[triangles[at]] >= Frozen || freeze[triangles[at + 1]] >= Frozen || freeze[triangles[at + 2]] >= Frozen;
    }

    /// <summary>A step of its drift back: down to liquid (0…2), or up to frozen for one frozen by itself (−1…1).</summary>
    public void Step(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            return;
        }
        var (shift, least, most) = AutoFreeze ? (Drift * dt, -1f, 1f) : (-Drift * dt, 0f, 2f);
        var changed = false;
        for (var i = 0; i < freeze.Length; i++)
        {
            var value = Math.Clamp(freeze[i] + shift, least, most);
            changed |= value != freeze[i];
            freeze[i] = value;
        }
        Version += changed ? 1 : 0;
    }

    /// <summary>Back as it started: all liquid, or all frozen for one frozen by itself (a checkpoint's restart).</summary>
    public void Reset()
    {
        Array.Fill(freeze, AutoFreeze ? 1 : 0);
        Version++;
    }

    /// <summary>Every corner by an amount (a blizzard's tick, a reset); none for a liquid that does not freeze.</summary>
    public void FreezeAll(float amount)
    {
        if (!Freezable || amount == 0 || !float.IsFinite(amount))
        {
            return;
        }
        for (var i = 0; i < freeze.Length; i++)
        {
            freeze[i] += amount;
        }
        Version++;
    }

    /// <summary>
    /// How much a freeze of the damages changes a corner, as the original's: the magnitudes of the entries with cold, less
    /// those with fire (lava: plus those with water), N; then 3 · sign(N) · (1 − 0.75^(3|N|)) times the multiplier. An entry
    /// counts for each of its elements.
    /// </summary>
    public static float Amount(LiquidKind kind, IEnumerable<DamageEntry> damages, float multiplier)
    {
        ArgumentNullException.ThrowIfNull(damages);
        var (cold, water, fire) = (0f, 0f, 0f);
        foreach (var entry in damages)
        {
            cold += entry.Element.HasFlag(Elements.Cold) ? entry.Magnitude : 0;
            water += entry.Element.HasFlag(Elements.Water) ? entry.Magnitude : 0;
            fire += entry.Element.HasFlag(Elements.Fire) ? entry.Magnitude : 0;
        }
        var net = cold - fire + (kind == LiquidKind.Lava ? water : 0);
        return 3 * MathF.Sign(net) * (1 - MathF.Pow(0.75f, 3 * MathF.Abs(net))) * multiplier;
    }

    /// <summary>
    /// Freezes (a positive amount) or melts the corners in an arc on the ground plane, as the original's: heights left
    /// out, the range the direction's length across, the spread either side of it (2π: all round). The arc is cut into 2n + 1 slices of
    /// equal width (n = spread / (π/4), whole), so between a quarter and a whole turn it covers less than asked (π/2:
    /// ±54°); each slice reaches no further than <paramref name="reach"/> says (a spell shield in the way). Water takes
    /// the arc into its own space and counts a corner once (but the first of each later slice unchecked); lava does
    /// neither (both the original's). At most 2048 corners.
    /// </summary>
    /// <param name="reach">A slice's reach from the origin along a direction of the range's length (world); none: the range.</param>
    public void Freeze(Vector3 origin, Vector3 direction, float spread, float amount, Func<Vector3, Vector3, float>? reach = null)
    {
        if (!Freezable || amount == 0 || !float.IsFinite(amount))
        {
            return;
        }
        var range = new Vector2(direction.X, direction.Z).Length();
        if (!(range > 0) || !(spread > 0))
        {
            return;
        }
        var slices = (int)MathF.Floor(spread / (MathF.PI / 4));
        var half = spread / (2 * slices + 1);
        var (cosHalf, yaw) = (MathF.Cos(half), MathF.Atan2(direction.X, direction.Z));
        var water = Kind == LiquidKind.Water;
        var at = water ? Vector3.Transform(origin, toLocal) : origin;
        selected.Clear();
        seen.Clear();
        for (var k = -slices; k <= slices && selected.Count < MaxSelected; k++)
        {
            var angle = yaw + k * half;
            var way = new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle));
            var length = reach is null ? range : Math.Clamp(reach(origin, way * range), 0, range);
            var local = water ? Vector3.TransformNormal(way, toLocal) : way;
            if (new Vector2(local.X, local.Z).LengthSquared() < 1e-12f)
            {
                continue;                                                     // a sheet stood on its edge: no ground plane to sweep
            }
            var flat = Vector2.Normalize(new Vector2(local.X, local.Z));
            var first = selected.Count;
            for (var v = 0; v < vertices.Length && selected.Count < MaxSelected; v++)
            {
                var offset = new Vector2(vertices[v].X - at.X, vertices[v].Z - at.Z);
                var distance = offset.Length();
                if (distance > length || (distance > 0 && Vector2.Dot(offset / distance, flat) < cosHalf))
                {
                    continue;
                }
                if (water && k > -slices && selected.Count > first && seen.Contains(v))
                {
                    continue;                                                 // counted already (the slice's first is not checked)
                }
                selected.Add(v);
                seen.Add(v);
            }
        }
        foreach (var v in selected)
        {
            freeze[v] += amount;
        }
        Version += selected.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Where a segment (world) first meets the sheet, either side, skipping liquid or frozen triangles as asked; the point
    /// and the triangle's normal in the world (ours: the original gave them in the liquid's own space). None without
    /// collision, or with both skipped.
    /// </summary>
    public bool Meets(Vector3 from, Vector3 to, bool skipLiquid, bool skipFrozen, out Vector3 point, out Vector3 normal)
    {
        (point, normal) = (default, default);
        if (!HasCollision || (skipLiquid && skipFrozen))
        {
            return false;
        }
        var (a0, b0) = (Vector3.Transform(from, toLocal), Vector3.Transform(to, toLocal));
        if (Vector3.Max(a0, b0) is var top && Vector3.Min(a0, b0) is var bottom
            && (top.X < low.X || top.Y < low.Y || top.Z < low.Z || bottom.X > high.X || bottom.Y > high.Y || bottom.Z > high.Z))
        {
            return false;                                                     // nowhere near it
        }
        var direction = b0 - a0;
        var nearest = float.MaxValue;
        for (var t = 0; t < TriangleCount; t++)
        {
            var ice = IsIce(t);
            if ((ice && skipFrozen) || (!ice && skipLiquid))
            {
                continue;
            }
            var (p, q, r) = (vertices[triangles[t * 3]], vertices[triangles[t * 3 + 1]], vertices[triangles[t * 3 + 2]]);
            if (Hit(a0, direction, p, q, r) is { } along && along < nearest)
            {
                nearest = along;
                normal = Vector3.Normalize(Vector3.Cross(q - p, r - p));
            }
        }
        if (nearest == float.MaxValue)
        {
            return false;
        }
        point = Vector3.Transform(a0 + direction * nearest, world);
        normal = Vector3.Normalize(Vector3.TransformNormal(normal, world));
        return true;
    }

    /// <summary>Where along a segment it meets a triangle (0…1), from either side; none.</summary>
    private static float? Hit(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var (ab, ac) = (b - a, c - a);
        var p = Vector3.Cross(direction, ac);
        var det = Vector3.Dot(ab, p);
        if (MathF.Abs(det) < 1e-12f)
        {
            return null;
        }
        var s = origin - a;
        var u = Vector3.Dot(s, p) / det;
        var q = Vector3.Cross(s, ab);
        var v = Vector3.Dot(direction, q) / det;
        var t = Vector3.Dot(ac, q) / det;
        return u >= -1e-5f && v >= -1e-5f && u + v <= 1 + 1e-5f && t >= 0 && t <= 1 ? t : null;
    }
}
