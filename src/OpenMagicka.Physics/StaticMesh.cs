namespace OpenMagicka.Physics;

/// <summary>An immovable triangle mesh of a <see cref="PhysicsWorld"/>, such as one material of a level.</summary>
public sealed class StaticMesh
{
    internal StaticMesh(int material, int triangleCount, float friction)
    {
        Material = material;
        TriangleCount = triangleCount;
        Friction = friction;
    }

    /// <summary>Reported by the hits on this mesh.</summary>
    public int Material { get; }

    /// <summary>The triangles kept: degenerate ones are dropped.</summary>
    public int TriangleCount { get; }

    public float Friction { get; }
}
