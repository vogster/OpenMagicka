namespace OpenMagicka.Physics;

/// <summary>
/// An immovable triangle mesh of a <see cref="PhysicsWorld"/>: one material of a level, or a solid
/// thing that stands in the world without being the level (a spell's shield).
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
}
