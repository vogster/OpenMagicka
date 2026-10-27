using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OpenMagicka.Content.Effects;

/// <summary>
/// The skinned model effect of the original's animation library (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), which the
/// Tome's book is drawn with: it is not among the content's shaders but inside the player's own <c>XNAnimation.dll</c>,
/// next to the <c>Content</c> folder, as the data a static array of the library is initialised from. Read from there at
/// run time (nothing of it is kept in OpenMagicka): the one field with initial data that is an fx_2_0 effect, its size
/// the one its value type is laid out with.
/// </summary>
public static class XnAnimationEffect
{
    public const string Assembly = "XNAnimation.dll";

    /// <summary>The technique the book, its pages and the room are drawn with (lit by one point light, no G-buffer).</summary>
    public const string Forward = "AnimatedModelNonDeffered";

    /// <summary>An fx_2_0 effect's first four bytes, little-endian.</summary>
    public const uint Magic = 0xFEFF0901;

    /// <summary>The assembly in the game's folder (the one the content folder is in).</summary>
    public static string PathIn(string contentFolder)
    {
        ArgumentNullException.ThrowIfNull(contentFolder);
        var content = Path.GetFullPath(contentFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(Path.GetDirectoryName(content) ?? content, Assembly);
    }

    /// <summary>The effect's bytecode as the assembly holds it.</summary>
    /// <exception cref="FileNotFoundException">There is no such assembly.</exception>
    /// <exception cref="InvalidDataException">It is not an assembly, or holds no effect, or more than one.</exception>
    public static byte[] Read(string assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException($"no {Assembly} at {assembly}", assembly);
        }
        using var stream = File.OpenRead(assembly);
        return Read(stream, assembly);
    }

    /// <param name="what">The assembly, for the errors.</param>
    public static byte[] Read(Stream assembly, string what)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            using var pe = new PEReader(assembly, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata)
            {
                throw new InvalidDataException($"{what} is not a .NET assembly");
            }
            var metadata = pe.GetMetadataReader();
            var found = new List<byte[]>();
            foreach (var handle in metadata.FieldDefinitions)
            {
                var field = metadata.GetFieldDefinition(handle);
                var rva = field.GetRelativeVirtualAddress();
                if (rva != 0 && SizeOf(metadata, field) is > 4 and var size && pe.GetSectionData(rva) is { Length: var length } data && length >= size
                    && BinaryPrimitives.ReadUInt32LittleEndian(data.GetContent(0, 4).AsSpan()) == Magic)
                {
                    found.Add([.. data.GetContent(0, size)]);
                }
            }
            return found.Count switch
            {
                1 => found[0],
                0 => throw new InvalidDataException($"{what} holds no effect"),
                _ => throw new InvalidDataException($"{what} holds {found.Count} effects"),
            };
        }
        catch (BadImageFormatException e)
        {
            throw new InvalidDataException($"{what} does not read as an assembly: {e.Message}", e);
        }
    }

    /// <summary>The bytes a field's value type is laid out with; none for a field of another kind.</summary>
    private static int? SizeOf(MetadataReader metadata, FieldDefinition field)
    {
        var signature = metadata.GetBlobReader(field.Signature);
        if (signature.ReadSignatureHeader().Kind != SignatureKind.Field || signature.ReadCompressedInteger() != (int)SignatureTypeKind.ValueType)
        {
            return null;
        }
        return signature.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } type
            ? metadata.GetTypeDefinition((TypeDefinitionHandle)type).GetLayout().Size
            : null;
    }
}
