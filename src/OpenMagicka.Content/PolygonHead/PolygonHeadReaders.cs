using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.PolygonHead;

/// <summary>Readers of Arrowhead's PolygonHead engine. Layouts: docs/formats/XNB_READERS.md §3.</summary>
internal static class PolygonHeadReaders
{
    /// <summary>
    /// Limit for the BiTree recursion. Generous because BSP-style trees can be lopsided; to be
    /// tightened once the depth of the real level trees is measured (LevelModel, OM-13).
    /// </summary>
    private const int MaxTreeDepth = 256;

    // i32 primitive count, i32 start index, bounding box, two child flags
    private const int TreeNodeSize = 4 + 4 + 24 + 2;

    public static IReadOnlyDictionary<string, XnbTypeReaderFunc> All { get; } = new Dictionary<string, XnbTypeReaderFunc>
    {
        ["RenderDeferredEffectReader"] = ReadRenderDeferred,
        ["RenderDeferredLiquidEffectReader"] = ReadLiquid,
        ["LavaEffectReader"] = ReadLava,
        ["AdditiveEffectReader"] = ReadAdditive,
        ["SkinnedModelDeferredNormalMappedEffectReader"] = ReadSkinnedNormalMapped,
        ["BiTreeModelReader"] = ReadBiTree,
        ["BitmapFontReader"] = ReadBitmapFont,
    };

    private static RenderDeferredEffectContent ReadRenderDeferred(XnbContentReader r)
    {
        var alpha = r.ReadSingle();
        var sharpness = r.ReadSingle();
        var vertexColor = r.ReadBoolean();
        var materialReflectiveness = r.ReadBoolean();
        var reflectionMap = r.ReadExternalReference();
        var layer0 = ReadLayer(r);
        var layer1 = r.ReadBoolean() ? ReadLayer(r) : null;
        return new RenderDeferredEffectContent(alpha, sharpness, vertexColor, materialReflectiveness, reflectionMap, layer0, layer1);
    }

    private static RenderDeferredLayer ReadLayer(XnbContentReader r) => new(
        DiffuseTextureAlphaDisabled: r.ReadBoolean(),
        AlphaMaskEnabled: r.ReadBoolean(),
        DiffuseColor: r.ReadVector3(),
        SpecAmount: r.ReadSingle(),
        SpecPower: r.ReadSingle(),
        EmissiveAmount: r.ReadSingle(),
        NormalPower: r.ReadSingle(),
        Reflectiveness: r.ReadSingle(),
        DiffuseTexture: r.ReadExternalReference(),
        MaterialTexture: r.ReadExternalReference(),
        NormalTexture: r.ReadExternalReference());

    private static RenderDeferredLiquidEffectContent ReadLiquid(XnbContentReader r) => new(
        ReflectionMap: r.ReadExternalReference(),
        WaveHeight: r.ReadSingle(),
        WaveSpeed0: r.ReadVector2(),
        WaveSpeed1: r.ReadVector2(),
        WaterReflectiveness: r.ReadSingle(),
        BottomColor: r.ReadVector3(),
        DeepBottomColor: r.ReadVector3(),
        WaterEmissiveAmount: r.ReadSingle(),
        WaterSpecAmount: r.ReadSingle(),
        WaterSpecPower: r.ReadSingle(),
        BottomTexture: r.ReadExternalReference(),
        WaterNormalMap: r.ReadExternalReference(),
        IceReflectiveness: r.ReadSingle(),
        IceColor: r.ReadVector3(),
        IceEmissiveAmount: r.ReadSingle(),
        IceSpecAmount: r.ReadSingle(),
        IceSpecPower: r.ReadSingle(),
        IceDiffuseMap: r.ReadExternalReference(),
        IceNormalMap: r.ReadExternalReference());

    private static LavaEffectContent ReadLava(XnbContentReader r) => new(
        MaskDistortion: r.ReadSingle(),
        Speed0: r.ReadVector2(),
        Speed1: r.ReadVector2(),
        LavaHotEmissiveAmount: r.ReadSingle(),
        LavaColdEmissiveAmount: r.ReadSingle(),
        LavaSpecAmount: r.ReadSingle(),
        LavaSpecPower: r.ReadSingle(),
        TempFrequency: r.ReadSingle(),
        ToneMap: r.ReadExternalReference(),
        TempMap: r.ReadExternalReference(),
        MaskMap: r.ReadExternalReference(),
        RockColor: r.ReadVector3(),
        RockEmissiveAmount: r.ReadSingle(),
        RockSpecAmount: r.ReadSingle(),
        RockSpecPower: r.ReadSingle(),
        RockNormalPower: r.ReadSingle(),
        RockTexture: r.ReadExternalReference(),
        RockNormalMap: r.ReadExternalReference());

