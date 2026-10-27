using System.Buffers.Binary;
using System.Numerics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Gameplay.Liquids;

namespace OpenMagicka.Engine.Physics;

/// <summary>
/// A level's water and lava as the game plays them (OM-88, docs/engine/ARCHITECTURE.md §7.5): each liquid's sheet from
/// its own drawn vertices and indices, as the original's (there is no other collision for it), each triangle's second
/// and third corners swapped so it faces up, the freeze values starting at 0 (1 for one that freezes by itself).
/// </summary>
public static class LiquidSheets
{
    /// <summary>A liquid's sheet; none for one without vertices or indices (nothing to stand on or draw).</summary>
    public static Liquid? From(LiquidContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var kind = content.Effect switch
        {
            RenderDeferredLiquidEffectContent => LiquidKind.Water,
            LavaEffectContent => LiquidKind.Lava,
            _ => throw new InvalidDataException($"a liquid's effect is {content.Effect?.GetType().Name ?? "missing"}, neither water nor lava"),
        };
        if (content.VertexBuffer is not { } vertexBuffer || content.IndexBuffer is not { } indexBuffer || content.Declaration is not { } declaration
            || content.NumVertices <= 0 || content.PrimitiveCount <= 0)
        {
            return null;
        }
        var position = declaration.Elements.FirstOrDefault(e => e.Usage == VertexElementUsageXna31.Position && e.UsageIndex == 0)
            ?? throw new InvalidDataException("a liquid's vertices have no position");
        var stride = content.VertexStride;
        if (stride <= 0 || (long)content.NumVertices * stride > vertexBuffer.Data.Length || position.Offset + 12 > stride)
        {
            throw new InvalidDataException($"a liquid's {content.NumVertices} vertices do not fit its buffer");
        }
        var data = vertexBuffer.Data.AsSpan();
        var vertices = new Vector3[content.NumVertices];
        for (var v = 0; v < vertices.Length; v++)
        {
            var at = data[(v * stride + position.Offset)..];
            vertices[v] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(at), BinaryPrimitives.ReadSingleLittleEndian(at[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(at[8..]));
        }
        var size = indexBuffer.Is16Bit ? 2 : 4;
        var count = content.PrimitiveCount * 3;
        if ((long)count * size > indexBuffer.Data.Length)
        {
            throw new InvalidDataException($"a liquid's {content.PrimitiveCount} triangles do not fit its indices");
        }
        var triangles = new int[count];
        for (var i = 0; i < count; i++)
        {
            var at = indexBuffer.Data.AsSpan(i * size);
            triangles[i] = indexBuffer.Is16Bit ? BinaryPrimitives.ReadUInt16LittleEndian(at) : BinaryPrimitives.ReadInt32LittleEndian(at);
        }
        for (var t = 0; t < count; t += 3)
        {
            (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
        return new Liquid(kind, vertices, triangles, content.HasCollision, content.Freezable, content.AutoFreeze);
    }
}
