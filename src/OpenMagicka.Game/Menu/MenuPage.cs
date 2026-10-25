using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Game.Menu;

/// <summary>An item of a page: where it answers the mouse (page pixels), and what it does when chosen or turned left or right.</summary>
public sealed class MenuItem(Rectangle area, Action? choose = null, Action<int>? turn = null)
{
    public Rectangle Area { get; set; } = area;

    public bool Enabled { get; set; } = true;

    public Action? Choose { get; } = choose;

    /// <summary>A setting's step left (−1) or right (+1); none for a plain item (choosing it then steps right, as the original).</summary>
    public Action<int>? Turn { get; } = turn;
}

/// <summary>
/// A page of the Tome (OM-53, docs/engine/ARCHITECTURE.md §20): its items, one of them selected. The mouse selects
/// what it hovers and a click chooses it; the keys and a pad move through the enabled items (wrapping) and choose;
/// left and right turn a setting; back goes to the page before. Each draws itself into the page's picture.
/// </summary>
public abstract class MenuPage
{
    /// <summary>The ink of the page's text, a selected item's light, a disabled one's grey (the original's colours).</summary>
    public static readonly Vector4 Ink = new(0.1f, 0.05f, 0, 0.7f), Lit = new(1.5f, 1.5f, 1.5f, 1), Disabled = new(0, 0, 0, 0.4f);

    public const string TitleFont = "MenuTitle", ItemFont = "MenuOption", BoxFont = "MenuDefault";

    /// <summary>The back plate at the page's bottom left (an arrow), its art in <c>UI/Menu/pages</c>.</summary>
    public static readonly Rectangle BackPlate = new(96, 912, 320, 96), BackPlateArt = new(1696, 48, 320, 96);

    protected MenuPage(TomeMenu menu) => Menu = menu ?? throw new ArgumentNullException(nameof(menu));

    protected TomeMenu Menu { get; }

    public List<MenuItem> Items { get; } = [];

    public int Selected { get; set; } = -1;

    /// <summary>How many pages the book turns to it (the original's counts).</summary>
    public virtual int Pages => 1;

    /// <summary>The page is shown again (it came up, or the one over it went).</summary>
    public virtual void Enter()
    {
    }

    public virtual void Back() => Menu.Pop();

    /// <summary>The Delete key's action on the page (none but the slots').</summary>
    protected virtual void Delete()
    {
    }

    public void Update(MenuInput input)
    {
        if (input.Moved && input.Page is { } over)
        {
            var hovered = Items.FindIndex(i => i.Enabled && i.Area.Contains(Pixel(over)));
            if (hovered >= 0 && hovered != Selected)
            {
                Selected = hovered;
                Menu.Cue("ui_menu_mouseover");
            }
        }
        if (input.Click && input.Page is { } at && Items.FindIndex(i => i.Enabled && i.Area.Contains(Pixel(at))) is var clicked and >= 0)
        {
            Selected = clicked;
            Choose(Items[clicked]);
            return;
        }
        if (input.Vertical != 0)
        {
            Move(input.Vertical);
        }
        if (input.Horizontal != 0 && Selected >= 0 && Items[Selected].Turn is { } turn)
        {
            turn(input.Horizontal);
            Menu.Cue(input.Horizontal > 0 ? "ui_menu_increase" : "ui_menu_decrease");
        }
        if (input.Accept && Selected >= 0 && Items[Selected].Enabled)
        {
            Choose(Items[Selected]);
        }
        else if (input.Back)
        {
            Back();
        }
        else if (input.Delete)
        {
            Delete();
        }
    }

    private void Choose(MenuItem item)
    {
        Menu.Cue("ui_menu_select");
        if (item.Choose is { } choose)
        {
            choose();
        }
        else
        {
            item.Turn?.Invoke(1);
        }
    }

    /// <summary>The selection moves to the next enabled item that way (wrapping).</summary>
    protected void Move(int way)
    {
        if (Items.Count == 0 || !Items.Any(i => i.Enabled))
        {
            return;
        }
        var at = Selected < 0 ? (way > 0 ? -1 : 0) : Selected;
        do
        {
            at = (at + way + Items.Count) % Items.Count;
        }
        while (!Items[at].Enabled);
        Selected = at;
        Menu.Cue("ui_menu_scroll");
    }

    public abstract void Draw(MenuPicture picture);

    /// <summary>The pixel a point is in.</summary>
    public static Point Pixel(Vector2 at) => new((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y));

    /// <summary>A text's colour on the page: ink, light when its item is selected, grey when disabled.</summary>
    protected Vector4 ColourOf(int item) =>
        item >= 0 && item < Items.Count && !Items[item].Enabled ? Disabled : item == Selected ? Lit : Ink;

    /// <summary>A text on the page; a lit one over an ink shadow (ours: the flat page lacks the book's candle light that sets it off).</summary>
    protected static void Text(MenuPicture picture, string font, string text, Vector2 at, Vector4 colour, TextAlignment alignment = TextAlignment.Center,
        float scale = 1, int wrap = 0)
    {
        if (colour.X > 1)
        {
            picture.PageTexts.Add(new MenuText(font, text, at + new Vector2(2, 2), alignment, Ink, scale, wrap));
        }
        picture.PageTexts.Add(new MenuText(font, text, at, alignment, colour, scale, wrap));
    }

    protected static void Art(MenuPicture picture, MenuAtlas atlas, Rectangle source, Vector2 at, Vector4? colour = null, float saturation = 1) =>
        picture.PageSprites.Add(new MenuSprite(atlas, source, at, new Vector2(source.Width, source.Height), colour ?? Vector4.One, saturation));

    /// <summary>The page's title centred at its top.</summary>
    protected void Title(MenuPicture picture, string key) => Text(picture, TitleFont, Menu.Text(key), new Vector2(512, 72), Ink);

    /// <summary>The back plate, lit when selected.</summary>
    protected void BackArt(MenuPicture picture, int item) =>
        Art(picture, MenuAtlas.Pages, BackPlateArt, new Vector2(BackPlate.X, BackPlate.Y), item == Selected ? new Vector4(1.3f, 1.3f, 1.3f, 1) : Vector4.One);
}
