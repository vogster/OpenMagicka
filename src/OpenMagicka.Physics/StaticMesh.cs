namespace OpenMagicka.Physics;

/// <summary>
/// What a mesh's triangles are, one by one (OM-88: a liquid's, whose frozen triangles are solid and the rest liquid),
/// by their corners' indices as the mesh was given them.
/// </summary>
public interface IMeshSurface
{
    /// <summary>The triangle is solid: every body meets it. Else it is liquid: felt by bodies that feel liquids, passed by the rest.</summary>
    bool IsSolid(int a, int b, int c);

    /// <summary>The material a liquid contact reports (water or lava).</summary>
    int LiquidMaterial { get; }

    /// <summary>Its solid triangles are slippery (ice, not cooled rock).</summary>
    bool Slippery { get; }
}

/// <summary>
/// A triangle mesh of a <see cref="PhysicsWorld"/> that nothing pushes: one material of a level, a level's
/// moving part (placed anew each frame, <see cref="PhysicsWorld.PlaceStaticMesh"/>), or a solid thing that
/// stands in the world without being the level (a spell's shield).
/// </summary>
public sealed class StaticMesh
{
    internal StaticMesh(int material, int triangleCount, float friction, bool isLevel)
    {
        Material = material;
        TriangleCount = triangleCount;
        Friction = friction;
        IsLevel = isLevel;
    }

    /// <summary>Part of the level: level-only segment tests meet it.</summary>
    public bool IsLevel { get; }

    /// <summary>What the mesh stands for (a shield entity), for whoever meets it.</summary>
    public object? Owner { get; set; }

    /// <summary>Bodies that <see cref="PhysicsBody.PassesPassableMeshes"/> (characters) pass through it while set (a shield going down).</summary>
    public bool Passable { get; set; }

    public bool IsRemoved { get; internal set; }

    internal Jitter2.Dynamics.RigidBody? Body { get; set; }

    /// <summary>Reported by the hits on this mesh.</summary>
    public int Material { get; }

    /// <summary>The triangles kept: degenerate ones are dropped.</summary>
    public int TriangleCount { get; }

    public float Friction { get; }

    /// <summary>What its triangles are one by one (a liquid's, given when it was added); none: all solid.</summary>
    public IMeshSurface? Surface { get; internal set; }

    /// <summary>
    /// The given vertex each of the engine's stands for: it welds vertices at one place into the first of them and
    /// numbers them anew, so a surface's corners are told by these.
    /// </summary>
    internal int[] SourceVertices { get; set; } = [];

    /// <summary>A triangle of its is solid (always, without a surface).</summary>
    internal bool IsSolid(Jitter2.Collision.Shapes.TriangleShape triangle)
    {
        if (Surface is not { } surface)
        {
            return true;
        }
        var corners = triangle.Mesh.Indices[triangle.Index];
        return surface.IsSolid(SourceVertices[corners.IndexA], SourceVertices[corners.IndexB], SourceVertices[corners.IndexC]);
    }

    /// <summary>
    /// Told of each body touching it during a step, with the contact seen from the body (its normal pushing
    /// the body away from the mesh): a level's moving part learns what stands on it. Heard during the step,
    /// so it must not change the world.
    /// </summary>
    public Action<PhysicsBody, Contact>? Touched { get; set; }
}
