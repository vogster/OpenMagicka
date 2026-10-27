using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Install;
using OpenMagicka.Game.Menu;

namespace OpenMagicka.Game.Shell;

/// <summary>
/// The game on the logic thread (OM-53, docs/engine/ARCHITECTURE.md §20): the Tome's menu, then a campaign chapter
/// loading and played, its pause menu and its ending, and back. It is the menu's host: the settings it changes are
/// saved and put to use at once (the screen's and the language's by the render thread, through the frame).
/// </summary>
public sealed partial class ShellSimulation : ISimulation<ShellFrame>, IMenuHost
{
    private readonly IGameHost host;
    private readonly ShellContent content;
    private readonly ShellSignals signals;
    private readonly TomeMenu menu;
    private readonly MenuInputReader reader = new();
    private ShellMode mode = ShellMode.Menu;
    private Point realScreen;

    /// <summary>How much bigger the screen-pixel pieces are drawn (the settings' interface size, OM-87).</summary>
    public float InterfaceScale => Settings.InterfaceScale(realScreen.Y);

    /// <summary>The screen the screen's pieces (boxes, pause, loading) are laid out on: the real one made smaller by the interface's size.</summary>
    private Point screen => MenuPicture.Scaled(realScreen, InterfaceScale);
    private StringTable strings;

    /// <param name="screen">The screen's size at the start (the settings' when they give none).</param>
    public ShellSimulation(IGameHost host, ShellContent content, ShellSignals signals, IReadOnlyList<Point> resolutions, Point screen)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        Resolutions = resolutions ?? throw new ArgumentNullException(nameof(resolutions));
        Settings = Known(content.Settings.Clamped());
        if (Settings.Width is null || Settings.Height is null)
        {
            Settings = Settings with { Width = screen.X, Height = screen.Y };
        }
        realScreen = screen;
        strings = StringsOf(Settings.Language);
        menu = new TomeMenu(this);
        menu.Open();
        pause = new PauseMenu(this, new PauseActions(Resume, RestartLevel, QuitLevel));
        content.Sounds?.PlayMusic(SoundBanks.Music, MenuMusic);
    }

    public const string MenuMusic = "music_menu";

    public ShellMode Mode => mode;

    public TomeMenu Menu => menu;

    public StringTable Strings => strings;

    public GameSettings Settings { get; private set; }

    public CampaignSaves Saves => content.Saves;

    public IReadOnlyList<string> Languages => content.Languages;

    public IReadOnlyList<Point> Resolutions { get; }

    public IReadOnlyDictionary<int, MapCutscene> Cutscenes => content.Cutscenes;

    public ISoundCue? Voice(SoundBanks bank, string cue) => content.Sounds?.Get(bank, cue);

    public void PlayMusic(string cue) => content.Sounds?.PlayMusic(SoundBanks.Music, cue);

    private StringTable StringsOf(string language)
    {
        try
        {
            return LanguagePack.Load(content.Files, language).With(OwnStrings.For(language));
        }
        catch (Exception e) when (e is DirectoryNotFoundException or InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"OpenMagicka: no strings for {language} ({e.Message})");
            return StringTable.Empty.With(OwnStrings.For(language));
        }
    }

    public void Apply(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var applied = Known(settings.Clamped());
        if (applied.Language != Settings.Language)
        {
            strings = StringsOf(applied.Language);
        }
        Settings = applied;
        content.Sounds?.SetVolumes(applied.MusicVolume, applied.SoundVolume);
        ApplyHud();
        try
        {
            applied.Save(content.SettingsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: the settings were not saved ({e.Message})");
        }
    }

    /// <summary>Settings whose language the content has (else English).</summary>
    private GameSettings Known(GameSettings settings) =>
        content.Languages.Contains(settings.Language, StringComparer.OrdinalIgnoreCase) ? settings : settings with { Language = LanguagePack.Fallback };

    /// <summary>A menu's sound: the UI bank's, or the Misc bank's for its own names (the volume's sample).</summary>
    public void Cue(string name) =>
        content.Sounds?.Play(name.StartsWith("misc_", StringComparison.Ordinal) ? SoundBanks.Misc : SoundBanks.UI, name);

    public void Quit() => host.RequestExit();

    public void Tick(double dt)
    {
        var input = host.Input.Value;
        if (input.Screen.X > 0 && input.Screen.Y > 0)
        {
            realScreen = input.Screen;
        }
        var read = reader.Read(input, (float)dt);
        var laid = screen;                                                    // the mouse over the screen's pieces, on their smaller screen
        read = read with { Screen = new Point(read.Screen.X * laid.X / Math.Max(1, realScreen.X), read.Screen.Y * laid.Y / Math.Max(1, realScreen.Y)) };
        switch (mode)
        {
            case ShellMode.Menu:
                menu.Update(read, screen, (float)dt);
                content.Sounds?.Update((float)dt, []);
                break;
            case ShellMode.Loading:
                TickLoading(dt);
                content.Sounds?.Update((float)dt, []);
                break;
            default:
                TickPlay(dt, read);
                break;
        }
    }

    public void WriteFrame(ShellFrame frame, FrameTiming timing)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Screen = new ScreenMode(Settings.Width, Settings.Height, Settings.Fullscreen, Settings.VSync);
        frame.Language = Settings.Language;
        frame.Menu.Clear();
        frame.Menu.ScreenScale = InterfaceScale;
        frame.Play.Gui.Scale = InterfaceScale;
        frame.Mode = mode;
        frame.Level = play is null ? 0 : level;
        frame.Channel = play?.Channel;
        switch (mode)
        {
            case ShellMode.Menu:
                menu.Draw(frame.Menu, screen);
                break;
            case ShellMode.Loading:
                LoadingScreen.Write(frame.Menu, strings, chapter, tip, loadingSeconds, screen, LoadingAlpha, told);
                break;
            default:
                WritePlay(frame, timing);
                break;
        }
    }
}
