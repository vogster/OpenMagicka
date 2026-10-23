using System.Numerics;
using Jitter2.Collision;
using Jitter2.LinearMath;
using J = Jitter2.Collision.Shapes;

namespace OpenMagicka.Physics;

/// <summary>
/// One segment cast. The engine walks its tree and tests every shape. Triangles are also tested
/// here, because the engine's ray test misses rays that run exactly through a shared edge, which
/// a vertical ground check over a grid-aligned level does all the time. Our test accepts
/// everything the engine's does, so the engine's nearest hit can still prune the walk.
/// </summary>
internal sealed class SegmentQuery
{
    /// <summary>Barycentric slack that closes the cracks between neighbouring triangles.</summary>
    internal const float EdgeTolerance = 1e-4f;

    private readonly DynamicTree.RayCastFilterPre pre;
    private Vector3 start;
    private Vector3 direction;
    private object? ignored;

    public SegmentQuery() => pre = Visit;

    public float TriangleLambda { get; private set; }

    public J.TriangleShape? Triangle { get; private set; }

    /// <summary>Walks the tree; our nearest triangle is kept here, the engine's nearest hit comes back.</summary>
    public bool Run(DynamicTree tree, Vector3 from, Vector3 to, object? ignoreBody, out IDynamicTreeProxy? proxy, out JVector normal, out float lambda)
    {
        (start, direction, ignored) = (from, to - from, ignoreBody);
        (Triangle, TriangleLambda) = (null, float.PositiveInfinity);
        try
        {
            return tree.RayCast(from.ToJitter(), direction.ToJitter(), 1, pre, null, out proxy, out normal, out lambda);
        }
        finally
        {
            ignored = null;
        }
    }

    private bool Visit(IDynamicTreeProxy proxy)
    {
        if (proxy is not J.RigidBodyShape shape || (ignored is not null && ReferenceEquals(shape.RigidBody, ignored)))
        {
            return false;
        }
        if (shape is J.TriangleShape triangle)
        {
            triangle.GetWorldVertices(out var a, out var b, out var c);
            if (SegmentTriangle(start, direction, a.ToNumerics(), b.ToNumerics(), c.ToNumerics(), out var lambda) && lambda < TriangleLambda)
            {
                (Triangle, TriangleLambda) = (triangle, lambda);
            }
        }
        return true;
    }

    /// <summary>
    /// Möller–Trumbore against the front face only (the side the right-hand normal
    /// (b − a) × (c − a) points to), with edges widened by <see cref="EdgeTolerance"/>. Like the
    /// engine's test, a hit at the very end of the segment (lambda = 1) does not count.
    /// </summary>
    internal static bool SegmentTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float lambda)
    {
        lambda = 0;
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(direction, ac);
        var det = Vector3.Dot(ab, p);                  // −direction · normal: positive from the front
        if (!(det > 0) || !float.IsFinite(det))
        {
            return false;
        }
        var s = origin - a;
        var u = Vector3.Dot(s, p) / det;
        if (u < -EdgeTolerance || u > 1 + EdgeTolerance)
        {
            return false;
        }
        var q = Vector3.Cross(s, ab);
        var v = Vector3.Dot(direction, q) / det;
        if (v < -EdgeTolerance || u + v > 1 + EdgeTolerance)
        {
            return false;
        }
        lambda = Vector3.Dot(ac, q) / det;
        return lambda >= 0 && lambda < 1;
    }
}
