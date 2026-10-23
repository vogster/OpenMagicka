namespace OpenMagicka.Content.Xna;

/// <summary>
/// XNA 3.1 <c>SurfaceFormat</c> numbering (XNA 4.0 renumbered it). Magicka uses Color, Dxt1 and
/// Dxt5. <see cref="Color"/> pixels are stored B, G, R, A.
/// </summary>
public enum SurfaceFormatXna31
{
    Color = 1,
    Bgr32 = 2,
    Bgra1010102 = 3,
    Rgba32 = 4,
    Rgb32 = 5,
    Rgba1010102 = 6,
    Rg32 = 7,
    Rgba64 = 8,
    Bgr565 = 9,
    Bgra5551 = 10,
    Bgr555 = 11,
    Bgra4444 = 12,
    Bgr444 = 13,
    Bgra2338 = 14,
    Alpha8 = 15,
    Bgr233 = 16,
    Bgr24 = 17,
    NormalizedByte2 = 18,
    NormalizedByte4 = 19,
    NormalizedShort2 = 20,
    NormalizedShort4 = 21,
    Single = 22,
    Vector2 = 23,
    Vector4 = 24,
    HalfSingle = 25,
    HalfVector2 = 26,
    HalfVector4 = 27,
    Dxt1 = 28,
    Dxt2 = 29,
    Dxt3 = 30,
    Dxt4 = 31,
    Dxt5 = 32,
    Luminance8 = 33,
}

/// <summary>
/// 2D texture; <see cref="Levels"/> are mip levels from largest to smallest. Content records do
/// not copy their byte arrays: treat them as read-only, they may be shared with the loader.
/// </summary>
public sealed record Texture2DContent(SurfaceFormatXna31 Format, int Width, int Height, IReadOnlyList<byte[]> Levels);

public sealed record Texture3DContent(SurfaceFormatXna31 Format, int Width, int Height, int Depth, IReadOnlyList<byte[]> Levels);

/// <summary>Cube texture; <see cref="Faces"/> in CubeMapFace order (+X, -X, +Y, -Y, +Z, -Z), each with its mip levels.</summary>
public sealed record TextureCubeContent(SurfaceFormatXna31 Format, int Size, IReadOnlyList<IReadOnlyList<byte[]>> Faces);
