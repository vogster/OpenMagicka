using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>Vertex declarations of XNA 3.1 content as FNA vertex elements (no GPU work).</summary>
public static class VertexLayouts
{
    /// <summary>
    /// Every element, for the original effects. Elements may alias (UV1 at UV0's offset). Note: an
    /// XNA 3.1 vertex Color is D3DCOLOR (B, G, R, A in memory) while FNA's Color reads R, G, B, A;
    /// the level geometry uses Vector4 colours, so no swizzle is done here.
    /// </summary>
    public static VertexElement[] Full(VertexDeclarationContent declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.Elements.Select(Element).ToArray();
    }

    /// <summary>Position, Normal and the first UV (those present), for FNA's BasicEffect.</summary>
    public static VertexElement[] Basic(VertexDeclarationContent declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.Elements
            .Where(e => e.UsageIndex == 0 && e.Usage is VertexElementUsageXna31.Position or VertexElementUsageXna31.Normal
                or VertexElementUsageXna31.TextureCoordinate)
            .Select(Element)
            .ToArray();
    }

    private static VertexElement Element(VertexElementContent element)
    {
        if (element.Stream != 0)
        {
            throw new InvalidDataException($"vertex element in stream {element.Stream}: only one stream is supported");
        }
        return new VertexElement(element.Offset, Format(element.Format), Usage(element.Usage), element.UsageIndex);
    }

    private static VertexElementFormat Format(VertexElementFormatXna31 format) => format switch
    {
        VertexElementFormatXna31.Single => VertexElementFormat.Single,
        VertexElementFormatXna31.Vector2 => VertexElementFormat.Vector2,
        VertexElementFormatXna31.Vector3 => VertexElementFormat.Vector3,
        VertexElementFormatXna31.Vector4 => VertexElementFormat.Vector4,
        VertexElementFormatXna31.Color => VertexElementFormat.Color,
        VertexElementFormatXna31.Byte4 => VertexElementFormat.Byte4,
        VertexElementFormatXna31.Short2 => VertexElementFormat.Short2,
        VertexElementFormatXna31.Short4 => VertexElementFormat.Short4,
        VertexElementFormatXna31.NormalizedShort2 => VertexElementFormat.NormalizedShort2,
        VertexElementFormatXna31.NormalizedShort4 => VertexElementFormat.NormalizedShort4,
        VertexElementFormatXna31.HalfVector2 => VertexElementFormat.HalfVector2,
        VertexElementFormatXna31.HalfVector4 => VertexElementFormat.HalfVector4,
        _ => throw new InvalidDataException($"vertex format {format} has no FNA equivalent"),
    };

    private static VertexElementUsage Usage(VertexElementUsageXna31 usage) => usage switch
    {
        VertexElementUsageXna31.Position => VertexElementUsage.Position,
        VertexElementUsageXna31.BlendWeight => VertexElementUsage.BlendWeight,
        VertexElementUsageXna31.BlendIndices => VertexElementUsage.BlendIndices,
        VertexElementUsageXna31.Normal => VertexElementUsage.Normal,
        VertexElementUsageXna31.PointSize => VertexElementUsage.PointSize,
        VertexElementUsageXna31.TextureCoordinate => VertexElementUsage.TextureCoordinate,
        VertexElementUsageXna31.Tangent => VertexElementUsage.Tangent,
        VertexElementUsageXna31.Binormal => VertexElementUsage.Binormal,
        VertexElementUsageXna31.TessellateFactor => VertexElementUsage.TessellateFactor,
        VertexElementUsageXna31.Color => VertexElementUsage.Color,
        VertexElementUsageXna31.Fog => VertexElementUsage.Fog,
        VertexElementUsageXna31.Depth => VertexElementUsage.Depth,
        VertexElementUsageXna31.Sample => VertexElementUsage.Sample,
        _ => throw new InvalidDataException($"vertex usage {usage} has no FNA equivalent"),
    };
}

/// <summary>XNA 3.1 texture formats and pixels as FNA takes them.</summary>
public static class TextureFormats
{
    public static SurfaceFormat ToFna(SurfaceFormatXna31 format) => format switch
    {
        SurfaceFormatXna31.Color => SurfaceFormat.Color,
        SurfaceFormatXna31.Dxt1 => SurfaceFormat.Dxt1,
        SurfaceFormatXna31.Dxt3 => SurfaceFormat.Dxt3,
        SurfaceFormatXna31.Dxt5 => SurfaceFormat.Dxt5,
        _ => throw new InvalidDataException($"texture format {format} is not supported"),
    };

