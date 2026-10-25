using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The Tome's main page (docs/engine/ARCHITECTURE.md §20), as the original lays it out: the Adventure tile (the
/// campaign), the DLC, Challenges, Versus and Online tiles (greyed: not in the beta), the Options button, and the back
/// plate (quitting). The store banner and the social buttons are left out.
/// </summary>
public sealed class MainPage : MenuPage
{
    /// <summary>The tiles: their art (atlas, source), their place, their label's key.</summary>
    private static readonly (MenuAtlas Atlas, Rectangle Art, Vector2 At, string? Label, bool Live)[] Tiles =
    [
        (MenuAtlas.PagesNew, new Rectangle(1, 1, 388, 226), new Vector2(166, 254), "#menu_main_01", true),
        (MenuAtlas.PagesNew, new Rectangle(1, 229, 360, 226), new Vector2(577, 254), "#store_dlc", false),
        (MenuAtlas.Pages, new Rectangle(1040, 416, 336, 400), new Vector2(163, 500), "#menu_main_02", false),
        (MenuAtlas.Pages, new Rectangle(1376, 416, 336, 400), new Vector2(387, 500), "#menu_main_03", false),
        (MenuAtlas.Pages, new Rectangle(1712, 416, 336, 400), new Vector2(611, 500), "#menu_main_05", false),
        (MenuAtlas.PagesNew, new Rectangle(391, 111, 71, 71), new Vector2(851, 912), null, true),
    ];

    /// <summary>A tile's label colour (a pale lilac) and its shadow.</summary>
    public static readonly Vector4 Label = new(0.896f, 0.842f, 0.98f, 1), Shadow = new(0, 0, 0, 0.6f);

    /// <summary>A tile not in the beta: dimmed and without colour (opaque: the lower tiles overlap).</summary>
    public static readonly Vector4 Greyed = new(0.55f, 0.55f, 0.55f, 1);

    public MainPage(TomeMenu menu)
        : base(menu)
    {
        Items.Add(new MenuItem(Area(0), () => Menu.Push(new SlotsPage(Menu))));
        for (var tile = 1; tile <= 4; tile++)
        {
            Items.Add(new MenuItem(Area(tile)) { Enabled = false });
        }
        Items.Add(new MenuItem(Area(5), () => Menu.Push(new OptionsPage(Menu))));
        Items.Add(new MenuItem(BackPlate, Menu.AskToQuit));
        Selected = 0;
    }

    private static Rectangle Area(int tile) => new((int)Tiles[tile].At.X, (int)Tiles[tile].At.Y, Tiles[tile].Art.Width, Tiles[tile].Art.Height);

    public override void Back() => Menu.AskToQuit();

    public override void Draw(MenuPicture picture)
    {
        for (var tile = 0; tile < Tiles.Length; tile++)
        {
            var (atlas, art, at, label, live) = Tiles[tile];
            var lit = tile == Selected ? 1.37f : 1;
            Art(picture, atlas, art, at, live ? new Vector4(lit, lit, lit, 1) : Greyed, live ? 1 : 0.15f);
            if (label is not null)
            {
                var bottom = at + new Vector2(art.Width / 2f, art.Height - 40 - 38);
                Text(picture, ItemFont, Menu.Text(label), bottom + new Vector2(2, 2), live ? Shadow : Shadow * 0.5f);
                Text(picture, ItemFont, Menu.Text(label), bottom, live ? Label : Label * Greyed);
            }
        }
        BackArt(picture, Items.Count - 1);
    }
}
