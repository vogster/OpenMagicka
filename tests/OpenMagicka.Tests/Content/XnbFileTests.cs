using System.Text;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Tests.Content;

public class XnbFileTests
{
    private static byte[] Body(params (string Name, int Version)[] readers)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write7BitEncodedInt(readers.Length);
        foreach (var (name, version) in readers)
        {
            writer.Write(name);
            writer.Write(version);
        }
        writer.Write7BitEncodedInt(2);   // shared resources
        writer.Write((byte)1);           // primary object: reader #1
        writer.Write(42);
        return stream.ToArray();
    }

    private static byte[] Uncompressed(byte[] body, byte version = 4, string magic = "XNB")
    {
        var header = new List<byte>(Encoding.ASCII.GetBytes(magic)) { (byte)'w', version, 0 };
        header.AddRange(BitConverter.GetBytes(10 + body.Length));
        return [.. header, .. body];
    }

    private static byte[] Compressed(byte[] body)
    {
        var encoder = new LzxTestEncoder().VerbatimBlock(body.Length);
        foreach (var b in body)
        {
            encoder.Literal(b);
        }
        var frame = encoder.TakeFrame();
        var file = new List<byte>("XNB"u8.ToArray()) { (byte)'w', 4, 0x80 };
        file.AddRange(BitConverter.GetBytes(14 + 2 + frame.Length));
        file.AddRange(BitConverter.GetBytes(body.Length));
        file.Add((byte)(frame.Length >> 8));
        file.Add((byte)frame.Length);
        file.AddRange(frame);
        return [.. file];
    }

    private const string Texture = "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework, Version=3.1.0.0, Culture=neutral";

    [Fact]
    public void Read_UncompressedFile_ParsesHeaderAndReaderTable()
    {
        var xnb = XnbFile.Read(Uncompressed(Body((Texture, 0), ("Magicka.ContentReaders.ItemReader, Magicka", 1))));

        Assert.Equal('w', xnb.Platform);
        Assert.Equal(4, xnb.FormatVersion);
        Assert.False(xnb.IsCompressed);
        Assert.Equal(2, xnb.TypeReaders.Count);
        Assert.Equal(new XnbTypeReader(Texture, 0), xnb.TypeReaders[0]);
        Assert.Equal(1, xnb.TypeReaders[1].Version);
        Assert.Equal(2, xnb.SharedResourceCount);
        Assert.Equal(new byte[] { 1, 42, 0, 0, 0 }, xnb.Objects.ToArray());
    }

    [Fact]
    public void Read_CompressedFile_DecompressesTheBody()
    {
        var body = Body((Texture, 0));

        var xnb = XnbFile.Read(Compressed(body));

        Assert.True(xnb.IsCompressed);
        Assert.Equal(Texture, Assert.Single(xnb.TypeReaders).Name);
        Assert.Equal(new byte[] { 1, 42, 0, 0, 0 }, xnb.Objects.ToArray());
    }

    [Theory]
    [InlineData("Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework, Version=3.1.0.0", "Texture2DReader")]
    [InlineData("Magicka.ContentReaders.LevelModelReader, Magicka", "LevelModelReader")]
    [InlineData("Microsoft.Xna.Framework.Content.ListReader`1[[System.String, mscorlib, Version=2.0.0.0]], Microsoft.Xna.Framework", "ListReader`1[[System.String, mscorlib, Version=2.0.0.0]]")]
    public void TypeReader_ShortName_StripsNamespaceAndAssembly(string name, string expected)
    {
        Assert.Equal(expected, new XnbTypeReader(name, 0).ShortName);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        Assert.Throws<InvalidDataException>(() => XnbFile.Read(Uncompressed(Body(), magic: "XNA")));
    }

    [Fact]
    public void Read_Xna40File_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => XnbFile.Read(Uncompressed(Body(), version: 5)));
        Assert.Contains("version 5", error.Message);
    }

    [Fact]
    public void Read_SizeLargerThanData_Throws()
    {
        var file = Uncompressed(Body((Texture, 0)));

        Assert.Throws<InvalidDataException>(() => XnbFile.Read(file[..^3]));
    }

    [Fact]
    public void Read_TwoFramesWithExplicitFrameHeader_AreJoined()
    {
        var encoder = new LzxTestEncoder().VerbatimBlock(6).Literal((byte)'x').Literal((byte)'y');
        var first = encoder.TakeFrame();
        var second = encoder.OffsetMatch(offset: 2, length: 2).RepeatMatch(2).TakeFrame();
        var body = new List<byte>();
        // 0xFF form: frame size 2, then block size.
        body.AddRange([0xFF, 0, 2, (byte)(first.Length >> 8), (byte)first.Length]);
        body.AddRange(first);
        body.AddRange([(byte)(second.Length >> 8), (byte)second.Length]);
        body.AddRange(second);
        var file = new List<byte>("XNB"u8.ToArray()) { (byte)'w', 4, 0x80 };
        file.AddRange(BitConverter.GetBytes(14 + body.Count));
        file.AddRange(BitConverter.GetBytes(6));
        file.AddRange(body);

        // "xyxyxy" is not a valid reader table, but decompression must succeed first.
        var error = Assert.Throws<InvalidDataException>(() => XnbFile.Read([.. file]));
        Assert.Contains("type reader", error.Message);
    }

    [Fact]
    public void Read_FrameLargerThan32K_Throws()
    {
        var file = new List<byte>("XNB"u8.ToArray()) { (byte)'w', 4, 0x80 };
        file.AddRange(BitConverter.GetBytes(14 + 5 + 4));
        file.AddRange(BitConverter.GetBytes(0x9000));
        file.AddRange([0xFF, 0x90, 0x00, 0, 4, 0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => XnbFile.Read([.. file]));
    }

    [Fact]
    public void Read_DecompressedSizeImpossibleForTheData_Throws()
    {
        var file = new List<byte>("XNB"u8.ToArray()) { (byte)'w', 4, 0x80 };
        file.AddRange(BitConverter.GetBytes(14));
        file.AddRange(BitConverter.GetBytes(200 * 1024 * 1024));

        Assert.Throws<InvalidDataException>(() => XnbFile.Read([.. file]));
    }

    [Fact]
    public void Read_NegativeStringLength_Throws()
    {
        var body = new byte[] { 1, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };

        Assert.Throws<InvalidDataException>(() => XnbFile.Read(Uncompressed(body)));
    }

    [Theory]
    [InlineData("A[[B")]
    [InlineData("]]x[[")]
    [InlineData("")]
    [InlineData("Plain")]
    public void TypeReader_ShortName_NeverThrowsOnMalformedNames(string name)
    {
        var shortName = new XnbTypeReader(name, 0).ShortName;

        Assert.NotNull(shortName);
    }

    [Fact]
    public void Read_MutatedFiles_OnlyEverThrowInvalidData()
    {
        var valid = Compressed(Body((Texture, 0), ("Magicka.ContentReaders.ItemReader, Magicka", 1)));
        var random = new Random(1234);
        for (var i = 0; i < 2000; i++)
        {
            var mutated = (byte[])valid.Clone();
            for (var m = random.Next(1, 4); m > 0; m--)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }
            try
            {
                XnbFile.Read(mutated);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void Read_ReaderCountBeyondData_Throws()
    {
        var body = new byte[] { 0x7F };

        Assert.Throws<InvalidDataException>(() => XnbFile.Read(Uncompressed(body)));
    }
}
