using System.Globalization;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;
using OpenMagicka.Game.Menu;

namespace OpenMagicka.Game.Shell;

/// <summary>
/// The loading screen between the menu and a level (OM-53, docs/engine/ARCHITECTURE.md §20), as the original's in
/// part: black, its backdrop as a square of the screen's height in the middle, the chapter's name and title, "Loading"
/// with dots counting up, and a tip at the bottom (Maiandra14 wrapped to 700 px, its bottom two lines above the
/// screen's). The original's picture of a spell and its progress bar are left out. A narration (the intro's last
/// words, OM-84) takes the tip's place.
/// </summary>
public static class LoadingScreen
{
    public const int Tips = 22, TipWidth = 700;
    public const string TipFont = "Maiandra14", TitleFont = "MenuTitle", WordFont = "Maiandra18";

    public static readonly Rectangle Backdrop = new(0, 0, 1024, 1024);

    /// <summary>A tip's key (1…22).</summary>
    public static string TipKey(int tip) => "#tip" + Math.Clamp(tip, 1, Tips).ToString("D2", CultureInfo.InvariantCulture);

    public static void Write(MenuPicture picture, StringTable strings, int chapter, int tip, double seconds, Point screen, float alpha = 1, string? narration = null)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(strings);
        var (w, h) = (screen.X, screen.Y);
        var white = new Vector4(1, 1, 1, Math.Clamp(alpha, 0, 1));
        picture.ScreenSprites.Add(new MenuSprite(MenuAtlas.Loading, Backdrop, new Vector2((w - h) / 2f, 0), new Vector2(h, h), white));
        var s = Math.Max(h, 1) / 720f;
        picture.ScreenTexts.Add(new MenuText(TitleFont, strings.Get(Chapters.NameKey(chapter)), new Vector2(w / 2f, h * 0.25f), TextAlignment.Center, white, s));
        picture.ScreenTexts.Add(new MenuText(TitleFont, strings.Get(Chapters.TitleKey(chapter)), new Vector2(w / 2f, h * 0.25f + 60 * s), TextAlignment.Center,
            white, 0.8f * s));
        var dots = new string('.', (int)(Math.Max(0, seconds) / 0.5) % 4);
        picture.ScreenTexts.Add(new MenuText(WordFont, strings.Get("#network_23") + dots, new Vector2(w / 2f, h * 0.6f), TextAlignment.Center, white, s));
        var words = narration is { } told ? strings.Get(told) : strings.Get("#tip") + strings.Get(TipKey(tip));
        picture.ScreenTexts.Add(new MenuText(TipFont, words, new Vector2(w / 2f, h - 4 * 18 * s), TextAlignment.Center, white, s, TipWidth));   // wrapped before scaling
    }
}
