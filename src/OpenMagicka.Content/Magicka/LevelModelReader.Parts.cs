using Microsoft.Xna.Framework;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

internal static partial class LevelModelReader
{
    private const ushort NoNeighbour = 0xFFFF;

    // three u16 vertices, three u16 neighbours, three f32 costs, one u8 movement flag
    private const int NavTriangleSize = 6 + 6 + 12 + 1;

    private static AnimatedLevelPartContent ReadAnimatedPart(XnbContentReader r, int depth)
    {
        if (depth >= MaxPartDepth)
        {
            throw new InvalidDataException($"animated level parts nested deeper than {MaxPartDepth}");
        }
        var name = r.ReadString();
        var affectShields = r.ReadBoolean();
        var model = r.ReadObject<ModelContent>();
        var meshSettings = r.ReadList(1 + 2, x => new MeshSetting(x.ReadString(), x.ReadBoolean(), x.ReadBoolean()));
        var liquids = r.ReadList(4 + 12 + 3, ReadLiquid);
        var locators = r.ReadList(1 + 68, ReadLocator);
        var duration = r.ReadSingle();
        var animation = XnAnimationReaders.ReadKeyframes(r);
        var effects = r.ReadList(2 + 28, ReadEffectPlacement);
        var lights = r.ReadList(1 + 64, x => new PartLightAttachment(x.ReadString(), x.ReadMatrix()));
        var collision = r.ReadBoolean() ? ReadPartCollision(r) : null;
        var navMesh = r.ReadBoolean() ? ReadNavMesh(r) : null;
        var children = r.ReadList(MinPartSize, x => ReadAnimatedPart(x, depth + 1));
        return new AnimatedLevelPartContent(name, affectShields, model, meshSettings, liquids, locators,
            duration, animation, effects, lights, collision, navMesh, children);
    }

    private static PartCollisionContent ReadPartCollision(XnbContentReader r)
    {
        var material = (CollisionMaterial)r.ReadByte();
        if (!Enum.IsDefined(material))
        {
            throw new InvalidDataException($"unknown collision material {(int)material}");
        }
        return new PartCollisionContent(material, ReadCollisionMesh(r));
    }

    /// <summary>A List&lt;Vector3&gt; object of vertices, then an i32 count of index triples.</summary>
    internal static CollisionMeshContent ReadCollisionMesh(XnbContentReader r)
    {
        var vertices = r.ReadObject<IReadOnlyList<Vector3>>()
            ?? throw new InvalidDataException("collision mesh without a vertex list");
        var triangles = r.ReadList(12, x => new CollisionTriangle(x.ReadInt32(), x.ReadInt32(), x.ReadInt32()));
        foreach (var t in triangles)
        {
            if (!InRange(t.I0, vertices.Count) || !InRange(t.I1, vertices.Count) || !InRange(t.I2, vertices.Count))
            {
                throw new InvalidDataException($"collision triangle {t} outside {vertices.Count} vertices");
            }
        }
        return new CollisionMeshContent(vertices, triangles);
    }

    /// <summary>u16 vertex count and vertices, u16 triangle count and 25-byte triangles.</summary>
    private static NavMeshContent ReadNavMesh(XnbContentReader r)
    {
        var vertices = new Vector3[CheckedU16(r, 12)];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = r.ReadVector3();
        }
        var triangles = new NavTriangle[CheckedU16(r, NavTriangleSize)];
        for (var i = 0; i < triangles.Length; i++)
        {
            var a = r.ReadUInt16();
            var b = r.ReadUInt16();
            var c = r.ReadUInt16();
            if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
            {
                throw new InvalidDataException($"nav triangle ({a}, {b}, {c}) outside {vertices.Length} vertices");
            }
            triangles[i] = new NavTriangle(a, b, c,
                Neighbour(r.ReadUInt16()), Neighbour(r.ReadUInt16()), Neighbour(r.ReadUInt16()),
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
                (MovementProperties)r.ReadByte());
        }
        foreach (var t in triangles)
        {
            if (t.NeighbourA >= triangles.Length || t.NeighbourB >= triangles.Length || t.NeighbourC >= triangles.Length)
            {
                throw new InvalidDataException($"nav triangle neighbour outside {triangles.Length} triangles");
            }
        }
        return new NavMeshContent(vertices, triangles);
    }

    private static ushort? Neighbour(ushort value) => value == NoNeighbour ? null : value;

    private static int CheckedU16(XnbContentReader r, int itemSize)
    {
        var count = r.ReadUInt16();
        if ((long)count * itemSize > r.Remaining)
        {
            throw new InvalidDataException($"nav mesh count {count} exceeds the remaining {r.Remaining} bytes");
        }
        return count;
    }

    private static bool InRange(int index, int count) => index >= 0 && index < count;
}
