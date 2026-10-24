using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>Meshes for things spells raise in the world.</summary>
public static class Shapes
{
    /// <summary>A ball, its faces outward: a solid barrier, a rock pillar.</summary>
    public static (List<Vector3>, List<MeshTriangle>) Ball(Vector3 centre, float radius)
    {
        const int slices = 12, stacks = 6;
        var vertices = new List<Vector3> { centre + Vector3.UnitY * radius, centre - Vector3.UnitY * radius };
        for (var stack = 1; stack < stacks; stack++)
        {
            var polar = MathF.PI * stack / stacks;
            for (var slice = 0; slice < slices; slice++)
            {
                var azimuth = MathF.Tau * slice / slices;
                vertices.Add(centre + new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(azimuth)) * radius);
            }
        }
        var triangles = new List<MeshTriangle>();
        int At(int stack, int slice) => 2 + (stack - 1) * slices + slice % slices;
        for (var slice = 0; slice < slices; slice++)
        {
            triangles.Add(new MeshTriangle(0, At(1, slice + 1), At(1, slice)));
            triangles.Add(new MeshTriangle(1, At(stacks - 1, slice), At(stacks - 1, slice + 1)));
            for (var stack = 1; stack < stacks - 1; stack++)
            {
                triangles.Add(new MeshTriangle(At(stack, slice), At(stack, slice + 1), At(stack + 1, slice + 1)));
                triangles.Add(new MeshTriangle(At(stack, slice), At(stack + 1, slice + 1), At(stack + 1, slice)));
            }
        }
        return (vertices, triangles);
    }
}
