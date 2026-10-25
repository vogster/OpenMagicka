using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Game.Menu;

/// <summary>What the pause menu's items do to the level.</summary>
public sealed record PauseActions(Action Resume, Action Restart, Action Quit);

/// <summary>
/// The pause menu over a level (OM-53, docs/engine/ARCHITECTURE.md §20), on the screen as the original's: a dark panel
/// in the middle, its items in Maiandra18 (scaled with the screen's height against 720), the selected one dark on a
/// light bar. Resume, Restart (asked first: the level's progress goes), Options (the Tome's options pages over the
/// level) and Quit (asked first, to the lobby). Back, or Start again, resumes.
/// </summary>
public sealed class PauseMenu
{
    public const string Font = "Maiandra18";
    public const float Height = 720, PanelSize = 400, FirstItem = 290, ItemStep = 28;

    private static readonly string[] Labels = ["#menu_resume", "#add_menu_restart", "#menu_main_06", "#menu_main_07"];

    private static readonly Vector4 Panel = new(0, 0, 0, 0.666f), Bar = new(1, 1, 1, 0.8f), White = Vector4.One, Dark = new(0, 0, 0, 1);

    private readonly IMenuHost host;
    private readonly PauseActions actions;
    private readonly TomeMenu options;
    private MessageBox? box;

    public PauseMenu(IMenuHost host, PauseActions actions)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        options = new TomeMenu(host) { Leave = CloseOptions };
    }

    /// <summary>The selected item; none until the mouse or a key picks one (the original's, with the keyboard).</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The options pages are up (the book's page over the level).</summary>
    public bool InOptions => options.Page is not null;

    /// <summary>The menu comes up: nothing selected, no box, no options.</summary>
    public void Open(bool pad)
    {
        Selected = pad ? 0 : -1;
        box = null;
        options.Close();
    }

    private void CloseOptions()
    {
        options.Close();
        host.Cue("ui_menu_tomepage");
    }

    /// <summary>The panel's scale: the screen's height against the original's 720.</summary>
    public static float Scale(Point screen) => Math.Max(screen.Y, 1) / Height;

    /// <summary>The items' rows on that screen (the panel's width, an item high).</summary>
    public static Rectangle Row(int item, Point screen)
    {
        var s = Scale(screen);
        return new Rectangle((int)(screen.X / 2f - PanelSize * s / 2), (int)((FirstItem + item * ItemStep) * s), (int)(PanelSize * s), (int)(ItemStep * s));
    }

    public void Update(MenuInput input, Point screen)
    {
        if (InOptions)
        {
            options.Update(input, screen);
            return;
        }
        if (box is { } open)
        {
            open.Update(input, screen.X, screen.Y, TomeMenu.BoxRow, host.Cue);
            return;
        }
        var hovered = Enumerable.Range(0, Labels.Length).Where(i => Row(i, screen).Contains(input.Screen)).DefaultIfEmpty(-1).First();
        if (input.Moved && hovered >= 0 && hovered != Selected)
        {
            Selected = hovered;
            host.Cue("ui_menu_scroll");
        }
        if (input.Click && hovered >= 0)
        {
            Selected = hovered;
            Choose(hovered);
            return;
        }
        if (input.Vertical != 0)
        {
            Selected = Selected < 0 ? (input.Vertical > 0 ? 0 : Labels.Length - 1) : (Selected + input.Vertical + Labels.Length) % Labels.Length;
            host.Cue("ui_menu_scroll");
        }
        if (input.Accept && Selected >= 0)
        {
            Choose(Selected);
        }
        else if (input.Back || input.Start)
        {
            host.Cue("ui_menu_decrease");
            actions.Resume();
        }
    }

    private void Choose(int item)
    {
        switch (item)
        {
            case 0:
                host.Cue("ui_menu_decrease");
                actions.Resume();
                break;
            case 1:
                host.Cue("ui_menu_increase");
                Ask(actions.Restart);
                break;
            case 2:
                host.Cue("ui_menu_select");
                options.Open(menu => new OptionsPage(menu));
                break;
            default:
                host.Cue("ui_menu_select");
                Ask(actions.Quit);
                break;
        }
    }

    /// <summary>The original's "Are you sure? Any progress on this level will be lost." with Yes and No.</summary>
    private void Ask(Action yes) =>
        box = new MessageBox($"{host.Strings.Get("#add_menu_rus")} {host.Strings.Get("#add_menu_rus_quitlev")}",
            [host.Strings.Get("#add_menu_yes"), host.Strings.Get("#add_menu_no")], 1, answer =>
            {
                box = null;
                if (answer == 0)
                {
                    yes();
                }
            });

    public void Draw(MenuPicture picture, Point screen)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (InOptions)
        {
            options.Draw(picture, screen);
            return;
        }
        var s = Scale(screen);
        var fade = new Rectangle(0, 0, 128, 128);
        var top = (Height - PanelSize) / 2 * s;
        picture.ScreenSprites.Add(new MenuSprite(MenuAtlas.FadeBox, fade, new Vector2(screen.X / 2f - PanelSize * s / 2, top),
            new Vector2(PanelSize * s), Panel));
        if (box is { } open)
        {
            open.Draw(picture, screen.X, screen.Y, TomeMenu.BoxRow);         // alone: the screen's texts go over all its sprites
            return;
        }
        for (var item = 0; item < Labels.Length; item++)
        {
            var row = Row(item, screen);
            if (item == Selected)
            {
                picture.ScreenSprites.Add(new MenuSprite(MenuAtlas.FadeBox, fade, new Vector2(row.X, row.Y), new Vector2(row.Width, row.Height), Bar));
            }
            picture.ScreenTexts.Add(new MenuText(Font, host.Strings.Get(Labels[item]), new Vector2(screen.X / 2f, row.Y), TextAlignment.Center,
                item == Selected ? Dark : White, s));
        }
    }
}
