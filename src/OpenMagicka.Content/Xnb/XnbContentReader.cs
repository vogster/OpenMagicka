using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb;

/// <summary>
/// Reads the objects of an XNB file in the XNA 3.1 ContentReader encoding. Every read is
/// bounds-checked: malformed data raises <see cref="InvalidDataException"/>.
/// </summary>
public sealed class XnbContentReader
{
    /// <summary>Nesting limit for objects inside objects; real content stays below 10.</summary>
    public const int MaxDepth = 64;

    private readonly BinaryReader reader;
    private readonly IReadOnlyList<XnbTypeReader> table;
    private readonly XnbReaderRegistry registry;
    private readonly XnbTypeReaderFunc?[] resolved;
    private int depth;
    private int currentType = -1;

    internal XnbContentReader(XnbFile file, XnbReaderRegistry registry)
    {
        var stream = MemoryMarshal.TryGetArray(file.Objects, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(file.Objects.ToArray(), writable: false);
        reader = new BinaryReader(stream, Encoding.UTF8);
        table = file.TypeReaders;
        this.registry = registry;
        resolved = new XnbTypeReaderFunc?[table.Count];
        SharedResourceCount = file.SharedResourceCount;
    }

    public int SharedResourceCount { get; }

    public long Remaining => reader.BaseStream.Length - reader.BaseStream.Position;

    /// <summary>Version of the reader whose data is being read, from the file's reader table.</summary>
    public int CurrentReaderVersion => currentType >= 0 ? table[currentType].Version : 0;

    /// <summary>A type id (0 = null, otherwise 1-based index into the reader table) and that type's data.</summary>
    public object? ReadObject()
    {
        var typeId = reader.Read7BitEncodedInt();
        if (typeId == 0)
        {
            return null;
        }
        if (typeId < 0 || typeId > table.Count)
        {
            throw new InvalidDataException($"type id {typeId} outside the reader table of {table.Count}");
        }
        if (depth >= MaxDepth)
        {
            throw new InvalidDataException($"objects nested deeper than {MaxDepth}");
        }
        var read = Resolve(typeId - 1);
        var outerType = currentType;
        depth++;
        currentType = typeId - 1;
        try
        {
            return read(this);
        }
        finally
        {
            depth--;
            currentType = outerType;
        }
    }

    public T? ReadObject<T>() where T : class => ReadObject() switch
    {
        null => null,
        T value => value,
        var other => throw new InvalidDataException($"expected {typeof(T).Name}, found {other.GetType().Name}"),
    };

    /// <summary>An object that is either an inline <typeparamref name="T"/> or an external reference.</summary>
    public ContentRef<T> ReadContentRef<T>() where T : class => ReadObject() switch
    {
        null => default,
        T inline => ContentRef<T>.FromInline(inline),
        ExternalReference external => ContentRef<T>.FromExternal(external),
        var other => throw new InvalidDataException($"expected {typeof(T).Name} or a reference, found {other.GetType().Name}"),
    };

    /// <summary>0 = null, otherwise a 1-based index into the file's shared resources.</summary>
    public SharedResourceRef? ReadSharedResourceRef()
    {
        var index = reader.Read7BitEncodedInt();
        if (index == 0)
        {
            return null;
        }
        if (index < 0 || index > SharedResourceCount)
        {
            throw new InvalidDataException($"shared resource {index} outside the table of {SharedResourceCount}");
        }
        return new SharedResourceRef(index - 1);
    }

    /// <summary>An inline path to another asset; an empty string means null.</summary>
    public ExternalReference? ReadExternalReference()
    {
        var path = ReadString();
        return path.Length == 0 ? null : new ExternalReference(path);
    }

    /// <summary>A u32 element count, checked against the bytes left so it cannot force a huge allocation.</summary>
    public int ReadCount(int minBytesPerItem)
    {
        var count = reader.ReadUInt32();
        if (count > int.MaxValue || (long)count * Math.Max(minBytesPerItem, 1) > Remaining)
        {
            throw new InvalidDataException($"count {count} exceeds the remaining {Remaining} bytes");
        }
        return (int)count;
    }

    /// <summary>A u32 byte count followed by that many bytes.</summary>
    public byte[] ReadSizedBytes() => ReadBytes(ReadCount(1));

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new InvalidDataException($"{count} bytes requested, {Remaining} left");
        }
        return reader.ReadBytes(count);
    }

    public string ReadString() => reader.ReadString();

    /// <summary>
    /// One UTF-8 encoded character as written by BinaryWriter.Write(char): 1 to 3 bytes. A 4-byte
    /// sequence (outside the BMP) is accepted as well, deliberately wider than the writer.
    /// </summary>
    public Rune ReadUtf8Char()
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = reader.ReadByte();
        var length = bytes[0] switch
        {
            < 0x80 => 1,
            >= 0xC2 and < 0xE0 => 2,
            >= 0xE0 and < 0xF0 => 3,
            >= 0xF0 and < 0xF5 => 4,
            _ => throw new InvalidDataException($"invalid UTF-8 lead byte 0x{bytes[0]:X2}"),
        };
        for (var i = 1; i < length; i++)
        {
            bytes[i] = reader.ReadByte();
        }
        if (Rune.DecodeFromUtf8(bytes[..length], out var rune, out var consumed) != System.Buffers.OperationStatus.Done
            || consumed != length)
        {
            throw new InvalidDataException("invalid UTF-8 character");
        }
        return rune;
    }

    public bool ReadBoolean() => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        var b => throw new InvalidDataException($"boolean with value {b}"),
    };

    public byte ReadByte() => reader.ReadByte();

    public sbyte ReadSByte() => reader.ReadSByte();

    public short ReadInt16() => reader.ReadInt16();

    public ushort ReadUInt16() => reader.ReadUInt16();

    public long ReadInt64() => reader.ReadInt64();

    public double ReadDouble() => reader.ReadDouble();

    /// <summary>XNA 3.1 packed color: 4 bytes in B, G, R, A order (D3DCOLOR).</summary>
    public Color ReadColor()
    {
        var b = reader.ReadByte();
        var g = reader.ReadByte();
        var r = reader.ReadByte();
        var a = reader.ReadByte();
        return new Color(r, g, b, a);
    }

    public int ReadInt32() => reader.ReadInt32();

    public uint ReadUInt32() => reader.ReadUInt32();

    public float ReadSingle() => reader.ReadSingle();

    public Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());

    public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());

    public Vector4 ReadVector4() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    public Quaternion ReadQuaternion() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>16 floats M11..M44, XNA row-major order (row 4 is the translation).</summary>
    public Matrix ReadMatrix() => new(
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    public BoundingSphere ReadBoundingSphere() => new(ReadVector3(), ReadSingle());

    public BoundingBox ReadBoundingBox() => new(ReadVector3(), ReadVector3());

    private XnbTypeReaderFunc Resolve(int index)
    {
        if (resolved[index] is { } known)
        {
            return known;
        }
        var key = table[index].Key;
        if (!registry.TryGet(key, out var found))
        {
            throw new XnbUnsupportedReaderException(key);
        }
        resolved[index] = found;
        return found;
    }
}
