using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

/// <summary>A tree of boxes over a mesh's triangles, built once by halving along the longest side.</summary>
internal sealed class TriangleTree
{
    private const int Leaf = 8;

    /// <summary>Room on the walk's stack: the tree is balanced, so its depth is about log2(65535 / 8) and two pushes a level.</summary>
    private const int Depth = 64;

    private readonly record struct Node(Vector3 Min, Vector3 Max, int Left, int Right, int First, int Count);

    private readonly Vector3[] vertices;
    private readonly NavTriangle[] triangles;
    private readonly int[] order;
    private readonly Vector3[] mins, maxs;
    private readonly List<Node> nodes = [];

    public TriangleTree(Vector3[] vertices, NavTriangle[] triangles)
    {
        (this.vertices, this.triangles) = (vertices, triangles);
        order = [.. Enumerable.Range(0, triangles.Length)];
        (mins, maxs) = (new Vector3[triangles.Length], new Vector3[triangles.Length]);
        for (var i = 0; i < triangles.Length; i++)
        {
            var (a, b, c) = Corners(i);
            (mins[i], maxs[i]) = (Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
        }
        if (triangles.Length > 0)
        {
            Build(0, triangles.Length);
        }
    }

    public float Nearest(Vector3 point, NavMovement abilities, float within, out Vector3 nearest, out int triangle)
    {
        (nearest, triangle) = (default, -1);
        var best = within;
        if (nodes.Count == 0)
        {
            return float.PositiveInfinity;
        }
        Span<int> stack = stackalloc int[Depth];
        Span<float> reach = stackalloc float[Depth];
        var top = 0;
        (stack[top], reach[top++]) = (0, BoxDistanceSquared(point, nodes[0].Min, nodes[0].Max));
        while (top > 0)
        {
            var index = stack[--top];
            if (reach[top] > best)
            {
                continue;                                                     // only farther ones within: an equal one may hold a lower index
            }
            var node = nodes[index];
            if (node.Count > 0)
            {
                for (var k = node.First; k < node.First + node.Count; k++)
                {
                    var i = order[k];
                    if (!triangles[i].Admits(abilities))
                    {
                        continue;
                    }
                    var (a, b, c) = Corners(i);
                    var on = Geometry.ClosestOnTriangle(point, a, b, c);
                    var d = Vector3.DistanceSquared(point, on);
                    if (d < best || (d == best && triangle >= 0 && i < triangle))
                    {
                        (best, nearest, triangle) = (d, on, i);              // ties go to the lower index, for a stable answer
                    }
                }
                continue;
            }
            var left = BoxDistanceSquared(point, nodes[node.Left].Min, nodes[node.Left].Max);
            var right = BoxDistanceSquared(point, nodes[node.Right].Min, nodes[node.Right].Max);
            if (left <= right)
            {
                (stack[top], reach[top++]) = (node.Right, right);
                (stack[top], reach[top++]) = (node.Left, left);
            }
            else
            {
                (stack[top], reach[top++]) = (node.Left, left);
                (stack[top], reach[top++]) = (node.Right, right);
            }
        }
        return best;
    }

    /// <summary>Adds the Dynamic triangles whose boxes meet the box to the list.</summary>
    public void DynamicIn(Vector3 min, Vector3 max, List<int> into)
    {
        if (nodes.Count == 0)
        {
            return;
        }
        Span<int> stack = stackalloc int[Depth];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = nodes[stack[--top]];
            if (!Overlaps(min, max, node.Min, node.Max))
            {
                continue;
            }
            if (node.Count > 0)
            {
                for (var k = node.First; k < node.First + node.Count; k++)
                {
                    var i = order[k];
                    if ((triangles[i].Movement & NavMovement.Dynamic) != 0 && Overlaps(min, max, mins[i], maxs[i]))
                    {
                        into.Add(i);
                    }
                }
                continue;
            }
            stack[top++] = node.Right;
            stack[top++] = node.Left;
        }
    }

    private int Build(int first, int count)
    {
        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        for (var k = first; k < first + count; k++)
        {
            (min, max) = (Vector3.Min(min, mins[order[k]]), Vector3.Max(max, maxs[order[k]]));
        }
        var index = nodes.Count;
        nodes.Add(new Node(min, max, -1, -1, first, count));
        if (count <= Leaf)
        {
            return index;
        }
        var size = max - min;
        var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        Array.Sort(order, first, count, Comparer<int>.Create((p, q) =>
            Axis(mins[p] + maxs[p], axis).CompareTo(Axis(mins[q] + maxs[q], axis)) is var c && c != 0 ? c : p.CompareTo(q)));
        var half = count / 2;
        var left = Build(first, half);
        var right = Build(first + half, count - half);
        nodes[index] = new Node(min, max, left, right, first, 0);
        return index;
    }

    private (Vector3, Vector3, Vector3) Corners(int i) => (vertices[triangles[i].A], vertices[triangles[i].B], vertices[triangles[i].C]);

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static bool Overlaps(Vector3 min, Vector3 max, Vector3 otherMin, Vector3 otherMax) =>
        min.X <= otherMax.X && max.X >= otherMin.X && min.Y <= otherMax.Y && max.Y >= otherMin.Y && min.Z <= otherMax.Z && max.Z >= otherMin.Z;

    private static float BoxDistanceSquared(Vector3 p, Vector3 min, Vector3 max) => Vector3.DistanceSquared(p, Vector3.Clamp(p, min, max));
}
