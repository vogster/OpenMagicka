using OpenMagicka.Content;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Play;

/// <summary>
/// One level played on its own (OM-75, the <c>--level</c> option): the logic thread runs it (<see cref="PlaySimulation"/>),
/// this thread draws it (<see cref="PlayView"/>). With a screenshot path it saves a frame once the scene is shown and
/// exits (exit code 1 when that fails).
/// </summary>
public sealed class PlayGame : ThreadedGame<PlayFrame>
{
    private const int ScreenshotAfterFrames = 30;

    private readonly ContentLoader loader;
    private readonly LevelPlay play;
    private readonly string levelName;
    private readonly string? screenshot;
    private PlayView? view;
    private int framesShown;
    private string title = "";

    public PlayGame(ContentLoader loader, LevelPlay play, string levelName, string? screenshot = null)
        : base(() => new PlayFrame())
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.play = play ?? throw new ArgumentNullException(nameof(play));
        this.levelName = levelName ?? throw new ArgumentNullException(nameof(levelName));
        this.screenshot = screenshot;
        Window.Title = "OpenMagicka";
        IsMouseVisible = true;                                                // the cursor aims (the original's own cursors: OM-56)
    }

    /// <summary>0, or 1 when the level could not be shown for a requested screenshot.</summary>
    public int ExitCode { get; private set; }

    /// <summary>A level of the content by its file name (any case, with or without .lvl) or its path; null when there is none.</summary>
    public static string? FindLevel(ContentFileSystem files, string name)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(name);
        var wanted = ContentFileSystem.Normalize(name);
        return files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(p => p.Equals(wanted, StringComparison.OrdinalIgnoreCase) || p.Equals(wanted + ".lvl", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(wanted), StringComparison.OrdinalIgnoreCase));
    }

    protected override Engine.Threading.ISimulation<PlayFrame> CreateSimulation() => new PlaySimulation(this, play);

    protected override void LoadContent()
    {
        base.LoadContent();
        view = new PlayView(GraphicsDevice, loader, GpuWork);
        view.Begin(play.Channel);
    }

    protected override void UnloadContent()
    {
        view?.Dispose();
        base.UnloadContent();
    }

    protected override void Render(PlayFrame? frame, TimeSpan now)
    {
        if (frame is null || view is null)
        {
            GraphicsDevice.Clear(PlayView.Sky);
            return;
        }
        view.Render(GraphicsDevice, frame, now, fixedStep: screenshot is not null);
        if (view.Failure is not null && screenshot is not null)
        {
            ExitCode = 1;
            RequestExit();
        }
        SetTitle($"OpenMagicka - {levelName}" + (frame.SceneName is { } scene ? $" / {scene}" : "")
            + (!view.IsShowing ? " (loading)" : $", {view.Batches} batches, {frame.Models.Count} models, {view.Effects} effects")
            + (frame.Error is { } error ? $" - stopped: {error}" : ""));
        if (screenshot is not null && view.IsShowing && view.EffectsReady && ++framesShown == ScreenshotAfterFrames)
        {
            ExitCode = Screenshots.Save(GraphicsDevice, screenshot) && frame.Error is null ? 0 : 1;
            Console.WriteLine($"{title}; saved {screenshot}");
            RequestExit();
        }
    }

    private void SetTitle(string text)
    {
        if (text != title)
        {
            title = text;
            Window.Title = text;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            play.Dispose();
        }
    }
}
