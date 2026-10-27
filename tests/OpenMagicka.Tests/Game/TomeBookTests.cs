using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Menu;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Game;

/// <summary>The Tome's 3D book (OM-83): its candle, the mouse onto its page, its camera and its effect from the game.</summary>
public class TomeBookTests
{
    [Fact]
    public void TheCandle_LightsUpFromNothing_ItsWobbleEasingDown()
    {
        var candle = new Candle(() => 0);
        candle.Aim(1, 0.2f, 6);
        candle.Step(0.01f);

        Assert.Equal(23.1f, candle.Strength, 3);                              // speed 24 → 23.1, intensity 0 → 0.231 (×100)
        candle.Step(float.NaN);
        Assert.Equal(23.1f, candle.Strength, 3);                              // a step not a number does nothing
    }

    [Fact]
    public void TheCandlesWobble_IsBounded()
    {
        var candle = new Candle(() => 1);
        candle.Aim(0, 1, 6);
        for (var i = 0; i < 1000; i++)
        {
            candle.Step(0.1f);
        }

        Assert.InRange(candle.Strength, 99, 101);                             // intensity 0, wobble held at 1, amount 1
    }

    [Fact]
    public void AStraightDownLook_AtThePagesMiddle_HitsItsPixel_ByTheOriginalsMap()
    {
        var camera = new Vector3(-2.65f, 10, 0);
        var view = Matrix4x4.CreateLookAt(camera, new Vector3(-2.65f, 0, 0), Vector3.UnitZ);
        var lens = Matrix4x4.CreatePerspectiveFieldOfView(0.75f, 1, 2, 200);

        var page = BookPicking.ToPage(view * lens, camera, new Vector2(400, 400), new Vector2(800, 800));

        Assert.NotNull(page);
        Assert.Equal(0.53f * 0.97f * 1024, page.Value.X, 1);                  // (0.03 − x / 5.3) · 0.97 · 1024
        Assert.Equal(512, page.Value.Y, 1);
        Assert.Null(BookPicking.ToPage(view * lens, camera, new Vector2(0, 0), new Vector2(800, 800)));  // off the page
        Assert.Null(BookPicking.ToPage(view * lens, camera, new Vector2(400, 400), Vector2.Zero));
    }

    [Fact]
    public void AMeshWithoutATangentFrame_GetsOneOfNothing_AndOneWithItKeepsIt()
    {
        static VertexElementContent E(int offset, VertexElementFormatXna31 format, VertexElementUsageXna31 usage) => new(0, offset, format, 0, usage, 0);
        var plain = new LevelBatch(new byte[24], 12, 2, new VertexDeclarationContent([E(0, VertexElementFormatXna31.Vector3, VertexElementUsageXna31.Position)]),
            [0, 0, 1, 0, 0, 0], true, 0, 1, new Microsoft.Xna.Framework.BoundingBox(), null, Microsoft.Xna.Framework.Vector3.One, false, false, false);

        var framed = BookPass.Unframed(plain);

        Assert.Equal(36, framed.Stride);
        Assert.All(framed.VertexData, b => Assert.Equal(0, b));
        Assert.Contains(framed.Declaration.Elements, e => e.Usage == VertexElementUsageXna31.Binormal);
        Assert.Same(framed, BookPass.Unframed(framed));
    }

    [Fact]
    public void TheMenusMouse_GoesThroughTheBooksPicking_WhenItIsShown()
    {
        var reader = new MenuInputReader { Picking = (mouse, screen) => new Microsoft.Xna.Framework.Vector2(mouse.X + screen.X, 7) };
        var input = new OpenMagicka.Engine.InputState(default, default, true, new Microsoft.Xna.Framework.Point(100, 50));
        reader.Read(input, 0);

        Assert.Equal(new Microsoft.Xna.Framework.Vector2(100, 7), reader.Read(input, 0).Page);
    }

    [Fact]
    public void TheCandlesCamera_LooksAtTheBooksMiddle_FromTheCandle()
    {
        var candle = new Microsoft.Xna.Framework.Vector3(-1.438f, 8.539f, 3.036f);
        var lens = BookPass.LightViewProjection(candle);
        var aim = Microsoft.Xna.Framework.Vector4.Transform(new Microsoft.Xna.Framework.Vector4(BookPass.ShadowAim, 1), lens);
        var near = Microsoft.Xna.Framework.Vector4.Transform(new Microsoft.Xna.Framework.Vector4(candle + new Microsoft.Xna.Framework.Vector3(0, -1.5f, 0), 1), lens);

        Assert.Equal(0, aim.X / aim.W, 3);
        Assert.Equal(0, aim.Y / aim.W, 3);
        Assert.InRange(aim.Z / aim.W, 0, 1);                                  // within its 1…15
        Assert.InRange(near.Z / near.W, 0, aim.Z / aim.W);
    }