    /// <summary>Pixel data for FNA: XNA 3.1 Color is B, G, R, A, FNA's is R, G, B, A (a copy); compressed data as is.</summary>
    public static byte[] Pixels(SurfaceFormatXna31 format, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (format != SurfaceFormatXna31.Color)
        {
            return data;
        }
        var rgba = (byte[])data.Clone();
        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            rgba[i] = data[i + 2];
            rgba[i + 1] = data[i + 1];
            rgba[i + 2] = data[i];
            rgba[i + 3] = data[i + 3];
        }
        return rgba;
    }
}

/// <summary>
/// One drawable batch of a level's static geometry (a BiTree root): raw vertex and index data,
/// the whole index range (the root node's), and the material's diffuse texture as referenced.
/// </summary>
public sealed record LevelBatch(
    byte[] VertexData,
    int Stride,
    int VertexCount,
    VertexDeclarationContent Declaration,
    byte[] IndexData,
    bool Is16BitIndices,
    int StartIndex,
    int PrimitiveCount,
    BoundingBox Bounds,
    ExternalReference? DiffuseTexture,
    Vector3 DiffuseColor,
    bool Additive,
    bool HasNormal,
    bool HasUv);

/// <summary>Turns a level model's BiTree into batches (no GPU work).</summary>
public static class LevelGeometry
{
    public static IReadOnlyList<LevelBatch> Batches(BiTreeModelContent geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var batches = new List<LevelBatch>();
        foreach (var root in geometry.Roots)
        {
            if (!root.Visible || root.NumVertices == 0 || root.Declaration is null || root.VertexBuffer is null
                || root.IndexBuffer is null)
            {
                continue;
            }
            Check(root);
            var (texture, color, additive) = root.Effect switch
            {
                RenderDeferredEffectContent m => (m.Layer0.DiffuseTexture, m.Layer0.DiffuseColor, false),
                AdditiveEffectContent a => (a.TextureEnabled ? a.Texture : null, a.ColorTint, true),
                _ => (null, Vector3.One, false),
            };
            if (root.Tree.PrimitiveCount == 0)
            {
                continue;
            }
            var basic = VertexLayouts.Basic(root.Declaration);
            batches.Add(new LevelBatch(root.VertexBuffer.Data, root.VertexStride, root.NumVertices, root.Declaration,
                root.IndexBuffer.Data, root.IndexBuffer.Is16Bit, root.Tree.StartIndex, root.Tree.PrimitiveCount, root.Tree.Bounds,
                texture, color, additive,
                basic.Any(e => e.VertexElementUsage == VertexElementUsage.Normal),
                basic.Any(e => e.VertexElementUsage == VertexElementUsage.TextureCoordinate)));
        }
        return batches;
    }

    /// <summary>The box a camera frames: the opaque geometry, since additive glows can float far outside it.</summary>
    public static BoundingBox FramingBounds(IReadOnlyList<LevelBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        var opaque = batches.Where(b => !b.Additive).Select(b => b.Bounds).ToList();
        var framing = opaque.Count > 0 ? opaque : batches.Select(b => b.Bounds).ToList();
        return framing.Count == 0 ? new BoundingBox() : framing.Aggregate(BoundingBox.CreateMerged);
    }

    private static void Check(BiTreeRootContent root)
    {
        var indexSize = root.IndexBuffer!.Is16Bit ? 2 : 4;
        var indices = root.IndexBuffer.Data.Length / indexSize;
        if (root.Tree.StartIndex < 0 || root.Tree.PrimitiveCount < 0 || (long)root.Tree.StartIndex + root.Tree.PrimitiveCount * 3L > indices)
        {
            throw new InvalidDataException($"a level batch draws {root.Tree.PrimitiveCount} triangles from index {root.Tree.StartIndex} of {indices}");
        }
        if ((long)root.NumVertices * root.VertexStride > root.VertexBuffer!.Data.Length)
        {
            throw new InvalidDataException($"a level batch has {root.NumVertices} vertices of {root.VertexStride} bytes in {root.VertexBuffer.Data.Length} bytes");
        }
    }
}
