using System.Numerics;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The mouse onto the Tome's right page (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: a ray from the
/// camera through the point is met with a fixed quad where the page lies (its outer edge a little higher than the
/// spine), and the hit is turned into page pixels by the original's map (allowing for the sheet curling down at the
/// spine). The left page is met first by nothing a page draws, so a ray only counts on the right.
/// </summary>
public static class BookPicking
{
    /// <summary>The right page's quad: its outer edge at x −5.3 (y 0.5), its spine at x 0 (y 0.4), its top at z 2.75.</summary>
    public static readonly Vector3 OuterTop = new(-5.3f, 0.5f, 2.75f), SpineTop = new(0, 0.4f, 2.75f),
        OuterBottom = new(-5.3f, 0.5f, -2.75f), SpineBottom = new(0, 0.4f, -2.75f);

    /// <summary>A screen point (pixels from the top left) on the right page, in page pixels; none off it.</summary>
    public static Vector2? ToPage(Matrix4x4 viewProjection, Vector3 camera, Vector2 point, Vector2 screen)
    {
        if (!(screen.X > 0) || !(screen.Y > 0) || !Matrix4x4.Invert(viewProjection, out var unproject))
        {
            return null;
        }
        var far = Vector4.Transform(new Vector4(point.X / screen.X * 2 - 1, 1 - point.Y / screen.Y * 2, 1, 1), unproject);
        if (far.W == 0)
        {
            return null;
        }
        var direction = new Vector3(far.X, far.Y, far.Z) / far.W - camera;
        var hit = Meet(camera, direction, OuterTop, SpineTop, OuterBottom) ?? Meet(camera, direction, SpineTop, SpineBottom, OuterBottom);
        return hit is { } at ? FromWorld(at) : null;
    }

    /// <summary>A point on the right page (world x, z) in page pixels, by the original's map.</summary>
    public static Vector2 FromWorld(Vector3 at) => new((0.03f - at.X / 5.3f) * 0.97f * 1024, (2.75f - at.Z) / 5.5f * 1024);

    /// <summary>Where a ray meets a triangle from either side (Möller–Trumbore); none when it misses or lies behind.</summary>
    private static Vector3? Meet(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var (ab, ac) = (b - a, c - a);
        var p = Vector3.Cross(direction, ac);
        var det = Vector3.Dot(ab, p);
        if (MathF.Abs(det) < 1e-9f)
        {
            return null;
        }
        var t = origin - a;
        var u = Vector3.Dot(t, p) / det;
        var q = Vector3.Cross(t, ab);
        var v = Vector3.Dot(direction, q) / det;
        var along = Vector3.Dot(ac, q) / det;
        return u >= 0 && v >= 0 && u + v <= 1 && along >= 0 ? origin + direction * along : null;
    }
}
