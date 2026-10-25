using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Play;

namespace OpenMagicka.Game.Shell;

/// <summary>
/// The game (OM-53, docs/engine/ARCHITECTURE.md §20): one window that goes from the Tome's menu to a chapter's loading
/// screen and its level and back, the logic in <see cref="ShellSimulation"/>. This thread draws: the level through
/// <see cref="PlayView"/> (begun and ended as the frame's level comes and goes), the menu's picture through
/// <see cref="MenuPass"/> (made again when the language changes its fonts), and puts the screen the settings ask for
/// to use. With a screenshot path it saves a frame of the menu once it is up and exits.
/// </summary>
public sealed class GameShell : ThreadedGame<ShellFrame>
{
    public const int DefaultWidth = 1280, DefaultHeight = 720, ScreenshotAfterFrames = 60;

    private readonly ShellContent content;
    private readonly ShellSignals signals = new();
    private readonly string? screenshot;
    private readonly IReadOnlyList<Point> resolutions;
    private ShellSimulation? simulation;
    private PlayView? view;
    private MenuPass? pass;
    private Task<MenuPass>? passLoading;
    private string? passLanguage;
    private readonly HashSet<string> failed = [];
    private int levelShown;
    private ScreenMode applied;
    private int framesShown;
    private bool deviceMade;

    public GameShell(ShellContent content, string? screenshot = null)
        : base(() => new ShellFrame())
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        this.screenshot = screenshot;
        Window.Title = $"OpenMagicka {Version}";
        IsMouseVisible = true;
        resolutions = Resolutions(GraphicsAdapter.DefaultAdapter.SupportedDisplayModes.Select(m => new Point(m.Width, m.Height)));
        var settings = content.Settings;
        var (width, height) = SetScreen(new ScreenMode(settings.Width, settings.Height, settings.Fullscreen, settings.VSync));
        applied = new ScreenMode(width, height, settings.Fullscreen, settings.VSync);      // as the simulation fills it in: no second reset
    }

    /// <summary>The game's version (without the commit the build adds).</summary>
    public static string Version =>
        (typeof(GameShell).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault()
            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] ?? "";

    /// <summary>0, or 1 when a requested screenshot could not be taken.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The screen sizes offered: each once, at least 800 × 600, smallest first (the original's list).</summary>
    public static IReadOnlyList<Point> Resolutions(IEnumerable<Point> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        return [.. modes.Where(m => m.X >= 800 && m.Y >= 600).Distinct().OrderBy(m => m.X).ThenBy(m => m.Y)];
    }

    protected override ISimulation<ShellFrame> CreateSimulation() =>
        simulation = new ShellSimulation(this, content, signals, resolutions, new Point(Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight));

    /// <summary>The logic thread stops first; then the level's time played is kept.</summary>
    protected override void OnExiting(object sender, EventArgs args)
    {
        base.OnExiting(sender, args);
        simulation?.Close();
    }

    protected override void LoadContent()
    {
        base.LoadContent();
        deviceMade = true;
        view = new PlayView(GraphicsDevice, content.Loader, GpuWork);
    }

    protected override void UnloadContent()
    {
        view?.Dispose();
        pass?.Dispose();
        if (passLoading is { IsCompletedSuccessfully: true } made)
        {
            made.Result.Dispose();
        }
        base.UnloadContent();
    }

    protected override void Render(ShellFrame? frame, TimeSpan now)
    {
        if (frame is null || view is null)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }
        if (frame.Screen != applied)
        {
            applied = frame.Screen;
            SetScreen(applied);
        }
        var menu = Pass(frame.Language);
        view.Language = frame.Language;
        Follow(frame);
        var (w, h) = (GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        if (frame.Mode == ShellMode.Menu || frame.Level == 0)
        {
            GraphicsDevice.Clear(Color.Black);
            menu?.Draw(GraphicsDevice, frame.Menu, w, h);
        }
        else
        {
            view.Render(GraphicsDevice, frame.Play, now);                     // on the loading screen too: the scene loads as it is drawn
            if (frame.Level != levelShown && (view is { IsShowing: true, EffectsReady: true } || view.Failure is not null))
            {
                levelShown = frame.Level;
                signals.Shown = frame.Level;                                  // the level may run: it is seen (or never will be)
            }
            menu?.Draw(GraphicsDevice, frame.Menu, w, h, clear: frame.Mode == ShellMode.Loading);
        }
        if (screenshot is not null && menu is not null && ++framesShown == ScreenshotAfterFrames)
        {
            ExitCode = Screenshots.Save(GraphicsDevice, screenshot) ? 0 : 1;
            RequestExit();
        }
    }

    /// <summary>The view follows the frame's level: begun for a new one, ended when it goes.</summary>
    private void Follow(ShellFrame frame)
    {
        if (frame.Level == view!.Level)
        {
            return;
        }
        if (frame.Level != 0 && frame.Channel is { } channel)
        {
            view.Begin(channel, frame.Level);
        }
        else
        {
            view.End();
        }
    }

    /// <summary>The menu's pass in the frame's language; while another loads, the one before (none at first).</summary>
    private MenuPass? Pass(string language)
    {
        if (passLoading is { IsCompleted: true } done)
        {
            passLoading = null;
            if (done.IsCompletedSuccessfully)
            {
                pass?.Dispose();
                pass = done.Result;
            }
            else
            {
                failed.Add(passLanguage!);
                Console.Error.WriteLine($"OpenMagicka: the menu did not load in {passLanguage}: {done.Exception?.GetBaseException().Message}");
                if (!failed.Contains(LanguagePack.Fallback))
                {
                    StartPass(LanguagePack.Fallback);                         // English's fonts, then
                }
                else if (pass is null)
                {
                    SDL3.SDL.SDL_ShowSimpleMessageBox(SDL3.SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "OpenMagicka",
                        "The menu could not be loaded from the game's files; the log says why.", Window.Handle);
                    ExitCode = 1;
                    RequestExit();
                }
            }
        }
        if (passLoading is null && language != passLanguage && !failed.Contains(language))
        {
            StartPass(language);
        }
        return pass;
    }

    private void StartPass(string language)
    {
        passLanguage = language;
        var (shaders, loader, gpu) = (view!.Shaders, content.Loader, GpuWork);
        passLoading = Task.Run(() => MenuPass.Load(shaders, gpu, loader, language));
    }

    /// <summary>The window or the screen as asked: a size (none: 1280 × 720 in a window, the desktop's on the screen), and the sync.</summary>
    private (int Width, int Height) SetScreen(ScreenMode screen)
    {
        var desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        var (width, height) = screen is { Width: { } w, Height: { } h } ? (w, h)
            : screen.Fullscreen ? (desktop.Width, desktop.Height) : (DefaultWidth, DefaultHeight);
        Graphics.PreferredBackBufferWidth = width;
        Graphics.PreferredBackBufferHeight = height;
        Graphics.IsFullScreen = screen.Fullscreen;
        Graphics.SynchronizeWithVerticalRetrace = screen.VSync;
        if (deviceMade)
        {
            Graphics.ApplyChanges();
        }
        return (width, height);
    }
}
