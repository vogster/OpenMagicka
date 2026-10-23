using System.Text;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;
using static OpenMagicka.Tests.Content.XnbTestWriter;

namespace OpenMagicka.Tests.Content;

/// <summary>Readers of XNAnimation 0.7 and Arrowhead's PolygonHead engine.</summary>
public class EngineReadersTests
{
    private const string Pipeline = "PolygonHead.Pipeline.";
    private const string PolygonHeadAsm = ", PolygonHead, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private const string XnAnim = "XNAnimation.Pipeline.";
    private const string XnAnimAsm = ", XNAnimation, Version=0.7.0.0, Culture=neutral, PublicKeyToken=null";

    private static XnbContent Load(XnbTestWriter writer) => XnbContent.Load(writer.Build(), XnbReaderRegistry.Magicka);

    private static XnbTestWriter Ext(XnbTestWriter w, string path)
    {
        w.Writer.Write(path);
        return w;
    }

    private static XnbTestWriter Pose(XnbTestWriter w, float x) => w.Floats(x, 0, 0, 0, 0, 0, 1, 1, 1, 1);

    [Fact]
    public void AnimationClip_ReadsChannelsAndKeyframes()
    {
        var w = new XnbTestWriter().TypeId(XnAnim + "AnimationClipReader" + XnAnimAsm);
        w.Writer.Write("idle");
        w.Floats(1.5f);
        w.Writer.Write(1);                    // channels
        w.Writer.Write("spine");
        w.Writer.Write(2);                    // keyframes
        Pose(w.Floats(0), 1);
        Pose(w.Floats(1), 2);

        var clip = Assert.IsType<AnimationClipContent>(Load(w).Root);

        Assert.Equal(("idle", 1.5f), (clip.Name, clip.Duration));
        var channel = Assert.Single(clip.Channels);
        Assert.Equal("spine", channel.BoneName);
        Assert.Equal(1f, channel.Keyframes[1].Time);
        Assert.Equal(new Vector3(2, 0, 0), channel.Keyframes[1].Pose.Translation);
        Assert.Equal(Quaternion.Identity, channel.Keyframes[1].Pose.Orientation);
    }

    [Fact]
    public void SkinnedModel_ReferencesBonesAndClipsInSharedResources()
    {
        var w = new XnbTestWriter { SharedResources = 2 };
        w.TypeId(XnAnim + "SkinnedModelReader" + XnAnimAsm);
        w.TypeId(XnaContent + "ModelReader" + Xna).UInts(0, 0, 0).Bytes(0).Null(); // empty model
        w.Writer.Write(1); w.Bytes(1);       // one bone: shared #1
        w.Writer.Write(1); w.Bytes(2);       // one clip: shared #2
        w.TypeId(XnAnim + "SkinnedModelBoneReader" + XnAnimAsm);
        w.Writer.Write((ushort)0);
        w.Writer.Write("root");
        Pose(w, 0).Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        w.Bytes(0);                          // no parent
        w.Writer.Write(0);                   // no children
        w.TypeId(XnAnim + "AnimationClipReader" + XnAnimAsm);
        w.Writer.Write("walk");
        w.Floats(2);
        w.Writer.Write(0);

        var content = Load(w);
        var skinned = Assert.IsType<SkinnedModelContent>(content.Root);

        Assert.NotNull(skinned.Model);
        var bone = Assert.IsType<SkinnedModelBoneContent>(content.Resolve(skinned.Bones[0]!.Value));
        Assert.Equal(("root", 0), (bone.Name, bone.Index));
        Assert.Null(bone.Parent);
        Assert.Equal("walk", Assert.IsType<AnimationClipContent>(content.Resolve(skinned.Clips[0]!.Value)).Name);
    }

    [Fact]
    public void SkinnedModelBasicEffect_TexturesAreObjectsInlineOrExternal()
    {
        var w = new XnbTestWriter().TypeId(XnAnim + "SkinnedModelBasicEffectReader" + XnAnimAsm);
        w.Bytes(1).Floats(0.5f, 1, 1, 1, 0.2f, 32, 1).Bytes(0, 1, 0, 0, 0, 0, 0);
        Ext(w.TypeId(XnaContent + "ExternalReferenceReader" + Xna), "skin_0");
        w.Null().Null().Null().Null().Null();

        var effect = Assert.IsType<SkinnedModelBasicEffectContent>(Load(w).Root);

        Assert.Equal(1, effect.Technique);
        Assert.Equal(32f, effect.SpecularPower);
        Assert.True(effect.DiffuseMap0Enabled);
        Assert.Equal(new ExternalReference("skin_0"), effect.DiffuseMap0.External);
        Assert.True(effect.NormalMap.IsNull);
    }

