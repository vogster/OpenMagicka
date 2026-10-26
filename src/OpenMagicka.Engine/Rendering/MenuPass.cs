using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Text;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The menu drawn (OM-53, docs/engine/ARCHITECTURE.md §20): the page laid out on the Tome's paper (the right page of
/// <c>UI/ToM/tome_pages_0</c>) in a 1024² target, shown as a square centred on the black screen (the book's 3D desk
/// comes later), then the screen's own pieces over it, straight to the screen (no scene encoding). Render thread.
/// </summary>
public sealed class MenuPass : IDisposable
{
    public static readonly string[] FontNames = ["MenuTitle", "MenuOption", "MenuDefault", "Maiandra14", "Maiandra16", "Maiandra18", "Stonecross50", LanguageFont];

    /// <summary>The font of every language's own name (not a language's: <c>UI/Font/LanguageFont</c>).</summary>
    public const string LanguageFont = "LanguageFont";

    private static readonly (MenuAtlas Atlas, string Asset)[] Atlases =
    [
        (MenuAtlas.Pages, "UI/Menu/pages"), (MenuAtlas.PagesNew, "UI/Spritesheets/pagesNew"), (MenuAtlas.Hud, "UI/HUD/hud"), (MenuAtlas.FadeBox, "UI/Menu/FadeBox"),
        (MenuAtlas.Loading, "UI/Loading/background"),
    ];

    public const string Paper = "UI/ToM/tome_pages_0";

    private readonly GuiText text;
    private readonly Texture2D paper;
    private readonly Dictionary<MenuAtlas, Texture2D> atlases;
    private readonly RenderTarget2D page;
    private readonly List<GuiSprite> batch = [];

    private MenuPass(GuiText text, Texture2D paper, Dictionary<MenuAtlas, Texture2D> atlases, IReadOnlyDictionary<string, GuiFont> fonts, RenderTarget2D page) =>
        (this.text, this.paper, this.atlases, Fonts, this.page) = (text, paper, atlases, fonts, page);

    /// <summary>The menu's fonts by the original's names.</summary>
    public IReadOnlyDictionary<string, GuiFont> Fonts { get; }

