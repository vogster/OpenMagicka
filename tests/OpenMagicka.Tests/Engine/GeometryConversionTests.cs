using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class GeometryConversionTests
{
    private static VertexElementContent E(int offset, VertexElementFormatXna31 format, VertexElementUsageXna31 usage, int index = 0) =>
        new(0, offset, format, 0, usage, index);

    /// <summary>The most common level layout (5909 of 9518 batches): stride 60, UV1 and Tangent1 alias UV0 and Tangent0.</summary>
    internal static VertexDeclarationContent LevelDeclaration() => new(
    [
        E(0, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Position),
        E(12, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Normal),
        E(24, VertexElementFormatXna31.Vector2, VertexElementUsageXna31.TextureCoordinate),
        E(32, VertexElementFormatXna31.Vector4, VertexElementUsageXna31.Color),
        E(48, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Tangent),
        E(24, VertexElementFormatXna31.Vector2, VertexElementUsageXna31.TextureCoordinate, 1),
        E(48, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Tangent, 1),
    ]);

    [Fact]
    public void FullLayout_MapsEveryElementToFna()
    {
        var elements = VertexLayouts.Full(LevelDeclaration());

        Assert.Equal(7, elements.Length);
        Assert.Equal(new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.Color, 0), elements[3]);
        Assert.Equal(new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1), elements[5]);
    }

    [Fact]
    public void BasicLayout_KeepsPositionNormalAndFirstUv_ForBasicEffect()
    {
        var elements = VertexLayouts.Basic(LevelDeclaration());

        Assert.Equal([VertexElementUsage.Position, VertexElementUsage.Normal, VertexElementUsage.TextureCoordinate], elements.Select(e => e.VertexElementUsage));
        Assert.Equal([0, 12, 24], elements.Select(e => e.Offset));
    }

    [Theory]
    [InlineData(VertexElementFormatXna31.Rg32)]
    [InlineData(VertexElementFormatXna31.UInt101010)]
    public void FormatsFnaLacks_AreInvalid(VertexElementFormatXna31 format)
    {
        Assert.Throws<InvalidDataException>(() => VertexLayouts.Full(new VertexDeclarationContent([E(0, format, VertexElementUsageXna31.Position)])));
    }

    [Fact]
    public void AStreamOtherThanZero_IsInvalid()
    {
        var declaration = new VertexDeclarationContent([new VertexElementContent(1, 0, VertexElementFormatXna31.Vector3, 0, VertexElementUsageXna31.Position, 0)]);

        Assert.Throws<InvalidDataException>(() => VertexLayouts.Full(declaration));
    }

    [Theory]
    [InlineData(SurfaceFormatXna31.Dxt1, SurfaceFormat.Dxt1)]
    [InlineData(SurfaceFormatXna31.Dxt3, SurfaceFormat.Dxt3)]
    [InlineData(SurfaceFormatXna31.Dxt5, SurfaceFormat.Dxt5)]
    [InlineData(SurfaceFormatXna31.Color, SurfaceFormat.Color)]
    public void TextureFormats_MapToFna(SurfaceFormatXna31 format, SurfaceFormat expected)
    {
        Assert.Equal(expected, TextureFormats.ToFna(format));
    }

    [Fact]
    public void ColorPixels_AreSwizzledFromBgraToRgba_DxtDataIsKept()
    {
        byte[] bgra = [1, 2, 3, 4, 10, 20, 30, 40];
        byte[] dxt = [9, 9, 9, 9, 9, 9, 9, 9];

        Assert.Equal([3, 2, 1, 4, 30, 20, 10, 40], TextureFormats.Pixels(SurfaceFormatXna31.Color, bgra));
        Assert.Equal([1, 2, 3, 4, 10, 20, 30, 40], bgra);                        // the content is not changed
        Assert.Same(dxt, TextureFormats.Pixels(SurfaceFormatXna31.Dxt1, dxt));
        Assert.Throws<InvalidDataException>(() => TextureFormats.ToFna(SurfaceFormatXna31.Bgr233));
    }

    private static BiTreeRootContent Root(int vertices, bool visible = true, object? effect = null, int primitives = 2) => new(
        visible, true, 0, 0, 0, vertices, 60, LevelDeclaration(), new VertexBufferContent(new byte[vertices * 60]),
        new IndexBufferContent(true, new byte[primitives * 3 * 2]), effect,
        new BiTreeNodeContent(primitives, 0, new BoundingBox(Vector3.Zero, Vector3.One), null, null));

    private static RenderDeferredLayer Layer(string? texture) =>
        new(false, false, Vector3.One, 0, 0, 0, 0, 0, texture is null ? null : new ExternalReference(texture), null, null);

    [Fact]
    public void LevelBatches_TakeTheVisibleRoots_WithTheirDiffuseTexture()
    {
        var material = new RenderDeferredEffectContent(1, 0, false, false, null, Layer(@"..\Textures\grass"), null);
        var geometry = new BiTreeModelContent(
        [
            Root(4, effect: material),
            Root(3, visible: false),
            Root(3, effect: new AdditiveEffectContent(Vector3.One, false, true, new ExternalReference("glow"))),
            Root(0),
        ]);

        var batches = LevelGeometry.Batches(geometry);

        Assert.Equal(2, batches.Count);
        Assert.Equal((4, 60, 2, @"..\Textures\grass", false), (batches[0].VertexCount, batches[0].Stride, batches[0].PrimitiveCount,
            batches[0].DiffuseTexture?.Path, batches[0].Additive));
        Assert.True(batches[1].Additive);
        Assert.Equal("glow", batches[1].DiffuseTexture?.Path);
        Assert.Equal(new BoundingBox(Vector3.Zero, Vector3.One), batches[0].Bounds);
    }

    [Fact]
    public void Batches_KnowWhetherTheyCanBeLitAndTextured()
    {
        var noNormal = new VertexDeclarationContent(LevelDeclaration().Elements.Where(e => e.Usage != VertexElementUsageXna31.Normal).ToArray());
        var batches = LevelGeometry.Batches(new BiTreeModelContent([Root(4), Root(4) with { Declaration = noNormal }]));

        Assert.Equal([(true, true), (false, true)], batches.Select(b => (b.HasNormal, b.HasUv)));
        Assert.Equal([VertexElementUsage.Position, VertexElementUsage.TextureCoordinate], VertexLayouts.Basic(noNormal).Select(e => e.VertexElementUsage));
    }

    [Fact]
    public void FramingBounds_IgnoreAdditiveGlows_UnlessThereIsNothingElse()
    {
        var glow = Root(3, effect: new AdditiveEffectContent(Vector3.One, false, false, null)) with
        {
            Tree = new BiTreeNodeContent(2, 0, new BoundingBox(new Vector3(500), new Vector3(600)), null, null),
        };

        Assert.Equal(new BoundingBox(Vector3.Zero, Vector3.One), LevelGeometry.FramingBounds(LevelGeometry.Batches(new BiTreeModelContent([Root(4), glow]))));
        Assert.Equal(new BoundingBox(new Vector3(500), new Vector3(600)), LevelGeometry.FramingBounds(LevelGeometry.Batches(new BiTreeModelContent([glow]))));
        Assert.Equal(new BoundingBox(), LevelGeometry.FramingBounds([]));
    }

    [Fact]
    public void Pixels_KeepATrailingPartialPixel()
    {
        Assert.Equal([3, 2, 1, 4, 9], TextureFormats.Pixels(SurfaceFormatXna31.Color, [1, 2, 3, 4, 9]));
    }

    [Fact]
    public void ABatchWithTooFewVertexBytesOrANegativeRange_IsInvalid()
    {
        var shortVertices = Root(4) with { VertexBuffer = new VertexBufferContent(new byte[100]) };
        var negative = Root(4) with { Tree = new BiTreeNodeContent(-2, 0, new BoundingBox(), null, null) };

        Assert.Throws<InvalidDataException>(() => LevelGeometry.Batches(new BiTreeModelContent([shortVertices])));
        Assert.Throws<InvalidDataException>(() => LevelGeometry.Batches(new BiTreeModelContent([negative])));
    }

    [Fact]
    public void ABatchWhoseIndicesDoNotFit_IsInvalid()
    {
        var root = Root(4) with { Tree = new BiTreeNodeContent(10, 0, new BoundingBox(), null, null) };

        Assert.Throws<InvalidDataException>(() => LevelGeometry.Batches(new BiTreeModelContent([root])));
    }
}