    [Fact]
    public void RenderDeferredEffect_ReadsOneOrTwoLayers()
    {
        static void Layer(XnbTestWriter w, string texture)
        {
            w.Bytes(0, 1).Floats(1, 0.5f, 0.25f, 0.1f, 8, 0, 1, 0.3f);
            Ext(w, texture); Ext(w, ""); Ext(w, "");
        }

        var single = new XnbTestWriter().TypeId(Pipeline + "RenderDeferredEffectReader" + PolygonHeadAsm);
        single.Floats(1, 0.5f).Bytes(1, 0);
        Ext(single, "");
        Layer(single, "grass_0");
        single.Bytes(0);
        var dual = new XnbTestWriter().TypeId(Pipeline + "RenderDeferredEffectReader" + PolygonHeadAsm);
        dual.Floats(1, 0.5f).Bytes(0, 0);
        Ext(dual, "sky_cube");
        Layer(dual, "grass_0");
        dual.Bytes(1);
        Layer(dual, "dirt_0");

        var one = Assert.IsType<RenderDeferredEffectContent>(Load(single).Root);
        var two = Assert.IsType<RenderDeferredEffectContent>(Load(dual).Root);

        Assert.True(one.VertexColorEnabled);
        Assert.True(one.Layer0.AlphaMaskEnabled);
        Assert.Equal(new ExternalReference("grass_0"), one.Layer0.DiffuseTexture);
        Assert.Null(one.Layer0.MaterialTexture);
        Assert.Null(one.Layer1);
        Assert.Equal(new ExternalReference("sky_cube"), two.ReflectionMap);
        Assert.Equal(new ExternalReference("dirt_0"), two.Layer1!.DiffuseTexture);
    }

    [Fact]
    public void LiquidLavaAdditiveAndNormalMappedEffects_Read()
    {
        var liquid = new XnbTestWriter().TypeId(Pipeline + "RenderDeferredLiquidEffectReader" + PolygonHeadAsm);
        Ext(liquid, "").Floats(0.3f, 1, 0, 0, 1, 0.8f, 0, 0, 1, 0, 0, 0.5f, 0.1f, 0.2f, 16);
        Ext(liquid, "bottom"); Ext(liquid, "waves");
        liquid.Floats(0.4f, 1, 1, 1, 0.05f, 0.3f, 24);
        Ext(liquid, "ice"); Ext(liquid, "ice_n");

        var lava = new XnbTestWriter().TypeId(Pipeline + "LavaEffectReader" + PolygonHeadAsm);
        lava.Floats(0.1f, 1, 0, 0, 1, 2, 0.5f, 0.3f, 12, 0.7f);
        Ext(lava, "tone"); Ext(lava, "temp"); Ext(lava, "mask");
        lava.Floats(0.2f, 0.1f, 0, 0.4f, 0.3f, 8, 1);    // rock colour, emissive, spec amount/power, normal power
        Ext(lava, "rock"); Ext(lava, "rock_n");

        var additive = new XnbTestWriter().TypeId(Pipeline + "AdditiveEffectReader" + PolygonHeadAsm);
        additive.Floats(1, 0.5f, 0).Bytes(0, 1);
        Ext(additive, "glow");

        var normalMapped = new XnbTestWriter().TypeId(Pipeline + "SkinnedModelDeferredNormalMappedEffectReader" + PolygonHeadAsm);
        normalMapped.Floats(1, 1, 1, 0.5f, 16, 0, 1);
        foreach (var t in new[] { "d", "m", "", "n", "" })
        {
            Ext(normalMapped, t);
        }

        var water = Assert.IsType<RenderDeferredLiquidEffectContent>(Load(liquid).Root);
        Assert.Null(water.ReflectionMap);
        Assert.Equal(new Vector2(1, 0), water.WaveSpeed0);
        Assert.Equal(new ExternalReference("waves"), water.WaterNormalMap);
        Assert.Equal(new ExternalReference("ice_n"), water.IceNormalMap);
        Assert.Equal(24f, water.IceSpecPower);

        var hot = Assert.IsType<LavaEffectContent>(Load(lava).Root);
        Assert.Equal(0.7f, hot.TempFrequency);
        Assert.Equal(new ExternalReference("mask"), hot.MaskMap);
        Assert.Equal(new ExternalReference("rock_n"), hot.RockNormalMap);

        var add = Assert.IsType<AdditiveEffectContent>(Load(additive).Root);
        Assert.Equal(new Vector3(1, 0.5f, 0), add.ColorTint);
        Assert.True(add.TextureEnabled);

        var boss = Assert.IsType<SkinnedModelDeferredNormalMappedEffectContent>(Load(normalMapped).Root);
        Assert.Equal(new ExternalReference("n"), boss.NormalMap);
        Assert.Null(boss.DamageMap);
    }