    /// <summary>Reads the effect, the atlases and a language's fonts (English's when it lacks one) and builds the pass on the render thread.</summary>
    public static async Task<MenuPass> Load(ShaderLibrary shaders, RenderQueue<GraphicsDevice> gpu, ContentLoader loader, string language)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(loader);
        Texture2DContent Picture(string asset) => loader.LoadUncached(asset).Root as Texture2DContent ?? throw new InvalidDataException($"{asset} is not a picture");
        var paper = Picture(Paper);
        var pictures = Atlases.ToDictionary(a => a.Atlas, a => Picture(a.Asset));
        var fonts = FontNames.ToDictionary(n => n, n =>
        {
            var own = $"Languages/{language}/Font/{n}";
            var asset = n == LanguageFont ? "UI/Font/LanguageFont" : loader.Files.Exists(own + ".xnb") ? own : $"Languages/eng/Font/{n}";
            var font = loader.LoadUncached(asset).Root as BitmapFontContent ?? throw new InvalidDataException($"{n} is not a bitmap font");
            var atlas = font.Texture.Inline ?? (font.Texture.External is { } external ? loader.LoadReference(asset, external).Root as Texture2DContent : null)
                ?? throw new InvalidDataException($"{n} has no atlas");
            return (font, atlas);
        });
        var effect = await shaders.Load("GUIBasicEffect").ConfigureAwait(false); // the library's, shared with the GUI pass: not ours to dispose
        return await gpu.Enqueue(device => Build(device, effect, paper, pictures, fonts)).ConfigureAwait(false);
    }

    private static MenuPass Build(GraphicsDevice device, GameEffect effect, Texture2DContent paper, Dictionary<MenuAtlas, Texture2DContent> pictures,
        Dictionary<string, (BitmapFontContent Font, Texture2DContent Atlas)> fonts)
    {
        var made = new List<IDisposable>();
        try
        {
            var text = new GuiText(effect) { ScaleToHdr = false };
            var paperTexture = GpuLevel.CreateTexture2D(device, paper);
            made.Add(paperTexture);
            var atlases = new Dictionary<MenuAtlas, Texture2D>();
            foreach (var (atlas, picture) in pictures)
            {
                made.Add(atlases[atlas] = GpuLevel.CreateTexture2D(device, picture));
            }
            var white = new Texture2D(device, 1, 1);
            white.SetData([Color.White]);
            made.Add(atlases[MenuAtlas.White] = white);
            var gpuFonts = new Dictionary<string, GuiFont>();
            foreach (var (name, (font, atlas)) in fonts)
            {
                var gpuFont = new GuiFont(new BitmapFont(font, BitmapFont.SpacingOf(name)), GpuLevel.CreateTexture2D(device, atlas));
                made.Add(gpuFont);
                gpuFonts[name] = gpuFont;
            }
            var page = new RenderTarget2D(device, MenuPicture.PageSize, MenuPicture.PageSize, false, SurfaceFormat.Color, DepthFormat.None);
            made.Add(page);
            return new MenuPass(text, paperTexture, atlases, gpuFonts, page);
        }
        catch
        {
            made.ForEach(m => m.Dispose());
            throw;
        }
    }

    /// <summary>The menu's frame: the page (when shown) centred on black, then the screen's pieces, then the cover.</summary>
    public void Draw(GraphicsDevice device, MenuPicture picture, int width, int height, bool clear = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(picture);
        device.BlendState = BlendState.NonPremultiplied;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.LinearClamp;
        if (picture.ShowsPage)
        {
            device.SetRenderTarget(page);
            device.Clear(Color.Transparent);
            text.DrawSprites(device, paper, [new GuiSprite(Vector2.Zero, new Vector2(MenuPicture.PageSize), new Vector2(0.5f, 0), new Vector2(1, 0.5f), Vector4.One)],
                MenuPicture.PageSize, MenuPicture.PageSize);
            Draw(device, picture.PageSprites, picture.PageTexts, MenuPicture.PageSize, MenuPicture.PageSize);
            device.SetRenderTarget(null);
            if (clear)
            {
                device.Clear(Color.Black);
            }
            var at = MenuPicture.PageOnScreen(width, height);
            text.DrawSprites(device, page, [new GuiSprite(new Vector2(at.X, at.Y), new Vector2(at.Right, at.Bottom), Vector2.Zero, Vector2.One, Vector4.One)], width, height);
        }
        else if (clear)
        {
            device.Clear(Color.Black);
        }
        var laid = MenuPicture.Scaled(new Point(width, height), picture.ScreenScale);
        Draw(device, picture.ScreenSprites, picture.ScreenTexts, laid.X, laid.Y);
        if (picture.Cover > 0)
        {
            text.DrawSprites(device, atlases[MenuAtlas.White], [new GuiSprite(Vector2.Zero, new Vector2(width, height), Vector2.Zero, Vector2.One,
                new Vector4(0, 0, 0, Math.Clamp(picture.Cover, 0, 1)))], width, height);
        }
    }

    /// <summary>The sprites (in batches of one atlas and saturation, in order), then the texts.</summary>
    private void Draw(GraphicsDevice device, List<MenuSprite> sprites, List<MenuText> texts, int width, int height)
    {
        for (var i = 0; i < sprites.Count;)
        {
            var (atlas, saturation) = (sprites[i].Atlas, sprites[i].Saturation);
            var texture = atlases[atlas];
            batch.Clear();
            for (; i < sprites.Count && sprites[i].Atlas == atlas && sprites[i].Saturation == saturation; i++)
            {
                var s = sprites[i];
                var (u0, u1) = (s.Source.Left / (float)texture.Width, s.Source.Right / (float)texture.Width);
                batch.Add(new GuiSprite(s.At, s.At + s.Size, new Vector2(s.Mirrored ? u1 : u0, s.Source.Top / (float)texture.Height),
                    new Vector2(s.Mirrored ? u0 : u1, s.Source.Bottom / (float)texture.Height), s.Colour));
            }
            text.DrawSprites(device, texture, batch, width, height, saturation);
        }
        foreach (var line in texts)
        {
            if (!Fonts.TryGetValue(line.Font, out var font) || line.Text.Length == 0)
            {
                continue;
            }
            var shown = line.Wrap > 0 ? font.Metrics.Wrap(line.Text, line.Wrap) : line.Text;
            text.Draw(device, font, shown, line.Colour, line.At, line.Scale, int.MaxValue, width, height, line.Alignment);
        }
    }

    public void Dispose()
    {
        paper.Dispose();
        foreach (var atlas in atlases.Values)
        {
            atlas.Dispose();
        }
        foreach (var font in Fonts.Values)
        {
            font.Dispose();
        }
        page.Dispose();
    }
}
