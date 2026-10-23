using System.Text;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Tests.Content;

/// <summary>Builds uncompressed XNB v4 files for reader tests.</summary>
internal sealed class XnbTestWriter
{
    public const string Xna = ", Microsoft.Xna.Framework, Version=3.1.0.0, Culture=neutral, PublicKeyToken=6d5c3888ef60e27d";
    public const string XnaContent = "Microsoft.Xna.Framework.Content.";

    private readonly List<string> readers = [];
    private readonly MemoryStream objects = new();

    public XnbTestWriter()
    {
        Writer = new BinaryWriter(objects, Encoding.UTF8);
    }

    public BinaryWriter Writer { get; }

    public int SharedResources { get; set; }

    /// <summary>Registers a reader (if new) and writes its 1-based type id.</summary>
    public XnbTestWriter TypeId(string readerName)
    {
        var index = readers.IndexOf(readerName);
        if (index < 0)
        {
            readers.Add(readerName);
            index = readers.Count - 1;
        }
        Writer.Write7BitEncodedInt(index + 1);
        return this;
    }

    public XnbTestWriter Null()
    {
        Writer.Write7BitEncodedInt(0);
        return this;
    }

    public XnbTestWriter StringObject(string value)
    {
        TypeId(XnaContent + "StringReader" + Xna);
        Writer.Write(value);
        return this;
    }

    public XnbTestWriter Floats(params float[] values)
    {
        foreach (var v in values)
        {
            Writer.Write(v);
        }
        return this;
    }

    public XnbTestWriter UInts(params uint[] values)
    {
        foreach (var v in values)
        {
            Writer.Write(v);
        }
        return this;
    }

    public XnbTestWriter Bytes(params byte[] values)
    {
        Writer.Write(values);
        return this;
    }

    public XnbFile Build() => XnbFile.Read(BuildBytes());

    public byte[] BuildBytes()
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body, Encoding.UTF8);
        w.Write7BitEncodedInt(readers.Count);
        foreach (var name in readers)
        {
            w.Write(name);
            w.Write(0);
        }
        w.Write7BitEncodedInt(SharedResources);
        w.Write(objects.ToArray());
        var data = body.ToArray();
        var file = new List<byte>("XNB"u8.ToArray()) { (byte)'w', 4, 0 };
        file.AddRange(BitConverter.GetBytes(10 + data.Length));
        file.AddRange(data);
        return [.. file];
    }
}
