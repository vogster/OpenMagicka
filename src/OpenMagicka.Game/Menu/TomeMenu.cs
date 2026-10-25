using Microsoft.Xna.Framework;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Install;

namespace OpenMagicka.Game.Menu;

/// <summary>What the menu asks of the game around it.</summary>
public interface IMenuHost
{
    StringTable Strings { get; }

    GameSettings Settings { get; }

    CampaignSaves Saves { get; }

    /// <summary>The languages the content has (their folder names).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>The screen sizes the display offers.</summary>
    IReadOnlyList<Point> Resolutions { get; }

    /// <summary>New settings: kept, saved, and put to use.</summary>
    void Apply(GameSettings settings);

    void Cue(string name);

    /// <summary>A campaign chapter starts from a save slot.</summary>
    void Play(int slot, int chapter);

    void Quit();
}

/// <summary>
/// The Tome's menu (OM-53, docs/engine/ARCHITECTURE.md §20), on the logic thread: a stack of pages (the main page at the
/// bottom), a message box over them while one is up, and what the book shows (its right page) for each frame. Ours
/// shows the page flat in the screen's middle; the book's desk and its riffling pages come later.
/// </summary>
public sealed class TomeMenu(IMenuHost host)
{
    /// <summary>A message box's row height (MenuDefault's line and a quarter, as the original's).</summary>
    public const float BoxRow = 44;

    private readonly Stack<MenuPage> pages = new();
    private MessageBox? box;

    public IMenuHost Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    public MenuPage? Page => pages.Count > 0 ? pages.Peek() : null;

    public bool HasBox => box is not null;

    public string Text(string key) => Host.Strings.Get(key);

    public void Cue(string name) => Host.Cue(name);

    /// <summary>What backing out of the bottom page does; none: the quit box (the main menu's).</summary>
    public Action? Leave { get; init; }

    /// <summary>The menu opens at a page (none: the main page), the pages before it gone.</summary>
    public void Open(Func<TomeMenu, MenuPage>? root = null)
    {
        pages.Clear();
        box = null;
        Push(root?.Invoke(this) ?? new MainPage(this));
    }

    /// <summary>The pages back down to the bottom one (none when the menu is closed).</summary>
    public void Close()
    {
        pages.Clear();
        box = null;
    }

    public void Push(MenuPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        pages.Push(page);
        page.Enter();
        Cue("ui_menu_tomepage");
    }

    /// <summary>Back a page; at the main page, the quit box.</summary>
    public void Pop()
    {
        if (pages.Count <= 1)
        {
            if (Leave is { } leave)
            {
                leave();
            }
            else
            {
                AskToQuit();
            }
            return;
        }
        pages.Pop();
        pages.Peek().Enter();
        Cue("ui_menu_tomepage");
    }

    public void AskToQuit() => Ask(Text("#add_menu_rus_quit"), [Text("#add_menu_yes"), Text("#add_menu_no")], 0, yes =>
    {
        if (yes == 0)
        {
            Host.Quit();
        }
    });

    /// <summary>A message box; its answer (the option, or none when closed) goes to <paramref name="done"/>.</summary>
    public void Ask(string text, IReadOnlyList<string> options, int chosen, Action<int?> done, string font = MenuPage.BoxFont) =>
        box = new MessageBox(text, options, chosen, answer =>
        {
            box = null;
            done(answer);
        }, font);

    public void Update(MenuInput input, Point screen)
    {
        if (box is { } open)
        {
            open.Update(input, screen.X, screen.Y, BoxRow, Cue);
        }
        else
        {
            Page?.Update(input);
        }
    }

    public void Draw(MenuPicture picture, Point screen)
    {
        ArgumentNullException.ThrowIfNull(picture);
        picture.ShowsPage = Page is not null;
        Page?.Draw(picture);
        box?.Draw(picture, screen.X, screen.Y, BoxRow);
    }
}
