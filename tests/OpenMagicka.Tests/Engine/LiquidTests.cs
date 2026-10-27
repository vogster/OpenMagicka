using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class LiquidTests
{
    private sealed class Recorder : IEffectParameters
    {
        public Dictionary<string, object?> Set { get; } = [];

        public bool Contains(string name) => true;
        public void SetValue(string name, Matrix value) => Set[name] = value;
        public void SetValue(string name, Vector3 value) => Set[name] = value;
        public void SetValue(string name, float value) => Set[name] = value;
        public void SetValue(string name, bool value) => Set[name] = value;
        public void SetValue(string name, Vector2 value) => Set[name] = value;
        public void SetValue(string name, Vector4 value) => Set[name] = value;
        public void SetValue(string name, Texture? value) => Set[name] = value;
    }

    private static ExternalReference? Ref(string? path) => path is null ? null : new ExternalReference(path);

    internal static RenderDeferredLiquidEffectContent Water(string? reflection = null) => new(
        Ref(reflection), 0.2f, new Vector2(0.1f, 0), new Vector2(0, 0.05f), 0.6f, new Vector3(0.1f, 0.2f, 0.3f), new Vector3(0, 0, 0.1f),
        0.05f, 1, 64, Ref("bottom"), Ref("waves"), 0.4f, new Vector3(0.8f, 0.9f, 1), 0.1f, 0.7f, 32, Ref("ice"), null);

    internal static LavaEffectContent Lava() => new(
        0.3f, new Vector2(0.01f, 0), new Vector2(0, 0.02f), 4, 0.5f, 0.2f, 8, 0.1f, Ref("tone"), Ref("temp"), Ref("mask"),
        new Vector3(0.2f, 0.1f, 0.1f), 0.05f, 0.3f, 16, 1, Ref("rock"), null);

    private static VertexElementContent E(int offset, VertexElementFormatXna31 format, VertexElementUsageXna31 usage, int index = 0) =>
        new(0, offset, format, 0, usage, index);

    /// <summary>A quad of water, 2 × 2 at height 1, with the shipped liquid layout (stride 60).</summary>
    private static LiquidContent Pool(object effect, bool autoFreeze)
    {
        var declaration = new VertexDeclarationContent(
        [
            E(0, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Position), E(12, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Normal),
            E(24, VertexElementFormatXna31.Vector2, VertexElementUsageXna31.TextureCoordinate), E(32, VertexElementFormatXna31.Single, VertexElementUsageXna31.Color),
            E(36, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Tangent), E(48, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Binormal),
        ]);
        var corners = new[] { new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1) };
        var vertices = corners.SelectMany(c => new[] { c.X, c.Y, c.Z, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }).SelectMany(BitConverter.GetBytes).ToArray();
        var indices = new ushort[] { 0, 1, 2, 0, 2, 3 }.SelectMany(BitConverter.GetBytes).ToArray();
        return new LiquidContent(effect, new VertexBufferContent(vertices), new IndexBufferContent(true, indices), declaration, 60, 4, 2, true, true, autoFreeze);
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 1f)]
    public void ALiquid_IsABatch_ItsFreezeValueStartingForItsSecondStream(bool autoFreeze, float freeze)
    {
        var batch = Assert.Single(LevelLiquids.Batches([Pool(Water(), autoFreeze)]));

        Assert.Equal((4, 2, 0), (batch.VertexCount, batch.PrimitiveCount, batch.StartIndex));
        Assert.Equal(new BoundingBox(new Vector3(-1, 1, -1), new Vector3(1, 1, 1)), batch.Bounds);
        Assert.IsType<RenderDeferredLiquidEffectContent>(batch.Material);
        Assert.False(batch.Additive);
        Assert.DoesNotContain(batch.Declaration.Elements, e => e.Usage == VertexElementUsageXna31.Color && e.UsageIndex == 1);   // a second stream (OM-88)
        Assert.Equal((60, 0, freeze), (batch.Stride, batch.Liquid, batch.FreezeStart));
        Assert.Equal(("bottom", new Vector3(0.1f, 0.2f, 0.3f)), (batch.DiffuseTexture?.Path, batch.DiffuseColor));   // for the simple view
    }

    [Fact]
    public void APartsLiquid_MovesWithThePart()
    {
        var batch = Assert.Single(LevelLiquids.Batches([Pool(Water(), false)], Matrix.CreateTranslation(0, 5, 0)));

        Assert.Equal((Matrix.CreateTranslation(0, 5, 0), new BoundingBox(new Vector3(-1, 6, -1), new Vector3(1, 6, 1))), (batch.World, batch.Bounds));
    }

    [Fact]
    public void ALiquidWithoutGeometry_IsLeftOut()
    {
        var empty = Pool(Water(), false) with { VertexBuffer = null };

        Assert.Empty(LevelLiquids.Batches([empty]));
    }

    [Theory]
    [InlineData(false, OpaqueKind.GBuffer, LiquidMaterial.Default)]
    [InlineData(true, OpaqueKind.GBuffer, LiquidMaterial.Reflection)]
    [InlineData(true, OpaqueKind.Depth, LiquidMaterial.Depth)]
    [InlineData(true, OpaqueKind.Shadow, LiquidMaterial.Shadow)]
    public void TheWatersTechnique_FollowsThePassAndTheReflectionMap(bool reflection, OpaqueKind pass, int expected)
    {
        Assert.Equal(expected, LiquidMaterial.Technique(Water(), reflection, pass));
    }

    [Theory]
    [InlineData(OpaqueKind.GBuffer, LiquidMaterial.LavaDefault)]
    [InlineData(OpaqueKind.Depth, LiquidMaterial.LavaDepth)]
    [InlineData(OpaqueKind.Shadow, LiquidMaterial.LavaShadow)]
    public void TheLavasTechnique_FollowsThePass(OpaqueKind pass, int expected)
    {
        Assert.Equal(expected, LiquidMaterial.Technique(Lava(), false, pass));
    }

    [Fact]
    public void ALiquidPaddedForItsEffect_GetsNoFreezeValueInItsOwnVertices()
    {
        var batch = Assert.Single(LevelLiquids.Batches([Pool(Water(), false)]));

        var padded = VertexPadding.Complete(batch, EffectInputs.For(batch));

        Assert.DoesNotContain(padded.Declaration.Elements, e => e.Usage == VertexElementUsageXna31.Color && e.UsageIndex == 1);   // its second stream brings it
    }

    [Fact]
    public void ALiquidIndexingPastItsVertices_IsRejected()
    {
        var broken = Pool(Water(), false) with { IndexBuffer = new IndexBufferContent(true, new ushort[] { 0, 1, 9, 0, 2, 3 }.SelectMany(BitConverter.GetBytes).ToArray()) };

        Assert.Throws<InvalidDataException>(() => LevelLiquids.Batches([broken]));
    }

    [Fact]
    public void ApplyingWater_SetsItsMaterial_TheTimeAndTheTextureFlags()
    {
        var parameters = new Recorder();

        LiquidMaterial.Apply(parameters, Water(), Matrix.Identity, 12.5f, _ => null);

        Assert.Equal((0.2f, new Vector2(0.1f, 0), 0.6f, 64f, 12.5f), (parameters.Set["WaveHeight"], parameters.Set["WaveSpeed0"],
            parameters.Set["WaterReflectiveness"], parameters.Set["WaterSpecPower"], parameters.Set["Time"]));
        Assert.Equal(Matrix.Identity, parameters.Set["World"]);
        Assert.Equal((false, false, false), (parameters.Set["BottomTextureEnabled"], parameters.Set["ReflectionMapEnabled"], parameters.Set["IceNormalMapEnabled"]));
    }

    [Fact]
    public void ApplyingLava_SetsItsMaterial_AndTheRockNormalMapFlag()
    {
        var parameters = new Recorder();

        LiquidMaterial.Apply(parameters, Lava(), Matrix.CreateTranslation(1, 2, 3), 3, _ => null);

        Assert.Equal((0.3f, 4f, 0.1f, 3f, false), (parameters.Set["MaskDistortion"], parameters.Set["LavaHotEmissiveAmount"],
            parameters.Set["TempFrequency"], parameters.Set["Time"], parameters.Set["RockNormalMapEnabled"]));
        Assert.Equal(Matrix.CreateTranslation(1, 2, 3), parameters.Set["World"]);
        Assert.Equal(LiquidMaterial.LavaShadow, LiquidMaterial.Technique(Lava(), false, OpaqueKind.Shadow));
    }

    [Fact]
    public void TheLiquidsTextures_AreAllTheirMaps()
    {
        Assert.Equal(["bottom", "ice", "sky", "waves"], LiquidMaterial.References(Water("sky")).Select(r => r.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["mask", "rock", "temp", "tone"], LiquidMaterial.References(Lava()).Select(r => r.Path).Order(StringComparer.Ordinal));
    }
}
