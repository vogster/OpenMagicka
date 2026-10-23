using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb.Readers;

internal static partial class StandardReaders
{
    private const int MaxTextureSize = 16384;
    private const int MaxMipLevels = 16;

    private static Texture2DContent ReadTexture2D(XnbContentReader r)
    {
        var format = ReadSurfaceFormat(r);
        var width = ReadDimension(r);
        var height = ReadDimension(r);
        return new Texture2DContent(format, width, height, ReadMipLevels(r, ReadMipCount(r)));
    }

    private static Texture3DContent ReadTexture3D(XnbContentReader r)
    {
        var format = ReadSurfaceFormat(r);
        var width = ReadDimension(r);
        var height = ReadDimension(r);
        var depth = ReadDimension(r);
        return new Texture3DContent(format, width, height, depth, ReadMipLevels(r, ReadMipCount(r)));
    }

    /// <summary>Six faces in CubeMapFace order, each with all of its mip levels (face-major).</summary>
    private static TextureCubeContent ReadTextureCube(XnbContentReader r)
    {
        var format = ReadSurfaceFormat(r);
        var size = ReadDimension(r);
        var mips = ReadMipCount(r);
        var faces = new IReadOnlyList<byte[]>[6];
        for (var face = 0; face < faces.Length; face++)
        {
            faces[face] = ReadMipLevels(r, mips);
        }
        return new TextureCubeContent(format, size, faces);
    }

    private static SurfaceFormatXna31 ReadSurfaceFormat(XnbContentReader r)
    {
        var format = (SurfaceFormatXna31)r.ReadInt32();
        return Enum.IsDefined(format) ? format : throw new InvalidDataException($"unknown XNA 3.1 surface format {(int)format}");
    }

    private static int ReadDimension(XnbContentReader r)
    {
        var value = r.ReadUInt32();
        return value is >= 1 and <= MaxTextureSize ? (int)value : throw new InvalidDataException($"texture dimension {value}");
    }

    private static int ReadMipCount(XnbContentReader r)
    {
        var value = r.ReadUInt32();
        return value is >= 1 and <= MaxMipLevels ? (int)value : throw new InvalidDataException($"texture mip count {value}");
    }

    private static byte[][] ReadMipLevels(XnbContentReader r, int count)
    {
        var levels = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            levels[i] = r.ReadSizedBytes();
        }
        return levels;
    }
}
