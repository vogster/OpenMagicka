using Microsoft.Xna.Framework;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class VertexPaddingTests
{
    private static VertexElementContent E(int offset, VertexElementFormatXna31 format, VertexElementUsageXna31 usage, int index = 0) =>
        new(0, offset, format, 0, usage, index);

    /// <summary>Two vertices of a light shaft: position, normal and UV, no colour (stride 32).</summary>
    private static LevelBatch Shaft()
    {
        var declaration = new VertexDeclarationContent(
        [
            E(0, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Position),
            E(12, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Normal),
            E(24, VertexElementFormatXna31.Vector2, VertexElementUsageXna31.TextureCoordinate),
        ]);
        var data = new float[] { 1, 2, 3, 0, 0, 1, 0.5f, 0.25f, 4, 5, 6, 0, 0, 1, 1, 1 }.SelectMany(BitConverter.GetBytes).ToArray();
        return new LevelBatch(data, 32, 2, declaration, [0, 0, 1, 0, 0, 0], true, 0, 1, new BoundingBox(), null, Vector3.One, true, true, true)
        {
            Material = new AdditiveEffectContent(Vector3.One, false, false, null),
        };
    }

    [Fact]
    public void MissingInputs_AreAppendedToEveryVertex_WithDefaults()
    {
        var padded = VertexPadding.Complete(Shaft(),
            [new ShaderInput(VertexElementUsageXna31.Position, 0), new ShaderInput(VertexElementUsageXna31.Color, 0),
                new ShaderInput(VertexElementUsageXna31.Tangent, 1)]);

        Assert.Equal(32 + 4 + 12, padded.Stride);
        Assert.Equal(2 * padded.Stride, padded.VertexData.Length);
        var colour = padded.Declaration.Elements.Single(e => e.Usage == VertexElementUsageXna31.Color);
        var tangent = padded.Declaration.Elements.Single(e => e.Usage == VertexElementUsageXna31.Tangent);
        Assert.Equal((32, VertexElementFormatXna31.Color, 36, VertexElementFormatXna31.Vector3, 1), (colour.Offset, colour.Format, tangent.Offset, tangent.Format, tangent.UsageIndex));
        for (var v = 0; v < 2; v++)
        {
            var vertex = padded.VertexData.AsSpan(v * padded.Stride, padded.Stride);
            Assert.True(vertex[..32].SequenceEqual(Shaft().VertexData.AsSpan(v * 32, 32)), "the original data is kept");
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, vertex[32..36].ToArray());   // white
            Assert.Equal((1f, 0f, 0f), (BitConverter.ToSingle(vertex[36..]), BitConverter.ToSingle(vertex[40..]), BitConverter.ToSingle(vertex[44..])));
        }
        Assert.Equal(Shaft().IndexData, padded.IndexData);
    }

    [Fact]
    public void TrailingBytesOfAVertex_AreKept_AndTheNewElementsGoAfterThem()
    {
        var shaft = Shaft();
        var wide = shaft with
        {
            Stride = 16,
            VertexCount = 2,
            Declaration = new VertexDeclarationContent([E(0, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Position)]),
            VertexData = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
        };

        var padded = VertexPadding.Complete(wide, [new ShaderInput(VertexElementUsageXna31.Color, 0)]);

        Assert.Equal((20, 16), (padded.Stride, padded.Declaration.Elements[^1].Offset));
        Assert.Equal(Enumerable.Range(16, 16).Select(i => (byte)i), padded.VertexData.Skip(20).Take(16));
    }

    [Fact]
    public void AStrideShorterThanItsDeclaration_IsRejected()
    {
        var broken = Shaft() with { Stride = 20 };

        Assert.Throws<InvalidDataException>(() => VertexPadding.Complete(broken, [new ShaderInput(VertexElementUsageXna31.Color, 0)]));
    }

    [Fact]
    public void ABatchWithEverything_IsLeftAsItIs()
    {
        var shaft = Shaft();

        var padded = VertexPadding.Complete(shaft, [new ShaderInput(VertexElementUsageXna31.Position, 0), new ShaderInput(VertexElementUsageXna31.TextureCoordinate, 0)]);

        Assert.Same(shaft, padded);
    }

    [Fact]
    public void EachMaterial_IsDrawnWithItsEffectsInputs()
    {
        var none = default(ContentRef<Texture2DContent>);
        var skinned = new SkinnedModelBasicEffectContent(0, 0, Vector3.One, 1, 16, 1, false, false, false, false, false, false, false, none, none, none, none, none, none);

        Assert.Equal(EffectInputs.Additive, EffectInputs.For(Shaft()));
        Assert.Equal(EffectInputs.SkinnedModelDeferred, EffectInputs.For(Shaft() with { Additive = false, Material = skinned }));
        Assert.Equal(EffectInputs.RenderDeferred, EffectInputs.For(Shaft() with { Additive = false, Material = null }));
    }
}