    private static AdditiveEffectContent ReadAdditive(XnbContentReader r) => new(
        ColorTint: r.ReadVector3(),
        VertexColorEnabled: r.ReadBoolean(),
        TextureEnabled: r.ReadBoolean(),
        Texture: r.ReadExternalReference());

    private static SkinnedModelDeferredNormalMappedEffectContent ReadSkinnedNormalMapped(XnbContentReader r) => new(
        DiffuseColor: r.ReadVector3(),
        SpecularAmount: r.ReadSingle(),
        SpecularPower: r.ReadSingle(),
        EmissiveAmount: r.ReadSingle(),
        NormalPower: r.ReadSingle(),
        DiffuseMap: r.ReadExternalReference(),
        MaterialMap: r.ReadExternalReference(),
        DamageMap: r.ReadExternalReference(),
        NormalMap: r.ReadExternalReference(),
        NormalDamageMap: r.ReadExternalReference());

    private static BiTreeModelContent ReadBiTree(XnbContentReader r)
    {
        var roots = new BiTreeRootContent[r.ReadCount(2 + 12 + 8 + 4 + TreeNodeSize)];
        for (var i = 0; i < roots.Length; i++)
        {
            var visible = r.ReadBoolean();
            var castShadows = r.ReadBoolean();
            var sway = r.ReadSingle();
            var entityInfluence = r.ReadSingle();
            var groundLevel = r.ReadSingle();
            var numVertices = ReadNonNegative(r, "vertex count");
            var stride = ReadNonNegative(r, "vertex stride");
            var declaration = r.ReadObject<VertexDeclarationContent>();
            var vertexBuffer = r.ReadObject<VertexBufferContent>();
            var indexBuffer = r.ReadObject<IndexBufferContent>();
            var effect = r.ReadObject();
            var tree = ReadTreeNode(r, depth: 0);
            roots[i] = new BiTreeRootContent(visible, castShadows, sway, entityInfluence, groundLevel,
                numVertices, stride, declaration, vertexBuffer, indexBuffer, effect, tree);
        }
        return new BiTreeModelContent(roots);
    }

    /// <summary>Depth-first: child A completely, then child B.</summary>
    private static BiTreeNodeContent ReadTreeNode(XnbContentReader r, int depth)
    {
        if (depth >= MaxTreeDepth)
        {
            throw new InvalidDataException($"BiTree deeper than {MaxTreeDepth}");
        }
        var primitives = ReadNonNegative(r, "primitive count");
        var startIndex = ReadNonNegative(r, "start index");
        var bounds = r.ReadBoundingBox();
        var childA = r.ReadBoolean() ? ReadTreeNode(r, depth + 1) : null;
        var childB = r.ReadBoolean() ? ReadTreeNode(r, depth + 1) : null;
        return new BiTreeNodeContent(primitives, startIndex, bounds, childA, childB);
    }

    private static BitmapFontContent ReadBitmapFont(XnbContentReader r)
    {
        var lineHeight = r.ReadInt32();
        var baseLine = r.ReadInt32();
        var defaultCharacter = r.ReadUtf8Char();
        var texture = r.ReadContentRef<Texture2DContent>();

        // character, six i32, one bool
        var glyphs = new GlyphContent[r.ReadCount(1 + 24 + 1)];
        for (var i = 0; i < glyphs.Length; i++)
        {
            glyphs[i] = new GlyphContent(
                Character: r.ReadUtf8Char(),
                X: r.ReadInt32(),
                Y: r.ReadInt32(),
                Width: r.ReadInt32(),
                Height: r.ReadInt32(),
                AdvanceWidth: r.ReadInt32(),
                LeftSideBearing: r.ReadInt32(),
                ForceWhite: r.ReadBoolean());
        }

        var kerning = new KerningPair[r.ReadCount(1 + 1 + 4)];
        for (var i = 0; i < kerning.Length; i++)
        {
            kerning[i] = new KerningPair(r.ReadUtf8Char(), r.ReadUtf8Char(), r.ReadInt32());
        }
        return new BitmapFontContent(lineHeight, baseLine, defaultCharacter, texture, glyphs, kerning);
    }

    private static int ReadNonNegative(XnbContentReader r, string what)
    {
        var value = r.ReadInt32();
        return value >= 0 ? value : throw new InvalidDataException($"negative BiTree {what} {value}");
    }
}
