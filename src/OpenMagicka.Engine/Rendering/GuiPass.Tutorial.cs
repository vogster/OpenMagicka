using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Scripting;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The tutorial's pieces as the original draws them (docs/engine/ARCHITECTURE.md §21): the darkening under the HUD;
/// the hint, a half-clear speech-box frame round its text (Maiandra14 wrapped to 300 px), at its place; the dialog
/// hint, an opaque frame centred at 0.4 of the screen's height, its element's 50 px icon (or its magick's 400 × 250
/// picture from <c>UI/Menu/Magicks</c>) at the top left and the text 6 px right of it.
/// </summary>
public sealed partial class GuiPass
{
    public const string TutorialFont = "Maiandra14";
    public const int TutorialWrap = 300, HintIconSize = 50, HintIconGap = 6, MagickWidth = 400, MagickHeight = 250, MagicksPerSheet = 37;

    public const string MagicksAtlas = "UI/Menu/Magicks", MagicksAtlas2 = "UI/Menu/Magicks_2";

    /// <summary>The tutorial's texts as wrapped and measured, by their text (a few at a time; the fonts' change empties it).</summary>
    private readonly Dictionary<string, (string Wrapped, int Width, int Height)> tutorialTexts = [];

    /// <summary>The magicks' pictures, five a row: the first 37, then the rest.</summary>
    private (Texture2D First, Texture2D Second) magicks;

    /// <summary>The hints' clips' pictures (<c>UI/HUD/Tutorial</c>).</summary>
    private Texture2D? clips;

    private readonly List<Hud.ClipPiece> clipPieces = [];
    private readonly List<GuiSprite> clipSprites = [];

    /// <summary>A magick's picture: its sheet (the second from the 38th) and its 400 × 250 cell there; none for none.</summary>
    public static (bool Second, Rectangle Cell)? MagickCell(Gameplay.Spells.MagickType magick)
    {
        var n = (int)magick;
        if (n <= 0)
        {
            return null;
        }
        var (second, index) = n < MagicksPerSheet ? (false, n) : (true, n - MagicksPerSheet);
        return (second, new Rectangle(index % 5 * MagickWidth, index / 5 * MagickHeight, MagickWidth, MagickHeight));
    }
    public const float MostDark = 0.5f;

    private (string Wrapped, int Width, int Height) Laid(GuiFont font, string text)
    {
        if (!tutorialTexts.TryGetValue(text, out var laid))
        {
            if (tutorialTexts.Count >= 8)
            {
                tutorialTexts.Clear();
            }
            var wrapped = font.Metrics.Wrap(text, TutorialWrap);
            var (width, height) = font.Metrics.Measure(wrapped);
            tutorialTexts[text] = laid = (wrapped, width, height);
        }
        return laid;
    }

    private void DrawDark(GraphicsDevice device, TutorialPicture? tutorial, int width, int height)
    {
        if (tutorial is { Dark: > 0 } shown)
        {
            Text.DrawQuads(device, [(Vector2.Zero, new Vector2(width, height))], new Vector4(0, 0, 0, MostDark * shown.Dark), width, height);
        }
    }

    /// <summary>
    /// A hint's centre on a screen of that size, for its text's size (as the original's: the right ones 5 % in, the top
    /// one at a tenth, the centre ones at 0.7 of the height). Ours: the left ones 5 % in from the left (the original's
    /// centre-left misses it), and the corners and the bottom, which the original leaves at the screen's corner, placed
    /// like the rest.
    /// </summary>
    public static Vector2 HintCentre(HintPosition position, Point size, int width, int height)
    {
        var (left, right) = (0.05f * width + size.X / 2f, 0.95f * width - size.X / 2f);
        var (top, middle, bottom) = (0.1f * height, 0.7f * height, 0.95f * height - size.Y / 2f);
        var centre = position switch
        {
            HintPosition.Top => new Vector2(0.5f * width, top),
            HintPosition.TopLeft => new Vector2(left, top),
            HintPosition.TopRight => new Vector2(right, top),
            HintPosition.Center => new Vector2(0.5f * width, middle),
            HintPosition.CenterLeft => new Vector2(left, middle),
            HintPosition.CenterRight => new Vector2(right, middle),
            HintPosition.BottomLeft => new Vector2(left, bottom),
            HintPosition.Bottom => new Vector2(0.5f * width, bottom),
            _ => new Vector2(right, bottom),
        };
        return new Vector2(MathF.Floor(centre.X), MathF.Floor(centre.Y));
    }

