using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Xnb.Readers;

/// <summary>
/// XNA 3.1 content readers used by Magicka, keyed by <see cref="XnbTypeReader.Key"/>.
/// Layouts: docs/formats/XNB_READERS.md §0–1.
/// </summary>
internal static partial class StandardReaders
{
    public static IReadOnlyDictionary<string, XnbTypeReaderFunc> All { get; } = new Dictionary<string, XnbTypeReaderFunc>
    {
        ["StringReader"] = r => r.ReadString(),
        ["SingleReader"] = r => r.ReadSingle(),
        ["BooleanReader"] = r => r.ReadBoolean(),
        ["Vector3Reader"] = r => r.ReadVector3(),
        ["MatrixReader"] = r => r.ReadMatrix(),
        ["ExternalReferenceReader"] = r => r.ReadExternalReference(),
        ["ListReader<Vector3>"] = ReadVector3List,
        ["ListReader<String>"] = ReadStringList,
        ["DictionaryReader<String,Object>"] = ReadStringObjectDictionary,
        ["Texture2DReader"] = ReadTexture2D,
        ["Texture3DReader"] = ReadTexture3D,
        ["TextureCubeReader"] = ReadTextureCube,
        ["VertexDeclarationReader"] = ReadVertexDeclaration,
        ["VertexBufferReader"] = ReadVertexBuffer,
        ["IndexBufferReader"] = ReadIndexBuffer,
        ["EffectReader"] = ReadEffect,
        ["BasicEffectReader"] = ReadBasicEffect,
        ["EffectMaterialReader"] = ReadEffectMaterial,
        ["ModelReader"] = ReadModel,
    };

    private static IReadOnlyList<Vector3> ReadVector3List(XnbContentReader r)
    {
        var count = r.ReadCount(12);
        var items = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = r.ReadVector3();
        }
        return items;
    }

    private static IReadOnlyList<string?> ReadStringList(XnbContentReader r)
    {
        var count = r.ReadCount(1); // a null string object is one byte
        var items = new string?[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = r.ReadObject<string>();
        }
        return items;
    }

    private static IReadOnlyDictionary<string, object?> ReadStringObjectDictionary(XnbContentReader r)
    {
        var count = r.ReadCount(3); // key object (type id + length) and value type id
        var items = new Dictionary<string, object?>();
        for (var i = 0; i < count; i++)
        {
            var key = r.ReadObject<string>() ?? throw new InvalidDataException("null dictionary key");
            if (!items.TryAdd(key, r.ReadObject()))
            {
                throw new InvalidDataException($"duplicate dictionary key '{key}'");
            }
        }
        return items;
    }
}
