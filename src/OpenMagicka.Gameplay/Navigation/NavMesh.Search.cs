using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

public sealed partial class NavMesh
{
    /// <summary>A search node: the cost so far, that plus the heuristic, the triangle, and the key it came from.</summary>
    internal readonly record struct SearchNode(float Cost, float Total, NavRef At, uint Parent);

    /// <summary>
    /// A* over triangles (ENTITIES_AI.md §11.3), as the original: the triangles from the end (or the
    /// node with the least total, when the end is not reached) back to the start.
    /// </summary>
    private bool Search(Vector3 start, Vector3 end, NavMovement abilities, List<NavRef> path)
    {
        if (!float.IsFinite(start.X + start.Y + start.Z) || !float.IsFinite(end.X + end.Y + end.Z))
        {
            return false;                                                     // the original only guards the start, and keeps its last path
        }
        JoinParts();
        abilities |= NavMovement.Dynamic | NavMovement.Water;
        if (Nearest(start, abilities) is not var (from, first, _) || Nearest(end, abilities) is not var (to, goal, _))
        {
            return false;
        }
        var closed = new Dictionary<uint, SearchNode>();
        var costs = new Dictionary<uint, float>();
        var open = new OpenList();
        var origin = new SearchNode(0, Geometry.Manhattan(from, to), first, new NavRef(first.Mesh, NavTriangle.None).Key);
        open.Push(origin);
        var best = origin;
        var reached = false;
        while (!open.IsEmpty)
        {
            var current = open.Pop();
            if (!closed.TryAdd(current.At.Key, current))
            {
                continue;
            }
            if (current.At == goal)
            {
                (best, reached) = (current, true);
                break;
            }
            var surface = Surface(current.At.Mesh);
            var triangle = surface.Triangles[current.At.Triangle];
            for (var edge = 0; edge < 3; edge++)
            {
                var next = triangle.Neighbour(edge);
                var at = new NavRef(current.At.Mesh, next);
                if (next == NavTriangle.None || next >= surface.Triangles.Count || closed.ContainsKey(at.Key) || !Walkable(surface, next, abilities))
                {
                    continue;
                }
                var mid = (surface.Vertex(triangle.Corner(edge)) + surface.Vertex(triangle.Corner((edge + 1) % 3))) * 0.5f;
                if (current.At.Mesh != NavRef.Level)
                {
                    mid = surface.ToWorld(mid);
                }
                var step = (current.Parent & 0xFFFF) == NavTriangle.None ? Vector3.Distance(from, mid)
                    : (ushort)(current.Parent >> 16) != current.At.Mesh ? 0.5f
                    : triangle.CostFrom((ushort)current.Parent, edge) ?? Vector3.Distance(surface.ToWorld(surface.Centre(current.At.Triangle)), mid);
                if (at == goal)
                {
                    step += Vector3.Distance(mid, to);
                }
                Relax(open, costs, new SearchNode(step + current.Cost, step + current.Cost + Geometry.Manhattan(mid, to), at, current.At.Key), ref best);
            }
            if ((triangle.Movement & NavMovement.Dynamic) == 0 || !joints.TryGetValue(current.At.Key, out var across))
            {
                continue;
            }
            foreach (var joint in across)
            {
                if (closed.ContainsKey(joint.To.Key) || !Triangle(joint.To).Admits(abilities))
                {
                    continue;                                                 // as the original: no ice check across a joint
                }
                var step = (current.Parent & 0xFFFF) == NavTriangle.None ? Vector3.Distance(from, joint.EdgePos) : joint.Cost;
                if (joint.To == goal)
                {
                    step += Vector3.Distance(joint.EdgePos, to);
                }
                step += joint.Cost;                                           // as the original: the cost so far is not carried across a joint
                Relax(open, costs, new SearchNode(step, step + Geometry.Manhattan(joint.EdgePos, to), joint.To, current.At.Key), ref best);
            }
        }
        for (var key = best.At.Key; (ushort)key != NavTriangle.None && closed.TryGetValue(key, out var node); key = node.Parent)
        {
            path.Add(NavRef.Of(key));
        }
        return reached;
    }

    /// <summary>A Water triangle is walkable only over ice, looked for from 1 above its centre to 3 below (in its mesh's own space, as the original).</summary>
    private bool Walkable(NavSurface surface, ushort triangle, NavMovement abilities)
    {
        var t = surface.Triangles[triangle];
        if (!t.Admits(abilities))
        {
            return false;
        }
        if ((t.Movement & NavMovement.Water) == 0)
        {
            return true;
        }
        var centre = surface.Centre(triangle);
        return IceAlong?.Invoke(centre + Vector3.UnitY, centre - Vector3.UnitY * 3) ?? false;
    }

    private static void Relax(OpenList open, Dictionary<uint, float> costs, SearchNode node, ref SearchNode best)
    {
        if (costs.TryGetValue(node.At.Key, out var known) && known <= node.Cost)
        {
            return;
        }
        costs[node.At.Key] = node.Cost;
        open.Push(node);
        if (node.Total < best.Total)
        {
            best = node;
        }
    }
}

/// <summary>The open list: a binary min-heap on the total cost, ordered exactly as the original's (ties included).</summary>
internal sealed class OpenList
{
    private NavMesh.SearchNode[] heap = new NavMesh.SearchNode[513];
    private int last;

    public bool IsEmpty => last == 0;

    public void Push(NavMesh.SearchNode value)
    {
        if (++last >= heap.Length)
        {
            Array.Resize(ref heap, heap.Length * 2);
        }
        var i = last;
        while (i > 1 && value.Total < heap[i / 2].Total)
        {
            heap[i] = heap[i / 2];
            i /= 2;
        }
        heap[i] = value;
    }

    public NavMesh.SearchNode Pop()
    {
        var top = heap[1];
        var moved = heap[last--];
        var i = 1;
        while (true)
        {
            var (left, right, pick) = (i * 2, i * 2 + 1, 0);
            if (left <= last)
            {
                if (heap[left].Total < moved.Total)
                {
                    pick = left;
                }
                if (right <= last && heap[right].Total < moved.Total && heap[right].Total < heap[left].Total)
                {
                    pick = right;
                }
            }
            if (pick == 0)
            {
                break;
            }
            heap[i] = heap[pick];
            i = pick;
        }
        heap[i] = moved;
        return top;
    }
}