public class EffectInputsGameTests
{
    [OpenMagicka.Tests.Content.GameContentTheory]
    [InlineData("RenderDeferredEffectWOVT")]
    [InlineData("AdditiveEffect")]
    [InlineData("SkinnedModelDeferredEffect")]
    [InlineData("RenderDeferredLiquidEffect")]
    [InlineData("LavaEffect")]
    public void TheListedInputs_AreWhatTheGamesVertexShadersRead(string effect)
    {
        var source = new ShaderSource(new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(OpenMagicka.Tests.Content.GameContent.ContentDirectory!),
            OpenMagicka.Content.Xnb.XnbReaderRegistry.Magicka));
        var read = Fx2Effect.Parse(source.Bytecode(effect)).Resources
            .Where(r => ShaderBytecode.IsVertexShader(r.Code))
            .SelectMany(r => ShaderBytecode.VertexInputs(r.Code))
            .ToHashSet();
        var listed = effect switch
        {
            "AdditiveEffect" => EffectInputs.Additive,
            "SkinnedModelDeferredEffect" => EffectInputs.SkinnedModelDeferred,
            "RenderDeferredLiquidEffect" or "LavaEffect" => EffectInputs.Liquid,
            _ => EffectInputs.RenderDeferred,
        };

        Assert.True(read.SetEquals(listed), string.Join(", ", read));
    }
}

public class LiquidParametersGameTests
{
    private sealed class Names : IEffectParameters
    {
        public HashSet<string> Set { get; } = [];

        public bool Contains(string name) => true;
        public void SetValue(string name, Microsoft.Xna.Framework.Matrix value) => Set.Add(name);
        public void SetValue(string name, Vector3 value) => Set.Add(name);
        public void SetValue(string name, float value) => Set.Add(name);
        public void SetValue(string name, bool value) => Set.Add(name);
        public void SetValue(string name, Vector2 value) => Set.Add(name);
        public void SetValue(string name, Vector4 value) => Set.Add(name);
        public void SetValue(string name, Microsoft.Xna.Framework.Graphics.Texture? value) => Set.Add(name);
    }

    [OpenMagicka.Tests.Content.GameContentTheory]
    [InlineData("RenderDeferredLiquidEffect")]
    [InlineData("LavaEffect")]
    public void EveryParameterTheLiquidsSet_IsInTheGamesEffect(string effect)
    {
        var source = new ShaderSource(new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(OpenMagicka.Tests.Content.GameContent.ContentDirectory!),
            OpenMagicka.Content.Xnb.XnbReaderRegistry.Magicka));
        var declared = Fx2Effect.Parse(source.Bytecode(effect)).Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var names = new Names();

        LiquidMaterial.Apply(names, effect == "LavaEffect" ? LiquidTests.Lava() : LiquidTests.Water(), Microsoft.Xna.Framework.Matrix.Identity, 0, _ => null);

        Assert.DoesNotContain(names.Set, n => !declared.Contains(n));
    }
}
