using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// A message box over the menu (OM-53, docs/engine/ARCHITECTURE.md §20): a parchment from <c>UI/Menu/pages</c> at its
/// own size in the screen's centre, its text above, its options below one a line. Up and down move, Enter or A
/// chooses, Esc or B closes it unchosen; the mouse hovers and clicks. Long lists scroll (the language and screen
/// size boxes).
/// </summary>
public sealed class MessageBox(string text, IReadOnlyList<string> options, int chosen, Action<int?> done, string optionFont = MenuPage.BoxFont)
{
    public static readonly Rectangle Parchment = new(0, 464, 448, 560);
    public const int TextWidth = 403, MaxRows = 10;

    public int Selected { get; private set; } = Math.Clamp(chosen, 0, Math.Max(0, options.Count - 1));

    private int scroll = Math.Max(0, Math.Clamp(chosen, 0, Math.Max(0, options.Count - 1)) - MaxRows + 1); // the chosen one in sight

    /// <summary>The option rows' areas on a screen that size (the first shown row first).</summary>
    public IEnumerable<(int Option, Rectangle Area)> Rows(int width, int height, float lineHeight)
    {
        var top = (int)(height / 2f + (text.Length > 0 ? 40 : -Parchment.Height / 2f + 70));
        for (var row = 0; row < Math.Min(MaxRows, options.Count - scroll); row++)
        {
            yield return (scroll + row, new Rectangle(width / 2 - Parchment.Width / 2 + 24, (int)(top + row * lineHeight), Parchment.Width - 48, (int)lineHeight));
        }
    }

    public void Update(MenuInput input, int width, int height, float lineHeight, Action<string> cue)
    {
        var rows = Rows(width, height, lineHeight).ToList();
        if (input.Moved && rows.FirstOrDefault(r => r.Area.Contains(input.Screen)) is { Area.Width: > 0 } hovered && hovered.Option != Selected)
        {
            Selected = hovered.Option;
            cue("ui_menu_mouseover");
        }
        if (input.Click && rows.FirstOrDefault(r => r.Area.Contains(input.Screen)) is { Area.Width: > 0 } clicked)
        {
            cue("ui_menu_select");
            done(clicked.Option);
            return;
        }
        var step = input.Vertical != 0 ? input.Vertical : -input.Wheel;
        if (step != 0 && options.Count > 0)
        {
            Selected = Math.Clamp(Selected + step, 0, options.Count - 1);
            scroll = Math.Clamp(scroll, Math.Max(0, Selected - MaxRows + 1), Selected);
            cue("ui_menu_scroll");
        }
        if (input.Accept)
        {
            cue("ui_menu_select");
            done(options.Count > 0 ? Selected : null);
        }
        else if (input.Back)
        {
            done(null);
        }
    }

    public void Draw(MenuPicture picture, int width, int height, float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var at = new Vector2(width / 2f - Parchment.Width / 2f, height / 2f - Parchment.Height / 2f);
        picture.ScreenSprites.Add(new MenuSprite(MenuAtlas.Pages, Parchment, at, new Vector2(Parchment.Width, Parchment.Height), Vector4.One));
        if (text.Length > 0)
        {
            picture.ScreenTexts.Add(new MenuText(MenuPage.BoxFont, text, new Vector2(width / 2f, at.Y + 90), TextAlignment.Center, MenuPage.Ink, 1, TextWidth));
        }
        foreach (var (option, area) in Rows(width, height, lineHeight))
        {
            picture.ScreenTexts.Add(new MenuText(optionFont, options[option], new Vector2(width / 2f, area.Y), TextAlignment.Center,
                option == Selected ? MenuPage.Lit : MenuPage.Ink));
        }
    }
}
