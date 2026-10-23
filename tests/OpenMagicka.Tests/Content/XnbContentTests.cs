using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;
using static OpenMagicka.Tests.Content.XnbTestWriter;

namespace OpenMagicka.Tests.Content;

public class XnbContentTests
{
    private static XnbContent Load(XnbTestWriter writer) => XnbContent.Load(writer.Build(), XnbReaderRegistry.Standard);

    [Fact]
    public void Root_String()
    {
        var content = Load(new XnbTestWriter().StringObject("goblin_die"));

        Assert.Equal("goblin_die", content.Root);
        Assert.Empty(content.SharedResources);
    }

    [Fact]
    public void Root_Null()
    {
        Assert.Null(Load(new XnbTestWriter().Null()).Root);
    }

    [Fact]
    public void Texture2D_KeepsXna31FormatAndMipLevels()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "Texture2DReader" + Xna);
        w.Writer.Write(1);                    // Color in XNA 3.1 numbering
        w.UInts(2, 1, 2);                     // 2x1, 2 mips
        w.UInts(8).Bytes(1, 2, 3, 4, 5, 6, 7, 8);
        w.UInts(4).Bytes(9, 9, 9, 9);

        var texture = Assert.IsType<Texture2DContent>(Load(w).Root);

        Assert.Equal(SurfaceFormatXna31.Color, texture.Format);
        Assert.Equal((2, 1), (texture.Width, texture.Height));
        Assert.Equal(2, texture.Levels.Count);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, texture.Levels[1]);
    }

    [Fact]
    public void TextureCube_ReadsSixFacesFaceMajor()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "TextureCubeReader" + Xna);
        w.Writer.Write(32);                   // Dxt5
        w.UInts(4, 1);
        for (byte face = 0; face < 6; face++)
        {
            w.UInts(1).Bytes(face);
        }

        var cube = Assert.IsType<TextureCubeContent>(Load(w).Root);

        Assert.Equal(SurfaceFormatXna31.Dxt5, cube.Format);
        Assert.Equal(6, cube.Faces.Count);
        Assert.Equal(new byte[] { 5 }, cube.Faces[5][0]);
    }

    [Fact]
    public void Texture3D_ReadsDepth()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "Texture3DReader" + Xna);
        w.Writer.Write(1);
        w.UInts(1, 1, 2, 1).UInts(8).Bytes(0, 0, 0, 0, 0, 0, 0, 0);

        var volume = Assert.IsType<Texture3DContent>(Load(w).Root);

        Assert.Equal(2, volume.Depth);
        Assert.Single(volume.Levels);
    }

    [Fact]
    public void VertexDeclaration_ComputesStrideFromElements()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "VertexDeclarationReader" + Xna);
        w.UInts(3);
        // stream, offset, format, method, usage, usage index
        w.Writer.Write((ushort)0); w.Writer.Write((ushort)0); w.Bytes(2, 0, 0, 0);   // Position Vector3
        w.Writer.Write((ushort)0); w.Writer.Write((ushort)12); w.Bytes(2, 0, 3, 0);  // Normal Vector3
        w.Writer.Write((ushort)0); w.Writer.Write((ushort)24); w.Bytes(1, 0, 5, 1);  // TexCoord1 Vector2

        var declaration = Assert.IsType<VertexDeclarationContent>(Load(w).Root);

        Assert.Equal(32, declaration.Stride);
        Assert.Equal(VertexElementUsageXna31.TextureCoordinate, declaration.Elements[2].Usage);
        Assert.Equal(1, declaration.Elements[2].UsageIndex);
    }

    [Fact]
    public void VertexAndIndexBuffers_AreRawBytes()
    {
        var vb = new XnbTestWriter().TypeId(XnaContent + "VertexBufferReader" + Xna).UInts(3).Bytes(1, 2, 3);
        var ib = new XnbTestWriter().TypeId(XnaContent + "IndexBufferReader" + Xna).Bytes(1).UInts(2).Bytes(7, 0);

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<VertexBufferContent>(Load(vb).Root).Data);
        var indices = Assert.IsType<IndexBufferContent>(Load(ib).Root);
        Assert.True(indices.Is16Bit);
        Assert.Equal(new byte[] { 7, 0 }, indices.Data);
    }

    [Fact]
    public void Effect_KeepsBytecode()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "EffectReader" + Xna).UInts(4).Bytes(1, 9, 0xFF, 0xFE);

        Assert.Equal(new byte[] { 1, 9, 0xFF, 0xFE }, Assert.IsType<EffectContent>(Load(w).Root).Bytecode);
    }

    [Fact]
    public void Collections_ListsAndDictionary()
    {
        var vectors = new XnbTestWriter().TypeId(XnaContent + "ListReader`1[[Microsoft.Xna.Framework.Vector3" + Xna + "]]" + Xna)
            .UInts(2).Floats(1, 2, 3, 4, 5, 6);
        var strings = new XnbTestWriter().TypeId(XnaContent + "ListReader`1[[System.String, mscorlib, Version=2.0.0.0]]" + Xna)
            .UInts(2).StringObject("a").Null();
        var dict = new XnbTestWriter().TypeId(XnaContent + "DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]]" + Xna)
            .UInts(2).StringObject("Alpha").TypeId(XnaContent + "SingleReader" + Xna).Floats(0.5f)
            .StringObject("Lit").TypeId(XnaContent + "BooleanReader" + Xna).Bytes(1);

        Assert.Equal(new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) }, Assert.IsAssignableFrom<IReadOnlyList<Vector3>>(Load(vectors).Root));
        Assert.Equal(new string?[] { "a", null }, Assert.IsAssignableFrom<IReadOnlyList<string?>>(Load(strings).Root));
        var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Load(dict).Root);
        Assert.Equal(0.5f, parameters["Alpha"]);
        Assert.Equal(true, parameters["Lit"]);
    }

    [Fact]
    public void Matrix_IsRowMajor()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "MatrixReader" + Xna)
            .Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 8, 9, 1);

        Assert.Equal(new Vector3(7, 8, 9), Assert.IsType<Matrix>(Load(w).Root).Translation);
    }

    [Fact]
    public void ExternalReference_KeepsRelativePath()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "ExternalReferenceReader" + Xna);
        w.Writer.Write(@"..\Textures\lava_0");

        Assert.Equal(new ExternalReference(@"..\Textures\lava_0"), Load(w).Root);
    }

    [Fact]
    public void BasicEffect_ReadsTextureReferenceAndColors()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "BasicEffectReader" + Xna);
        w.Writer.Write("tex_0");
        w.Floats(1, 0, 0, 0, 1, 0, 0, 0, 1, 16, 1).Bytes(0);

        var effect = Assert.IsType<BasicEffectContent>(Load(w).Root);

        Assert.Equal(new ExternalReference("tex_0"), effect.Texture);
        Assert.Equal(new Vector3(1, 0, 0), effect.DiffuseColor);
        Assert.Equal(16, effect.SpecularPower);
        Assert.False(effect.VertexColorEnabled);
    }

    [Fact]
    public void EffectMaterial_ReadsEffectAndParameters()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "EffectMaterialReader" + Xna);
        w.Writer.Write(@"..\Shaders\Maya\SkinnedModelEffect_0");
        w.TypeId(XnaContent + "DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]]" + Xna)
            .UInts(1).StringObject("Glow").TypeId(XnaContent + "SingleReader" + Xna).Floats(2);

        var material = Assert.IsType<EffectMaterialContent>(Load(w).Root);

        Assert.Equal(new ExternalReference(@"..\Shaders\Maya\SkinnedModelEffect_0"), material.Effect);
        Assert.Equal(2f, material.Parameters["Glow"]);
    }

    [Fact]
    public void Model_ReadsBonesMeshesPartsAndSharedEffect()
    {
        var w = new XnbTestWriter { SharedResources = 1 };
        w.TypeId(XnaContent + "ModelReader" + Xna).UInts(2);
        w.StringObject("root").Floats(Identity());
        w.Null().Floats(Translation(0, 1, 0));
        w.Bytes(0).UInts(1).Bytes(2);         // bone 0: no parent, child bone 1
        w.Bytes(1).UInts(0);                  // bone 1: parent bone 0
        w.UInts(1).TypeId(XnaContent + "VertexDeclarationReader" + Xna).UInts(1);
        w.Writer.Write((ushort)0); w.Writer.Write((ushort)0); w.Bytes(2, 0, 0, 0);
        w.UInts(1);                           // one mesh
        w.StringObject("body").Bytes(2).Floats(0, 0, 0, 5);
        w.TypeId(XnaContent + "VertexBufferReader" + Xna).UInts(36).Bytes(new byte[36]);
        w.TypeId(XnaContent + "IndexBufferReader" + Xna).Bytes(1).UInts(6).Bytes(0, 0, 1, 0, 2, 0);
        w.Null().UInts(1);                    // tag, one part
        w.UInts(0, 0, 3, 0, 1, 0).Null().Bytes(1); // part fields, tag, shared effect #1
        w.Bytes(1).Null();                    // root bone 0, tag
        w.TypeId(XnaContent + "EffectReader" + Xna).UInts(2).Bytes(1, 9);

        var content = Load(w);
        var model = Assert.IsType<ModelContent>(content.Root);

        Assert.Equal(["root", null], model.Bones.Select(b => b.Name));
        Assert.Null(model.Bones[0].Parent);
        Assert.Equal(0, model.Bones[1].Parent);
        Assert.Equal([1], model.Bones[0].Children);
        Assert.Equal(new Vector3(0, 1, 0), model.Bones[1].Transform.Translation);
        Assert.Equal(0, model.RootBone);
        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(("body", 1), (mesh.Name, mesh.ParentBone));
        Assert.Equal(5, mesh.Bounds.Radius);
        var part = Assert.Single(mesh.Parts);
        Assert.Equal((3, 1), (part.NumVertices, part.PrimitiveCount));
        Assert.Equal(12, model.VertexDeclarations[part.VertexDeclarationIndex].Stride);
        Assert.Equal(new SharedResourceRef(0), part.Effect);
        Assert.IsType<EffectContent>(content.Resolve(part.Effect!.Value));
    }

    [Fact]
    public void UnknownReader_IsNotSupported()
    {
        var w = new XnbTestWriter().TypeId("Magicka.ContentReaders.LevelModelReader, Magicka").Bytes(0);

        var error = Assert.Throws<XnbUnsupportedReaderException>(() => Load(w));
        Assert.Equal("LevelModelReader", error.ReaderKey);
    }

    [Fact]
    public void TrailingBytes_AreInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(new XnbTestWriter().StringObject("x").Bytes(0)));
    }

    [Fact]
    public void TypeIdOutsideTheTable_IsInvalid()
    {
        var w = new XnbTestWriter();
        w.Writer.Write7BitEncodedInt(3);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void ImpossibleCount_IsInvalidNotAnAllocation()
    {
        var w = new XnbTestWriter().TypeId(XnaContent + "VertexBufferReader" + Xna).UInts(0x7FFFFFF0);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void SharedReferenceOutsideTheTable_IsInvalid()
    {
        var w = new XnbTestWriter { SharedResources = 0 };
        w.TypeId(XnaContent + "ModelReader" + Xna).UInts(0).UInts(0).UInts(1);
        w.Null().Bytes(0).Floats(0, 0, 0, 1).Null().Null().Null().UInts(1);
        w.UInts(0, 0, 0, 0, 0, 0).Null().Bytes(1);
        w.Bytes(0).Null();

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void DeeplyNestedObjects_AreRejectedNotAStackOverflow()
    {
        var w = new XnbTestWriter();
        for (var i = 0; i < 100_000; i++)
        {
            w.TypeId(XnaContent + "DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]]" + Xna).UInts(1).StringObject("k");
        }
        w.Null();

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void HugeSharedResourceCount_IsInvalidNotAnAllocation()
    {
        var w = new XnbTestWriter { SharedResources = int.MaxValue }.StringObject("x");

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void Dictionary_NullOrDuplicateKeys_AreInvalid()
    {
        var nullKey = new XnbTestWriter().TypeId(XnaContent + "DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]]" + Xna).UInts(1).Null().Null();
        var duplicate = new XnbTestWriter().TypeId(XnaContent + "DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]]" + Xna)
            .UInts(2).StringObject("a").Null().StringObject("a").Null();

        Assert.Throws<InvalidDataException>(() => Load(nullKey));
        Assert.Throws<InvalidDataException>(() => Load(duplicate));
    }

    [Fact]
    public void Boolean_OtherThanZeroOrOne_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(new XnbTestWriter().TypeId(XnaContent + "BooleanReader" + Xna).Bytes(2)));
    }

    [Fact]
    public void Model_With255Bones_UsesFourByteBoneReferences()
    {
        const int bones = 255;
        var w = new XnbTestWriter().TypeId(XnaContent + "ModelReader" + Xna).UInts(bones);
        for (var i = 0; i < bones; i++)
        {
            w.Null().Floats(Identity());
        }
        w.UInts(0, 0);                        // bone 0: no parent, no children
        for (var i = 1; i < bones; i++)
        {
            w.UInts(1, 0);                    // parent bone 0
        }
        w.UInts(0, 0).UInts(bones).Null();    // no declarations, no meshes, root bone #255, tag

        var model = Assert.IsType<ModelContent>(Load(w).Root);

        Assert.Equal(0, model.Bones[254].Parent);
        Assert.Equal(254, model.RootBone);
    }

    [Fact]
    public void Resolve_ReferenceOutsideTheTable_Throws()
    {
        var content = Load(new XnbTestWriter().StringObject("x"));

        Assert.Throws<ArgumentOutOfRangeException>(() => content.Resolve(new SharedResourceRef(3)));
    }

    [Fact]
    public void GameReaderBug_IsReportedAsInvalidData()
    {
        var registry = XnbReaderRegistry.Standard.With([new("BrokenReader", _ => throw new InvalidCastException("bug"))]);
        var w = new XnbTestWriter().TypeId("Game.BrokenReader, Game");

        Assert.Throws<InvalidDataException>(() => XnbContent.Load(w.Build(), registry));
    }

    [Theory]
    [InlineData("Microsoft.Xna.Framework.Content.ListReader`1[[System.Collections.Generic.List`1[[System.String, mscorlib]], mscorlib]], Microsoft.Xna.Framework", "ListReader<List<String>>")]
    [InlineData("Microsoft.Xna.Framework.Content.ListReader`1[[System.Int32[], mscorlib]]", "ListReader<Int32[]>")]
    [InlineData("Microsoft.Xna.Framework.Content.ListReader`1[[broken", "Microsoft.Xna.Framework.Content.ListReader`1[[broken")]
    [InlineData("Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib]]", "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib]]")]
    public void TypeReader_Key_HandlesNestedGenericsAndRejectsMalformedNames(string name, string key)
    {
        Assert.Equal(key, new XnbTypeReader(name, 0).Key);
    }

    [Theory]
    [InlineData("Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Vector3, Microsoft.Xna.Framework, Version=3.1.0.0]]", "ListReader<Vector3>")]
    [InlineData("Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib],[System.Object, mscorlib]], Microsoft.Xna.Framework", "DictionaryReader<String,Object>")]
    [InlineData("PolygonHead.Pipeline.BiTreeModelReader, PolygonHead, Version=1.0.0.0", "BiTreeModelReader")]
    public void TypeReader_Key_NormalizesGenericArguments(string name, string key)
    {
        Assert.Equal(key, new XnbTypeReader(name, 0).Key);
    }

    private static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static float[] Translation(float x, float y, float z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
}
