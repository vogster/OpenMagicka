using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The passes that draw the opaque geometry.</summary>
public enum OpaqueKind { Shadow, Depth, GBuffer }

/// <summary>
/// A level's water and lava as batches (no GPU work). As the original's, each liquid gets a second vertex stream with
/// one "freeze" value per vertex (Color1: 0 water, 1 ice), which the game's freezing changes each frame (OM-88); it
/// starts at ice when the liquid freezes by itself (AutoFreeze), water otherwise. Each batch carries its liquid's key.
/// </summary>
public static class LevelLiquids
{
    /// <summary>
    /// A liquid's key, the same for the game and the render: the level's own by their place in its list; a part's by
    /// (the part's place in <see cref="LevelParts.Order"/> + 1) × 1000 + their place in the part's list.
    /// </summary>
    public static int Key(int part, int index) => part < 0 ? index : (part + 1) * 1000 + index;

    public static IReadOnlyList<LevelBatch> Batches(IEnumerable<LiquidContent> liquids)
    {
        ArgumentNullException.ThrowIfNull(liquids);
        var batches = new List<LevelBatch>();
        var index = -1;
        foreach (var liquid in liquids)
        {
            index++;
            if (liquid.VertexBuffer is null || liquid.IndexBuffer is null || liquid.Declaration is null || liquid.NumVertices <= 0
                || liquid.PrimitiveCount <= 0 || liquid.Effect is not (RenderDeferredLiquidEffectContent or LavaEffectContent))
            {
                continue;
            }
            Check(liquid);
            var (texture, color) = liquid.Effect switch
            {
                RenderDeferredLiquidEffectContent water => (water.BottomTexture, water.BottomColor),
                LavaEffectContent lava => (lava.RockTexture, lava.RockColor),
                _ => (null, Vector3.One),
            };
            var basic = VertexLayouts.Basic(liquid.Declaration);
            var batch = new LevelBatch(liquid.VertexBuffer.Data, liquid.VertexStride, liquid.NumVertices, liquid.Declaration,
                liquid.IndexBuffer.Data, liquid.IndexBuffer.Is16Bit, 0, liquid.PrimitiveCount, Bounds(liquid), texture, color, false,
                basic.Any(e => e.VertexElementUsage == VertexElementUsage.Normal),
                basic.Any(e => e.VertexElementUsage == VertexElementUsage.TextureCoordinate))
            {
                Material = liquid.Effect,
                Liquid = liquid.Declaration.Elements.Any(e => e.Usage == VertexElementUsageXna31.Color && e.UsageIndex == 1) ? -1 : index,   // one with its own freeze value keeps it
                FreezeStart = liquid.AutoFreeze ? 1 : 0,
            };
            batches.Add(batch);
        }
        return batches;
    }

    /// <summary>A part's liquids, placed with the part (the original moves them with it).</summary>
    public static IReadOnlyList<LevelBatch> Batches(IEnumerable<LiquidContent> liquids, Matrix world) =>
        [.. Batches(liquids).Select(b => b with { World = world, Bounds = Transform(b.Bounds, world) })];

    private static BoundingBox Transform(BoundingBox box, Matrix world) =>
        BoundingBox.CreateFromPoints(box.GetCorners().Select(c => Vector3.Transform(c, world)));

    /// <summary>The second stream's layout: the freeze value, one float a vertex, read as Color1.</summary>
    public static readonly VertexDeclaration FreezeStream = new(new VertexElement(0, VertexElementFormat.Single, VertexElementUsage.Color, 1));

    private static BoundingBox Bounds(LiquidContent liquid)
    {
        var position = liquid.Declaration!.Elements.FirstOrDefault(e => e.Usage == VertexElementUsageXna31.Position && e.Format == VertexElementFormatXna31.Vector3)
            ?? throw new InvalidDataException("a liquid has no position");
        var data = liquid.VertexBuffer!.Data;
        var points = Enumerable.Range(0, liquid.NumVertices).Select(v =>
        {
            var at = v * liquid.VertexStride + position.Offset;
            return new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
        });
        return BoundingBox.CreateFromPoints(points);
    }

    private static void Check(LiquidContent liquid)
    {
        var indexSize = liquid.IndexBuffer!.Is16Bit ? 2 : 4;
        if (liquid.PrimitiveCount * 3L * indexSize > liquid.IndexBuffer.Data.Length)
        {
            throw new InvalidDataException($"a liquid draws {liquid.PrimitiveCount} triangles from {liquid.IndexBuffer.Data.Length / indexSize} indices");
        }
        if ((long)liquid.NumVertices * liquid.VertexStride > liquid.VertexBuffer!.Data.Length || liquid.VertexStride < liquid.Declaration!.Stride)
        {
            throw new InvalidDataException($"a liquid has {liquid.NumVertices} vertices of {liquid.VertexStride} bytes in {liquid.VertexBuffer.Data.Length} bytes");
        }
        ModelMeshes.CheckIndices(liquid.IndexBuffer.Data[..(liquid.PrimitiveCount * 3 * indexSize)], liquid.IndexBuffer.Is16Bit, liquid.NumVertices, "a liquid");
    }
}

