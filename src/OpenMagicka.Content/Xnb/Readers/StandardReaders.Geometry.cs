using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb.Readers;

internal static partial class StandardReaders
{
    /// <summary>u32 count, then 8 bytes per element; XNA 3.1 stores no stride.</summary>
    private static VertexDeclarationContent ReadVertexDeclaration(XnbContentReader r)
    {
        var count = r.ReadCount(8);
        var elements = new VertexElementContent[count];
        for (var i = 0; i < count; i++)
        {
            var stream = r.ReadUInt16();
            var offset = r.ReadUInt16();
            var format = (VertexElementFormatXna31)r.ReadByte();
            var method = r.ReadByte();
            var usage = (VertexElementUsageXna31)r.ReadByte();
            var usageIndex = r.ReadByte();
            if (!Enum.IsDefined(format) || !Enum.IsDefined(usage))
            {
                throw new InvalidDataException($"unknown vertex element format {(int)format} or usage {(int)usage}");
            }
            elements[i] = new VertexElementContent(stream, offset, format, method, usage, usageIndex);
        }
        return new VertexDeclarationContent(elements);
    }

    private static VertexBufferContent ReadVertexBuffer(XnbContentReader r) => new(r.ReadSizedBytes());

    private static IndexBufferContent ReadIndexBuffer(XnbContentReader r)
    {
        var is16Bit = r.ReadBoolean();
        return new IndexBufferContent(is16Bit, r.ReadSizedBytes());
    }
}
