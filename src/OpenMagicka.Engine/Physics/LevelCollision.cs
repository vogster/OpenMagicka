using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Liquids;
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
    public static int AddTo(PhysicsWorld world, LevelModelContent level) => Load(world, level).Sum(m => m.TriangleCount);

    /// <summary>Adds the level's material meshes (as <see cref="AddTo"/>) and returns them, to take out when its scene is left.</summary>
    public static IReadOnlyList<StaticMesh> Load(PhysicsWorld world, LevelModelContent level)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(level);
        var meshes = new List<StaticMesh>();
        for (var material = 0; material < level.CollisionMeshes.Count; material++)
        {
            if (level.CollisionMeshes[material] is { } mesh && !IsLiquid((CollisionMaterial)material))
            {
                meshes.Add(world.AddStaticMesh(material, Vertices(mesh), Triangles(mesh)));
            }
        }
        return meshes;
    }

    /// <summary>The friction of a liquid's ice and rock, as the original's.</summary>
    public const float LiquidFriction = 10;

    /// <summary>
    /// A liquid's sheet in the physics world (OM-88), when it has collision: its frozen triangles solid for all (ice reports
    /// the Generic material), its liquid ones felt only by characters (<see cref="Liquid"/> is the mesh's surface). Ours: its
    /// ice counts as the level for level-only segment tests (the original asked the liquids apart).
    /// </summary>
    public static StaticMesh? AddLiquid(PhysicsWorld world, Liquid liquid)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(liquid);
        if (!liquid.HasCollision)
        {
            return null;
        }
        var triangles = new MeshTriangle[liquid.TriangleCount];
        for (var t = 0; t < triangles.Length; t++)
        {
            triangles[t] = new MeshTriangle(liquid.Triangles[t * 3], liquid.Triangles[t * 3 + 1], liquid.Triangles[t * 3 + 2]);
        }
        var mesh = world.AddStaticMesh((int)CollisionMaterial.Generic, liquid.Vertices, triangles, LiquidFriction, surface: liquid);
        mesh.Owner = liquid;
        return mesh;
    }

    /// <summary>
    /// Adds the scene's moving parts' collision meshes (each in its part's own space, placed where the part is)
    /// and gives each part its <see cref="PartCollision"/>; returns the meshes, to take out with the level's.
    /// </summary>
    public static IReadOnlyList<StaticMesh> LoadParts(PhysicsWorld world, Scripting.ScriptPlaces places)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(places);
        var meshes = new List<StaticMesh>();
        foreach (var part in places.AllParts)
        {
            StaticMesh? mesh = null;
            if (part.Content.Collision is { } collision && !IsLiquid(collision.Material) && part.IsPlaced)
            {
                mesh = world.AddStaticMesh((int)collision.Material, Vertices(collision.Mesh), Triangles(collision.Mesh));
                meshes.Add(mesh);
            }
            var liquids = part.Liquids.Select(l => (l, AddLiquid(world, l))).ToList();
            meshes.AddRange(liquids.Select(l => l.Item2).OfType<StaticMesh>());
            part.Collision = new PartCollision(world, mesh, part.World, liquids);   // one without a mesh still carries what is attached
            foreach (var body in part.Attached.Where(b => !b.IsRemoved))
            {
                part.Collision.Attach(body);                                  // what rode on it before the scene was left
            }
            part.Attached = [];
        }
        return meshes;
    }

    /// <summary>The scene's parts lose their collision (the scene is left; the meshes go with the level's).</summary>
    public static void UnloadParts(Scripting.ScriptPlaces places)
    {
        ArgumentNullException.ThrowIfNull(places);
        foreach (var part in places.AllParts)
        {
            part.Attached = part.Collision?.Attached ?? [];
            part.Collision = null;
        }
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
