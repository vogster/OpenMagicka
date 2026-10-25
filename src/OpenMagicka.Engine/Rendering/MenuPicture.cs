using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The menu's atlases (docs/engine/ARCHITECTURE.md §20).</summary>
public enum MenuAtlas : byte
{
    /// <summary><c>UI/Menu/pages</c> (2048 × 1024): plates, icons, ornaments.</summary>
    Pages,

    /// <summary><c>UI/Spritesheets/pagesNew</c>: the main page's newer tiles.</summary>
    PagesNew,

    /// <summary><c>UI/HUD/hud</c>: the loading bar's strips.</summary>
    Hud,

    /// <summary><c>UI/Menu/FadeBox</c>: a soft square (the pause menu's panel and bars).</summary>
    FadeBox,

    /// <summary>A 1 × 1 white texel (plain quads).</summary>
    White,

    /// <summary><c>UI/Loading/background</c> (1024²): the loading screen's backdrop.</summary>
    Loading,
}

/// <summary>A piece of an atlas drawn at a place: its source rectangle, where (top left) and how big, its colour and saturation.</summary>
public readonly record struct MenuSprite(MenuAtlas Atlas, Rectangle Source, Vector2 At, Vector2 Size, Vector4 Colour, float Saturation = 1, bool Mirrored = false);

/// <summary>A text drawn at a place: its font (by the original's name), its anchor and alignment, colour and scale; wrapped when given a width.</summary>
public readonly record struct MenuText(string Font, string Text, Vector2 At, TextAlignment Alignment, Vector4 Colour, float Scale = 1, int Wrap = 0);

/// <summary>
/// What the menu shows this frame (OM-53), filled on the logic thread: the book's right page (in page pixels,
/// 1024 × 1024, drawn on the Tome's paper), then what lies over the screen (message boxes, the pause menu, the
/// loading screen; in screen pixels), and a black cover.
/// </summary>
public sealed class MenuPicture
{
    public const int PageSize = 1024;

    /// <summary>Whether the book's page is shown (the main menu); not over a level.</summary>
    public bool ShowsPage { get; set; }

    public List<MenuSprite> PageSprites { get; } = [];

    public List<MenuText> PageTexts { get; } = [];

    public List<MenuSprite> ScreenSprites { get; } = [];

    public List<MenuText> ScreenTexts { get; } = [];

    /// <summary>How black the screen is over everything (a fade).</summary>
    public float Cover { get; set; }

    public void Clear()
    {
        ShowsPage = false;
        PageSprites.Clear();
        PageTexts.Clear();
        ScreenSprites.Clear();
        ScreenTexts.Clear();
        Cover = 0;
    }

    /// <summary>Where the page lies on a screen: a square as high as the screen (or as wide, when narrower), centred.</summary>
    public static Rectangle PageOnScreen(int width, int height)
    {
        var side = Math.Min(width, height);
        return new Rectangle((width - side) / 2, (height - side) / 2, side, side);
    }

    /// <summary>A screen point in page pixels; none off the page.</summary>
    public static Vector2? ToPage(Point screen, int width, int height)
    {
        var page = PageOnScreen(width, height);
        if (!page.Contains(screen) || page.Width == 0)
        {
            return null;
        }
        return new Vector2(screen.X - page.X, screen.Y - page.Y) * PageSize / page.Width;
    }
}
