using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Navigation;
using Microsoft.Xna.Framework;
using Numerics = System.Numerics;
using NavTriangle = OpenMagicka.Gameplay.Navigation.NavTriangle;

namespace OpenMagicka.Engine.Navigation;

/// <summary>
/// A level's navigation from its scene file (ENTITIES_AI.md §11.1): the level's nav mesh, and each
/// animated part's (children too) placed at the part's rest pose, as <see cref="LevelParts"/> places
/// the parts; a part whose rest pose is not finite is left out with its children.
/// </summary>
public static class LevelNavigation
{
    public static NavMesh From(LevelModelContent level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var mesh = new NavMesh(Surface(level.NavMesh));
        foreach (var part in level.AnimatedParts)
        {
            AddPart(mesh, part, Matrix.Identity);
        }
        return mesh;
    }

    public static NavSurface Surface(NavMeshContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new NavSurface(
            [.. content.Vertices.Select(v => new Numerics.Vector3(v.X, v.Y, v.Z))],
            [.. content.Triangles.Select(t => new NavTriangle(
                t.A, t.B, t.C,
                t.NeighbourA ?? NavTriangle.None, t.NeighbourB ?? NavTriangle.None, t.NeighbourC ?? NavTriangle.None,
                t.CostAB, t.CostBC, t.CostCA,
                (NavMovement)(byte)t.Movement))]);
    }

    /// <summary>FNA's matrix as System.Numerics' (both keep rows as vectors).</summary>
    public static Numerics.Matrix4x4 ToNumerics(Matrix m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    private static void AddPart(NavMesh mesh, AnimatedLevelPartContent part, Matrix parent)
    {
        var world = (part.Animation.Count > 0 ? LevelParts.PoseMatrix(part.Animation[0].Pose) : Matrix.Identity) * parent;
        var placed = ToNumerics(world);
        if (!IsFinite(placed))
        {
            return;
        }
        if (part.NavMesh is { } content)
        {
            var surface = Surface(content);
            if (!surface.Place(placed))
            {
                return;                                                       // a pose that cannot be undone: left out with its children
            }
            mesh.AddPart(surface);
        }
        foreach (var child in part.Children)
        {
            AddPart(mesh, child, world);
        }
    }

    private static bool IsFinite(Numerics.Matrix4x4 m) =>
        float.IsFinite(m.M11 + m.M12 + m.M13 + m.M14 + m.M21 + m.M22 + m.M23 + m.M24
            + m.M31 + m.M32 + m.M33 + m.M34 + m.M41 + m.M42 + m.M43 + m.M44);
}