    [Fact]
    public void BiTreeModel_ReadsRootsAndTheNodeTree()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BiTreeModelReader" + PolygonHeadAsm);
        w.Writer.Write(1);                                 // one root
        w.Bytes(1, 0).Floats(0.5f, 0.1f, -1);
        w.Writer.Write(3); w.Writer.Write(12);             // vertices, stride
        w.TypeId(XnaContent + "VertexDeclarationReader" + Xna).UInts(1);
        w.Writer.Write((ushort)0); w.Writer.Write((ushort)0); w.Bytes(2, 0, 0, 0);
        w.TypeId(XnaContent + "VertexBufferReader" + Xna).UInts(36).Bytes(new byte[36]);
        w.TypeId(XnaContent + "IndexBufferReader" + Xna).Bytes(1).UInts(6).Bytes(0, 0, 1, 0, 2, 0);
        w.TypeId(Pipeline + "AdditiveEffectReader" + PolygonHeadAsm).Floats(1, 1, 1).Bytes(0, 0);
        Ext(w, "");
        w.Writer.Write(1); w.Writer.Write(0);              // whole tree: 1 primitive from index 0
        w.Floats(0, 0, 0, 1, 1, 1);
        w.Bytes(1);                                        // child A
        w.Writer.Write(1); w.Writer.Write(0); w.Floats(0, 0, 0, 1, 1, 1).Bytes(0, 0);
        w.Bytes(0);                                        // no child B

        var tree = Assert.IsType<BiTreeModelContent>(Load(w).Root);

