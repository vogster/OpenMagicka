namespace OpenMagicka.Physics;

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

    /// <summary>
    /// Told of each body touching it during a step, with the contact seen from the body (its normal pushing
    /// the body away from the mesh): a level's moving part learns what stands on it. Heard during the step,
    /// so it must not change the world.
    /// </summary>
    public Action<PhysicsBody, Contact>? Touched { get; set; }
}
