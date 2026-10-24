using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

public sealed partial class NavMesh
{
    private const float Same = 0.001f;

    /// <summary>
    /// Pulls the triangle path straight (ENTITIES_AI.md §11.4): a funnel over the edges between
    /// triangles, or a triangle's centre where the path goes from one mesh to another. The path runs
    /// from the start's nearest point on its triangle to the end's on its.
    /// </summary>
    /// <remarks>
    /// The original then means to add a corner wherever the path crosses into a triangle that asks
    /// for something else; but it compares each triangle with itself, so it never does. Nor do we.
    /// </remarks>
    private void Straighten(Vector3 start, Vector3 end, List<NavRef> triangles, List<PathNode> path)
    {
        if (triangles.Count == 0)
        {
            return;
        }
        var last = triangles.Count - 1;
        var from = OnTriangle(triangles[last], start);
        var to = OnTriangle(triangles[0], end);
        path.Add(new PathNode(from, Triangle(triangles[last]).Movement));
        var (apex, left, right) = (from, from, from);
        var (apexAt, leftAt, rightAt) = (0, 0, 0);
        var guard = triangles.Count * triangles.Count + 16;
        for (var i = last; i >= 0 && guard-- > 0; i--)
        {
            var (portalLeft, portalRight) = i > 0 ? Portal(triangles[i], triangles[i - 1]) : (to, to);
            if (Vector3.DistanceSquared(apex, right) < Same)
            {
                (right, rightAt) = (portalRight, i);
            }
            else if (Geometry.Area2D(apex, right, portalRight) <= 0)
            {
                if (!(Geometry.Area2D(apex, left, portalRight) > 0))
                {
                    (apex, apexAt) = (left, leftAt);                          // the right side crossed the left: the left corner turns
                    Corner(path, apex, triangles[apexAt]);
                    (left, right, leftAt, rightAt) = (apex, apex, apexAt, apexAt);
                    i = apexAt;
                    continue;
                }
                (right, rightAt) = (portalRight, i);
            }
            if (Vector3.DistanceSquared(apex, left) < Same)
            {
                (left, leftAt) = (portalLeft, i);
                continue;
            }
            if (!(Geometry.Area2D(apex, left, portalLeft) >= 0))
            {
                continue;
            }
            if (Geometry.Area2D(apex, right, portalLeft) < 0)
            {
                (left, leftAt) = (portalLeft, i);
                continue;
            }
            (apex, apexAt) = (right, rightAt);                                // the left side crossed the right: the right corner turns
            Corner(path, apex, triangles[apexAt]);
            (left, right, leftAt, rightAt) = (apex, apex, apexAt, apexAt);
            i = apexAt;
        }
        path.Add(new PathNode(to, Triangle(triangles[0]).Movement));
    }

    private void Corner(List<PathNode> path, Vector3 at, NavRef triangle)
    {
        if (Vector3.DistanceSquared(path[^1].Position, at) >= Same)
        {
            path.Add(new PathNode(at, Triangle(triangle).Movement));
        }
    }

    /// <summary>The nearest point of a triangle to a world point, in the world.</summary>
    private Vector3 OnTriangle(NavRef at, Vector3 world)
    {
        var surface = Surface(at.Mesh);
        return surface.ToWorld(surface.Nearest(at.Triangle, surface.ToLocal(world)));
    }

    /// <summary>
    /// The edge from one triangle to the next, as (left, right) seen walking it; the triangle's centre
    /// for both when the next is in another mesh.
    /// </summary>
    private (Vector3 Left, Vector3 Right) Portal(NavRef at, NavRef next)
    {
        var surface = Surface(at.Mesh);
        var t = surface.Triangles[at.Triangle];
        if (at.Mesh != next.Mesh)
        {
            var centre = surface.ToWorld((surface.Vertex(t.A) + surface.Vertex(t.B) + surface.Vertex(t.C)) * (1f / 3));
            return (centre, centre);
        }
        var (leftCorner, rightCorner) = next.Triangle == t.NeighbourA ? (t.B, t.A)
            : next.Triangle == t.NeighbourB ? (t.C, t.B)
            : next.Triangle == t.NeighbourC ? (t.A, t.C)
            : (t.A, t.A);                                                     // not a neighbour (the original throws)
        return (surface.ToWorld(surface.Vertex(leftCorner)), surface.ToWorld(surface.Vertex(rightCorner)));
    }
}
