using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;
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

    /// <summary>A menu's sound with a variable of its cue (the riffle's number of pages); by default without it.</summary>
    void Cue(string name, (string Name, float Value) variable) => Cue(name);

    /// <summary>The world map's cutscenes by chapter (OM-84); none without the game's file.</summary>
    IReadOnlyDictionary<int, MapCutscene> Cutscenes { get; }

    /// <summary>A narration's cue, prepared, not played; none without the sound.</summary>
    ISoundCue? Voice(SoundBanks bank, string cue);

    /// <summary>The music bank's cue as the music.</summary>
    void PlayMusic(string cue);

    /// <summary>A campaign chapter starts from a save slot.</summary>
    /// <param name="resume">At the slot's saved checkpoint (Continue), else from the chapter's start.</param>
    /// <param name="narration">What the loading screen narrates, the level waiting for it (the intro's end).</param>
    void Play(int slot, int chapter, bool resume = false, Narration? narration = null);

    void Quit();
}

/// <summary>
/// The Tome's menu (OM-53, docs/engine/ARCHITECTURE.md §20), on the logic thread: a stack of pages (the main page at the
/// bottom), a message box over them while one is up, and what the book shows (its right page) for each frame. The page
/// lies in the 3D book (OM-83, §20, "The book") where the game has it, else flat in the screen's middle; the riffle comes later.
/// </summary>
public sealed class TomeMenu(IMenuHost host)
{
    /// <summary>A message box's row height (MenuDefault's line and a quarter, as the original's).</summary>
    public const float BoxRow = 44;

    private readonly Stack<MenuPage> pages = new();
    private MessageBox? box;

    public IMenuHost Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    public MenuPage? Page => pages.Count > 0 ? pages.Peek() : null;

    /// <summary>The screen the screen's pieces are laid out on, as last updated or drawn.</summary>
    public Point Screen { get; private set; }

    public bool HasBox => box is not null;

    public string Text(string key) => Host.Strings.Get(key);

    public void Cue(string name) => Host.Cue(name);

    /// <summary>What backing out of the bottom page does; none: the quit box (the main menu's).</summary>
    public Action? Leave { get; init; }

    /// <summary>The menu opens at a page (none: the main page), the pages before it gone; put there, not turned to (no riffle's sound), as the original's.</summary>
    public void Open(Func<TomeMenu, MenuPage>? root = null)
    {
        pages.Clear();
        box = null;
        var page = root?.Invoke(this) ?? new MainPage(this);
        pages.Push(page);
        page.Enter();
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
        Turn(page);
    }

    /// <summary>The riffle's sound with the pages it turns (the original's count as it is, not the sheets drawn).</summary>
    private void Turn(MenuPage page) => Host.Cue("ui_menu_tomepage", ("Pages", page.Pages));

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
        var gone = pages.Pop();
        pages.Peek().Enter();
        Turn(gone);
    }

    /// <summary>Back a page without its sound (a cutscene's end); never the bottom page.</summary>
    public void PopSilently()
    {
        if (pages.Count > 1)
        {
            pages.Pop();
            pages.Peek().Enter();
        }
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

    /// <param name="dt">The time passed, for a page that plays (a cutscene's).</param>
    public void Update(MenuInput input, Point screen, float dt = 0)
    {
        Screen = screen;
        if (box is { } open)
        {
            open.Update(input, screen.X, screen.Y, BoxRow, Cue);
        }
        else if (Page is { } page)
        {
            page.Update(input);
            if (ReferenceEquals(page, Page))
            {
                page.Tick(dt);
            }
        }
    }

    /// <summary>The game's version, shown at the screen's bottom left under the book (none: not shown).</summary>
    public string Version { get; init; } = "";

    public void Draw(MenuPicture picture, Point screen)
    {
        ArgumentNullException.ThrowIfNull(picture);
        Screen = screen;
        picture.ShowsPage = Page is not null;
        Page?.Draw(picture);
        if (picture.Book.Shown && Version.Length > 0 && Page is not CutscenePage)
        {
            picture.ScreenTexts.Add(new MenuText("Maiandra14", Version, new Vector2(16, screen.Y - 16), Engine.Text.TextAlignment.Left, Vector4.One,
                Shadow: true, FromBottom: true));
        }
        box?.Draw(picture, screen.X, screen.Y, BoxRow);
    }
}