        var root = Assert.Single(tree.Roots);
        Assert.Equal((3, 12), (root.NumVertices, root.VertexStride));
        Assert.Equal(-1f, root.GroundLevel);
        Assert.IsType<AdditiveEffectContent>(root.Effect);
        Assert.Equal(new BoundingBox(Vector3.Zero, Vector3.One), root.Tree.Bounds);
        Assert.NotNull(root.Tree.ChildA);
        Assert.Null(root.Tree.ChildA!.ChildA);
        Assert.Null(root.Tree.ChildB);
    }

    [Fact]
    public void BiTreeModel_TooDeepTree_IsInvalid()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BiTreeModelReader" + PolygonHeadAsm);
        w.Writer.Write(1);
        w.Bytes(1, 0).Floats(0, 0, 0);
        w.Writer.Write(0); w.Writer.Write(0);
        w.Null().Null().Null().Null();
        for (var i = 0; i < 10_000; i++)
        {
            w.Writer.Write(0); w.Writer.Write(0); w.Floats(0, 0, 0, 0, 0, 0).Bytes(1);
        }

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void BitmapFont_ReadsUtf8CharactersGlyphsAndKerning()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BitmapFontReader" + PolygonHeadAsm);
        w.Writer.Write(20); w.Writer.Write(16);
        w.Bytes(Encoding.UTF8.GetBytes("?"));
        w.TypeId(XnaContent + "Texture2DReader" + Xna);
        w.Writer.Write(1); w.UInts(1, 1, 1, 4).Bytes(0, 0, 0, 0);
        w.Writer.Write(2);
        w.Bytes(Encoding.UTF8.GetBytes("Ж"));              // two-byte character
        foreach (var v in new[] { 1, 2, 10, 12, 11, 0 })
        {
            w.Writer.Write(v);
        }
        w.Bytes(0);
        w.Bytes(Encoding.UTF8.GetBytes("あ"));             // three-byte character
        foreach (var v in new[] { 0, 0, 12, 12, 12, 1 })
        {
            w.Writer.Write(v);
        }
        w.Bytes(1);
        w.Writer.Write(1);
        w.Bytes(Encoding.UTF8.GetBytes("A")).Bytes(Encoding.UTF8.GetBytes("V"));
        w.Writer.Write(-2);

        var font = Assert.IsType<BitmapFontContent>(Load(w).Root);

        Assert.Equal((20, 16), (font.LineHeight, font.BaseLine));
        Assert.Equal(new Rune('?'), font.DefaultCharacter);
        Assert.NotNull(font.Texture.Inline);
        Assert.Equal(new Rune('Ж'), font.Glyphs[0].Character);
        Assert.Equal((1, 2, 10, 12), (font.Glyphs[0].X, font.Glyphs[0].Y, font.Glyphs[0].Width, font.Glyphs[0].Height));
        Assert.Equal(new Rune('あ'), font.Glyphs[1].Character);
        Assert.True(font.Glyphs[1].ForceWhite);
        Assert.Equal(new KerningPair(new Rune('A'), new Rune('V'), -2), Assert.Single(font.Kerning));
    }

    [Fact]
    public void InvalidUtf8Character_IsInvalid()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BitmapFontReader" + PolygonHeadAsm);
        w.Writer.Write(20); w.Writer.Write(16);
        w.Bytes(0xC3, 0x28);                                 // broken two-byte sequence

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void BitmapFont_AtlasCanBeAnExternalReference()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BitmapFontReader" + PolygonHeadAsm);
        w.Writer.Write(10); w.Writer.Write(8);
        w.Bytes((byte)'?');
        Ext(w.TypeId(XnaContent + "ExternalReferenceReader" + Xna), "font_atlas");
        w.Writer.Write(0); w.Writer.Write(0);

        var font = Assert.IsType<BitmapFontContent>(Load(w).Root);

        Assert.Equal(new ExternalReference("font_atlas"), font.Texture.External);
        Assert.Null(font.Texture.Inline);
    }

    [Fact]
    public void BitmapFont_NegativeGlyphCount_IsInvalid()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BitmapFontReader" + PolygonHeadAsm);
        w.Writer.Write(10); w.Writer.Write(8);
        w.Bytes((byte)'?').Null();
        w.Writer.Write(-1);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void Utf8LeadByteAtTheEnd_IsInvalid()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BitmapFontReader" + PolygonHeadAsm);
        w.Writer.Write(10); w.Writer.Write(8);
        w.Bytes(0xE3);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void BiTreeRoot_WithoutEffect_IsAllowed()
    {
        var w = new XnbTestWriter().TypeId(Pipeline + "BiTreeModelReader" + PolygonHeadAsm);
        w.Writer.Write(1);
        w.Bytes(0, 0).Floats(0, 0, 0);
        w.Writer.Write(0); w.Writer.Write(0);
        w.Null().Null().Null().Null();
        w.Writer.Write(0); w.Writer.Write(0); w.Floats(0, 0, 0, 0, 0, 0).Bytes(0, 0);

        var root = Assert.Single(Assert.IsType<BiTreeModelContent>(Load(w).Root).Roots);

        Assert.Null(root.Effect);
        Assert.False(root.Visible);
    }

    [Fact]
    public void ContentRef_Factories_SetExactlyOneSide()
    {
        var external = ContentRef<Texture2DContent>.FromExternal(new ExternalReference("x"));

        Assert.False(external.IsNull);
        Assert.Null(external.Inline);
        Assert.True(default(ContentRef<Texture2DContent>).IsNull);
        Assert.Throws<ArgumentNullException>(() => ContentRef<Texture2DContent>.FromInline(null!));
    }

    [Fact]
    public void Registry_With_RejectsDuplicateKeysUnlessReplacing()
    {
        var reader = new KeyValuePair<string, XnbTypeReaderFunc>("StringReader", r => "replaced " + r.ReadString());

        Assert.Throws<ArgumentException>(() => XnbReaderRegistry.Standard.With([reader]));
        var replaced = XnbReaderRegistry.Standard.With([reader], replace: true);
        Assert.Equal("replaced x", XnbContent.Load(new XnbTestWriter().StringObject("x").Build(), replaced).Root);
    }

    [Fact]
    public void ContentRef_RejectsAnUnexpectedType()
    {
        var w = new XnbTestWriter().TypeId(XnAnim + "SkinnedModelBasicEffectReader" + XnAnimAsm);
        w.Bytes(0).Floats(0, 0, 0, 0, 0, 0, 0).Bytes(0, 0, 0, 0, 0, 0, 0);
        w.StringObject("not a texture").Null().Null().Null().Null().Null();

        Assert.Throws<InvalidDataException>(() => Load(w));
    }
}