/// <summary>
/// How water (<c>RenderDeferredLiquidEffect</c>) and lava (<c>LavaEffect</c>) materials feed the
/// original effects, as the game sets them: the material's values, the liquid's own time (its
/// waves and flow; not the shared Time), and a texture flag for each optional map.
/// </summary>
public static class LiquidMaterial
{
    public const int Default = 0;
    public const int Reflection = 1;
    public const int Depth = 2;
    public const int Shadow = 3;
    public const int LavaDefault = 0;
    public const int LavaDepth = 1;
    public const int LavaShadow = 2;

    /// <summary>The technique for a pass: water reflects when it has a reflection map that was loaded.</summary>
    public static int Technique(object material, bool hasReflectionMap, OpaqueKind pass) => (material, pass) switch
    {
        (LavaEffectContent, OpaqueKind.Shadow) => LavaShadow,
        (LavaEffectContent, OpaqueKind.Depth) => LavaDepth,
        (LavaEffectContent, _) => LavaDefault,
        (_, OpaqueKind.Shadow) => Shadow,
        (_, OpaqueKind.Depth) => Depth,
        _ => hasReflectionMap ? Reflection : Default,
    };

    public static IEnumerable<ExternalReference> References(object material) => (material switch
    {
        RenderDeferredLiquidEffectContent w => new[] { w.ReflectionMap, w.BottomTexture, w.WaterNormalMap, w.IceDiffuseMap, w.IceNormalMap },
        LavaEffectContent l => [l.ToneMap, l.TempMap, l.MaskMap, l.RockTexture, l.RockNormalMap],
        _ => [],
    }).OfType<ExternalReference>();

    public static void Apply(IEffectParameters effect, object material, Matrix world, float time, Func<ExternalReference?, Texture?> textures)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(textures);
        effect.SetValue("World", world);
        effect.SetValue("Time", time);
        switch (material)
        {
            case RenderDeferredLiquidEffectContent water:
                ApplyWater(effect, water, textures);
                break;
            case LavaEffectContent lava:
                ApplyLava(effect, lava, textures);
                break;
            default:
                throw new ArgumentException($"not a liquid: {material?.GetType().Name ?? "null"}", nameof(material));
        }
    }

    private static void ApplyWater(IEffectParameters effect, RenderDeferredLiquidEffectContent water, Func<ExternalReference?, Texture?> textures)
    {
        effect.SetValue("WaveHeight", water.WaveHeight);
        effect.SetValue("WaveSpeed0", water.WaveSpeed0);
        effect.SetValue("WaveSpeed1", water.WaveSpeed1);
        effect.SetValue("WaterReflectiveness", water.WaterReflectiveness);
        effect.SetValue("BottomColor", water.BottomColor);
        effect.SetValue("DeepBottomColor", water.DeepBottomColor);
        effect.SetValue("WaterEmissiveAmount", water.WaterEmissiveAmount);
        effect.SetValue("WaterSpecAmount", water.WaterSpecAmount);
        effect.SetValue("WaterSpecPower", water.WaterSpecPower);
        effect.SetValue("IceReflectiveness", water.IceReflectiveness);
        effect.SetValue("IceColor", water.IceColor);
        // IceEmissiveAmount is read but not used: the original's setter does nothing and the effect has no such parameter.
        effect.SetValue("IceSpecAmount", water.IceSpecAmount);
        effect.SetValue("IceSpecPower", water.IceSpecPower);
        Map(effect, "ReflectionMap", "ReflectionMapEnabled", textures(water.ReflectionMap));
        Map(effect, "BottomTexture", "BottomTextureEnabled", textures(water.BottomTexture));
        Map(effect, "IceNormalMap", "IceNormalMapEnabled", textures(water.IceNormalMap));
        effect.SetValue("WaterNormalMap", textures(water.WaterNormalMap));
        effect.SetValue("IceDiffuseMap", textures(water.IceDiffuseMap));
    }

    private static void ApplyLava(IEffectParameters effect, LavaEffectContent lava, Func<ExternalReference?, Texture?> textures)
    {
        effect.SetValue("MaskDistortion", lava.MaskDistortion);
        effect.SetValue("Speed0", lava.Speed0);
        effect.SetValue("Speed1", lava.Speed1);
        effect.SetValue("LavaHotEmissiveAmount", lava.LavaHotEmissiveAmount);
        effect.SetValue("LavaColdEmissiveAmount", lava.LavaColdEmissiveAmount);
        effect.SetValue("LavaSpecAmount", lava.LavaSpecAmount);
        effect.SetValue("LavaSpecPower", lava.LavaSpecPower);
        effect.SetValue("TempFrequency", lava.TempFrequency);
        effect.SetValue("RockColor", lava.RockColor);
        effect.SetValue("RockSpecAmount", lava.RockSpecAmount);
        effect.SetValue("RockSpecPower", lava.RockSpecPower);
        effect.SetValue("RockEmissiveAmount", lava.RockEmissiveAmount);
        effect.SetValue("RockNormalPower", lava.RockNormalPower);
        effect.SetValue("ToneMap", textures(lava.ToneMap));
        effect.SetValue("TempMap", textures(lava.TempMap));
        effect.SetValue("MaskMap", textures(lava.MaskMap));
        effect.SetValue("RockTexture", textures(lava.RockTexture));
        Map(effect, "RockNormalMap", "RockNormalMapEnabled", textures(lava.RockNormalMap));
    }

    private static void Map(IEffectParameters effect, string name, string flag, Texture? texture)
    {
        effect.SetValue(name, texture);
        effect.SetValue(flag, texture is not null);
    }
}
