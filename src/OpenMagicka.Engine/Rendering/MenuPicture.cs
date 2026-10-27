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

    /// <summary><c>UI/Menu/CampaignMap</c> (2048²): the world map of the cutscenes between chapters (OM-84).</summary>
    CampaignMap,

    /// <summary><c>UI/Menu/MapMask</c>: the torn sheet the map is painted through, over the whole page.</summary>
    MapMask,

    /// <summary><c>UI/Cutscenes/intro_mask</c>: the torn sheet the intro's pictures are painted through.</summary>
    IntroMask,

    /// <summary><c>UI/Cutscenes/intro_s1</c> … <c>intro_s6</c>: the intro's pictures.</summary>
    Intro1, Intro2, Intro3, Intro4, Intro5, Intro6,
}

/// <summary>A piece of an atlas drawn at a place: its source rectangle, where (top left) and how big, its colour and saturation.</summary>
public readonly record struct MenuSprite(MenuAtlas Atlas, Rectangle Source, Vector2 At, Vector2 Size, Vector4 Colour, float Saturation = 1, bool Mirrored = false);

/// <summary>
/// A text drawn at a place: its font (by the original's name), its anchor and alignment, colour and scale; wrapped when
/// given a width; with a shadow, a black copy (0.8 of its alpha) a pixel down and right under it; from the bottom,
/// its anchor is under its last line, the lines rising from it (the subtitles').
/// </summary>
public readonly record struct MenuText(string Font, string Text, Vector2 At, TextAlignment Alignment, Vector4 Colour, float Scale = 1, int Wrap = 0,
    bool Shadow = false, bool FromBottom = false);

/// <summary>
/// A picture painted into the page's paper through a torn sheet, as the original's map and intro (OM-84): the mask
/// (its whole texture) over its area gives how much of the picture shows at each pixel, the paper the rest. A window
/// of texels (x, y, width, height) in between texels, when given, is the picture's source instead of its rectangle.
/// </summary>
public readonly record struct MenuPainting(MenuAtlas Mask, Rectangle Area, MenuSprite Picture, Vector4? Texels = null);

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

    /// <summary>The book the page lies in (OM-83); not shown: the page lies flat on the screen.</summary>
    public BookPicture Book { get; } = new();

    /// <summary>Painted into the paper first, under the page's sprites and texts.</summary>
    public List<MenuPainting> PagePaintings { get; } = [];

    public List<MenuSprite> PageSprites { get; } = [];

    public List<MenuText> PageTexts { get; } = [];

    public List<MenuSprite> ScreenSprites { get; } = [];

    public List<MenuText> ScreenTexts { get; } = [];

    /// <summary>How black the screen is over everything (a fade).</summary>
    public float Cover { get; set; }

    /// <summary>
    /// The interface's size (OM-87): the screen's own pieces are laid out on a screen this much smaller, and drawn this
    /// much bigger (the page keeps its square). Kept from frame to frame (not cleared).
    /// </summary>
    public float ScreenScale { get; set; } = 1;

    /// <summary>The screen the screen's pieces are laid out on: the real one made smaller by a scale.</summary>
    public static Point Scaled(Point screen, float scale)
    {
        var s = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4) : 1;
        return new Point(Math.Max(1, (int)(screen.X / s)), Math.Max(1, (int)(screen.Y / s)));
    }

    public void Clear()
    {
        ShowsPage = false;
        Book.Shown = false;
        PagePaintings.Clear();
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

/// <summary>
/// The Tome's book as a frame shows it (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), filled on the logic thread: the
/// camera, the candle (where it is and its three colours), and the bones of the book, of its two stacks of pages and of
/// the room (one, the room never moves). The arrays are kept from frame to frame, filled anew.
/// </summary>
public sealed class BookPicture
{
    /// <summary>The bones of the book (its covers and clasp) and of a stack or sheet of pages.</summary>
    public const int BookBones = 6, PageBones = 18;

    public bool Shown { get; set; }

    public Matrix View { get; set; } = Matrix.Identity;

    public Matrix Projection { get; set; } = Matrix.Identity;

    public Vector3 Camera { get; set; }

    public Vector3 Light { get; set; }

    public Vector3 Ambient { get; set; }

    public Vector3 Diffuse { get; set; }

    public Vector3 Specular { get; set; }

    /// <summary>Whether the room (desk, wall, the things on the desk) is drawn behind the book.</summary>
    public bool Room { get; set; } = true;

    public Matrix[] Book { get; } = new Matrix[BookBones];

    /// <summary>The stack of pages on the left, then on the right.</summary>
    public Matrix[] Left { get; } = new Matrix[PageBones];

    public Matrix[] Right { get; } = new Matrix[PageBones];
}