    private void DrawHint(GraphicsDevice device, TutorialPicture tutorial, int width, int height)
    {
        if (tutorial.Hint is not { Length: > 0 } text || tutorial.HintAlpha <= 0)
        {
            return;
        }
        var font = Fonts[TutorialFont];
        var (wrapped, w, h) = Laid(font, text);
        var clip = tutorial.Clip != HintAnimations.None;
        var size = new Point(w + (clip ? Hud.HintClips.Strip : 0), h);       // the clip's strip right of the text
        var centre = HintCentre(tutorial.Position, size, width, height);
        DrawFrame(device, centre, size, 1, width, height, new Vector4(1, 1, 1, 0.5f * tutorial.HintAlpha));
        var faded = new Vector4(1, 1, 1, tutorial.HintAlpha);
        if (clip)
        {
            DrawClip(device, font, tutorial, new Vector2(MathF.Floor(centre.X + size.X / 2f) - Hud.HintClips.Strip / 2, centre.Y), width, height);
        }
        Text.Draw(device, font, wrapped, Vector4.One, centre - new Vector2(size.X, size.Y) / 2, 1, int.MaxValue, width, height, tint: faded);
    }

    private static readonly string[] Ascii = [.. Enumerable.Range(0, 128).Select(c => ((char)c).ToString())];

    /// <summary>A letter as text, the plain ones made once.</summary>
    private static string LetterText(char letter) => letter < Ascii.Length ? Ascii[letter] : letter.ToString();

    /// <summary>A hint's clip at its anchor: its pictures, then the letters on its key caps (black, fading with the hint; the original's do not fade).</summary>
    private void DrawClip(GraphicsDevice device, GuiFont font, TutorialPicture tutorial, Vector2 anchor, int width, int height)
    {
        if (clips is not { } atlas)
        {
            return;
        }
        clipPieces.Clear();
        Hud.HintClips.Pieces(tutorial.Clip, tutorial.ClipTime, tutorial.Pad, tutorial.Letters, clipPieces);
        var faded = new Vector4(1, 1, 1, tutorial.HintAlpha);
        var size = new Vector2(atlas.Width, atlas.Height);
        clipSprites.Clear();
        foreach (var p in clipPieces)
        {
            var half = new Vector2(p.Source.Width, p.Source.Height) / 2;
            var at = anchor + p.Offset;
            clipSprites.Add(new GuiSprite(at - half, at + half, new Vector2(p.Source.Left, p.Source.Top) / size, new Vector2(p.Source.Right, p.Source.Bottom) / size,
                faded));
        }
        Text.DrawSprites(device, atlas, clipSprites, width, height);
        foreach (var piece in clipPieces)
        {
            if (piece.Letter is not '\0' and not ' ')
            {
                Text.Draw(device, font, LetterText(piece.Letter), new Vector4(0, 0, 0, tutorial.HintAlpha),
                    anchor + piece.Offset - new Vector2(0, font.Metrics.LineHeight / 2), 1, int.MaxValue, width, height, Engine.Text.TextAlignment.Center);
            }
        }
    }

    /// <summary>The dialog hint's inner size: as given, else its text's beside its picture (at least the picture high, an icon's when none).</summary>
    public static Point DialogHintSize(Point text, Point picture, float scale, Vector2? given) =>
        given is { } size ? new Point((int)size.X, (int)size.Y)
            : new Point(text.X + (int)(picture.X * scale), Math.Max(text.Y, (int)(Math.Max(picture.Y, HintIconSize) * scale)));

    private void DrawDialogHint(GraphicsDevice device, TutorialPicture tutorial, int width, int height)
    {
        if (tutorial.Dialog is not { } text || tutorial.DialogAlpha <= 0)
        {
            return;
        }
        var font = Fonts[TutorialFont];
        var (wrapped, w, h) = Laid(font, text);
        var scale = tutorial.Scale > 0 ? tutorial.Scale : 1;
        var cell = IconCell(tutorial.Element, HintIconSize);                  // the HUD's element cells, 50 px apart here
        var magick = MagickCell(tutorial.Magick);
        var picture = magick is not null ? new Point(MagickWidth, MagickHeight) : cell is not null ? new Point(HintIconSize, HintIconSize) : Point.Zero;
        var size = DialogHintSize(new Point(w, h), picture, scale, tutorial.Size);
        var centre = new Vector2(MathF.Floor(0.5f * width) + 0.5f, MathF.Floor(0.4f * height) + 0.5f);
        var faded = new Vector4(1, 1, 1, tutorial.DialogAlpha);
        DrawFrame(device, centre, size, 1, width, height, faded);
        var corner = centre - new Vector2(size.X, size.Y) / 2;
        var (sheet, from) = magick is var (second, magickCell) ? (second ? magicks.Second : magicks.First, magickCell)
            : cell is { } iconCell ? (hud, iconCell) : (null, Rectangle.Empty);
        if (sheet is not null)
        {
            var atlas = new Vector2(sheet.Width, sheet.Height);
            Text.DrawSprites(device, sheet, [new GuiSprite(corner, corner + new Vector2(picture.X, picture.Y) * scale, new Vector2(from.Left, from.Top) / atlas,
                new Vector2(from.Right, from.Bottom) / atlas, faded)], width, height);
        }
        var indent = picture.X > 0 ? (picture.X + HintIconGap) * scale : 0;
        Text.Draw(device, font, wrapped, Vector4.One, corner + new Vector2(indent, 0), 1, int.MaxValue, width, height, tint: faded);
    }
}
