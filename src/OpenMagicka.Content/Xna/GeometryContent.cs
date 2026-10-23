namespace OpenMagicka.Content.Xna;

/// <summary>XNA 3.1 <c>VertexElementFormat</c> numbering.</summary>
public enum VertexElementFormatXna31 : byte
{
    Single = 0,
    Vector2 = 1,
    Vector3 = 2,
    Vector4 = 3,
    Color = 4,
    Byte4 = 5,
    Short2 = 6,
    Short4 = 7,
    Rgba32 = 8,
    NormalizedShort2 = 9,
    NormalizedShort4 = 10,
    Rg32 = 11,
    Rgba64 = 12,
    UInt101010 = 13,
    Normalized101010 = 14,
    HalfVector2 = 15,
    HalfVector4 = 16,
}

/// <summary>XNA 3.1 <c>VertexElementUsage</c> numbering.</summary>
public enum VertexElementUsageXna31 : byte
{
    Position = 0,
    BlendWeight = 1,
    BlendIndices = 2,
    Normal = 3,
    PointSize = 4,
    TextureCoordinate = 5,
    Tangent = 6,
    Binormal = 7,
    TessellateFactor = 8,
    Color = 10,
    Fog = 11,
    Depth = 12,
    Sample = 13,
}

public sealed record VertexElementContent(
    int Stream, int Offset, VertexElementFormatXna31 Format, byte Method, VertexElementUsageXna31 Usage, int UsageIndex)
{
    public int Size => Format switch
    {
        VertexElementFormatXna31.Single or VertexElementFormatXna31.Color or VertexElementFormatXna31.Byte4
            or VertexElementFormatXna31.Short2 or VertexElementFormatXna31.Rgba32
            or VertexElementFormatXna31.NormalizedShort2 or VertexElementFormatXna31.Rg32
            or VertexElementFormatXna31.UInt101010 or VertexElementFormatXna31.Normalized101010
            or VertexElementFormatXna31.HalfVector2 => 4,
        VertexElementFormatXna31.Vector2 or VertexElementFormatXna31.Short4
            or VertexElementFormatXna31.NormalizedShort4 or VertexElementFormatXna31.Rgba64
            or VertexElementFormatXna31.HalfVector4 => 8,
        VertexElementFormatXna31.Vector3 => 12,
        VertexElementFormatXna31.Vector4 => 16,
        _ => throw new InvalidDataException($"unknown vertex element format {(int)Format}"),
    };
}

/// <summary>
/// XNA 3.1 vertex declaration. The file does not store the stride; it is the end of the last
/// element.
/// </summary>
public sealed record VertexDeclarationContent(IReadOnlyList<VertexElementContent> Elements)
{
    public int Stride { get; } = Elements.Count == 0 ? 0 : Elements.Max(e => e.Offset + e.Size);
}

/// <summary>Raw vertex data (read-only, not copied); its layout comes from a separate declaration.</summary>
public sealed record VertexBufferContent(byte[] Data);

public sealed record IndexBufferContent(bool Is16Bit, byte[] Data);