    [Fact]
    public void AnAssembly_WithoutTheEffect_OrNoAssembly_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => XnAnimationEffect.Read(typeof(object).Assembly.Location));
        Assert.Throws<InvalidDataException>(() => XnAnimationEffect.Read(new MemoryStream(new byte[256]), "nothing"));
        Assert.Throws<FileNotFoundException>(() => XnAnimationEffect.Read(Path.Combine(Path.GetTempPath(), "no", "XNAnimation.dll")));
        Assert.Equal(Path.GetFullPath(Path.Combine("game", XnAnimationEffect.Assembly)), XnAnimationEffect.PathIn(Path.Combine("game", "Content") + "/"));
    }

    [GameContentFact]
    public void ThePlayersEffect_IsFound_WithTheForwardTechniquesParameters()
    {
        var effect = XnAnimationEffect.Read(XnAnimationEffect.PathIn(GameContent.ContentDirectory!));
        var parsed = Fx2Effect.Parse(EffectPatcher.Patch(effect).Bytecode);

        Assert.Equal(76320, effect.Length);
        Assert.Empty(BookPass.Parameters.Except(parsed.Parameters.Select(p => p.Name)));
    }

    /// <summary>The right page's corners at the camera's rest on a 16:9 screen, as the spec computed them from the original's pose.</summary>
    [GameContentFact]
    public void TheCamera_ShowsTheRightPage_WhereTheOriginalsDoes()
    {
        var book = Book();
        var (view, lens, _) = book.Camera(16 / 9f);
        Vector2 Shown(Vector3 at)
        {
            var clip = Vector4.Transform(new Vector4(at, 1), view * lens);
            return new Vector2((clip.X / clip.W + 1) / 2, (1 - clip.Y / clip.W) / 2);
        }

        AssertNear(new Vector2(0.689f, 0.067f), Shown(BookPicking.OuterTop));
        AssertNear(new Vector2(0.229f, 0.073f), Shown(BookPicking.SpineTop));
        AssertNear(new Vector2(0.694f, 0.932f), Shown(BookPicking.OuterBottom));
        AssertNear(new Vector2(0.223f, 0.927f), Shown(BookPicking.SpineBottom));
    }

    [GameContentFact]
    public void APagePixel_Shown_IsPickedBack()
    {
        var book = Book();
        var screen = new Microsoft.Xna.Framework.Point(1600, 900);
        var (view, lens, _) = book.Camera(16 / 9f);
        foreach (var (px, py) in new[] { (100f, 100f), (512f, 512f), (900f, 950f) })
        {
            var x = -(px / (0.97f * 1024) - 0.03f) * 5.3f;                   // the map's inverse, on the quad's slope
            var at = new Vector3(x, 0.4f - 0.1f * x / 5.3f, 2.75f - py / 1024 * 5.5f);
            var clip = Vector4.Transform(new Vector4(at, 1), view * lens);
            var mouse = new Microsoft.Xna.Framework.Point((int)MathF.Round((clip.X / clip.W + 1) / 2 * screen.X),
                (int)MathF.Round((1 - clip.Y / clip.W) / 2 * screen.Y));

            var page = book.ToPage(mouse, screen);

            Assert.NotNull(page);
            Assert.InRange(page.Value.X, px - 3, px + 3);                     // a screen pixel is ~1.7 page pixels here
            Assert.InRange(page.Value.Y, py - 3, py + 3);
        }
    }

    [GameContentFact]
    public void TheFrame_HasTheCandleOverTheRightPage_AndTheBooksBones()
    {
        var book = Book();
        var picture = new BookPicture();
        book.Write(picture, 4 / 3f);

        Assert.True(picture.Shown);
        Assert.Equal(new Microsoft.Xna.Framework.Vector3(-1.438f, 8.539f, 3.036f), picture.Light, new XnaVectorNear(0.001f));
        Assert.All(picture.Book.Concat(picture.Left).Concat(picture.Right), m => Assert.True(float.IsFinite(m.Determinant())));
        Assert.NotEqual(picture.Left[1], picture.Right[1]);                   // the stacks lie apart
    }

    private static TomeBook Book()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var book = TomeBook.Load(loader) ?? throw new InvalidOperationException("the book did not load");
        book.Settle();
        return book;
    }

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(actual.X, expected.X - 0.005f, expected.X + 0.005f);
        Assert.InRange(actual.Y, expected.Y - 0.005f, expected.Y + 0.005f);
    }

    private sealed class XnaVectorNear(float tolerance) : IEqualityComparer<Microsoft.Xna.Framework.Vector3>
    {
        public bool Equals(Microsoft.Xna.Framework.Vector3 a, Microsoft.Xna.Framework.Vector3 b) => Microsoft.Xna.Framework.Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Microsoft.Xna.Framework.Vector3 v) => 0;
    }
}
