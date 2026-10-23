using OpenMagicka.Content.Magicka;
using OpenMagicka.Physics;
using Numerics = System.Numerics;

namespace OpenMagicka.Engine.Physics;

/// <summary>
/// A level's static collision in the physics world: one mesh per material, the mesh index being
/// the <see cref="CollisionMaterial"/> (docs/engine/ARCHITECTURE.md §7.3).
/// </summary>
public static class LevelCollision
{
    /// <summary>
    /// Adds the level's material meshes and returns the number of triangles kept. The triangles
    /// keep the order the reader gives: their right-hand normals face out of the level (floors up).
    /// Water and Lava meshes are left out until their freeze filtering exists, so nothing stands on
    /// them; the game's levels have none (liquids carry their own collision).
    /// </summary>
    public static int AddTo(PhysicsWorld world, LevelModelContent level)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(level);
        var kept = 0;
        for (var material = 0; material < level.CollisionMeshes.Count; material++)
        {
            if (level.CollisionMeshes[material] is { } mesh && !IsLiquid((CollisionMaterial)material))
            {
                kept += world.AddStaticMesh(material, Vertices(mesh), Triangles(mesh)).TriangleCount;
            }
        }
        return kept;
    }

    /// <summary>The material of a segment hit on the level, or null when a body was hit.</summary>
    public static CollisionMaterial? MaterialOf(SegmentHit hit) =>
        hit.Material is >= 0 and <= byte.MaxValue and var m && Enum.IsDefined((CollisionMaterial)m) ? (CollisionMaterial)m : null;

    private static bool IsLiquid(CollisionMaterial material) => material is CollisionMaterial.Water or CollisionMaterial.Lava;

    private static Numerics.Vector3[] Vertices(CollisionMeshContent mesh) =>
        [.. mesh.Vertices.Select(v => new Numerics.Vector3(v.X, v.Y, v.Z))];

    private static MeshTriangle[] Triangles(CollisionMeshContent mesh) =>
        [.. mesh.Triangles.Select(t => new MeshTriangle(t.I0, t.I1, t.I2))];
}
