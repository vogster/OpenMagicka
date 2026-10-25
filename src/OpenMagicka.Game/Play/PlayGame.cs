using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The game playing a level (OM-75): the logic thread runs it (<see cref="PlaySimulation"/>), this thread
/// draws its scene with the original pipeline through the stage's camera, with the entities' models (as of
/// the last tick), under the screen's cover. A
/// scene's model loads in the background when the level goes to it. With a screenshot path it saves
/// a frame once the scene is shown and exits (exit code 1 when that fails).
/// </summary>
public sealed class PlayGame : ThreadedGame<PlayFrame>
{
    private const int ScreenshotAfterFrames = 30;
    private static readonly Color Sky = new(40, 44, 60);

    private readonly ContentLoader loader;
    private readonly LevelPlay play;
    private readonly string levelName;
    private readonly string? screenshot;
    private ShaderLibrary? shaders;
    private Task<DeferredRenderer>? deferredLoading;
    private DeferredRenderer? deferred;
    private SpriteBatch? sprites;
    private Texture2D? white;
    private GpuLevel? level;
    private string? shownModel;
    private string? loadingModel;
    private Task<GpuLevel>? loading;
    private EffectStage? effects;
    private ModelCache? models;
    private readonly HashSet<string> reportedEffects = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private double lastSeconds;
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
        sprites = new SpriteBatch(GraphicsDevice);
        white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData([Color.White]);
        shaders = new ShaderLibrary(new ShaderSource(loader, GraphicsDriver.HasOwnFna3d()), GpuWork);
        var library = shaders;
        deferredLoading = Task.Run(() => DeferredRenderer.Create(library, GpuWork, loader));
        models = new ModelCache(loader, GpuWork);
        effects = new EffectStage(Task.Run(() => OpenMagicka.Content.Particles.EffectLibrary.Load(loader.Files)), play.Channel);
    }

    protected override void UnloadContent()
    {
        level?.Dispose();
        models?.Dispose();
        if (loading is { IsCompletedSuccessfully: true } finished)
        {
            finished.Result.Dispose();
        }
        deferred?.Dispose();
        if (deferred is null && deferredLoading is { IsCompletedSuccessfully: true } built)
        {
            built.Result.Dispose();
        }
        shaders?.Dispose();
        white?.Dispose();
        sprites?.Dispose();
        base.UnloadContent();
    }

    protected override void Render(PlayFrame? frame, TimeSpan now)
    {
        GraphicsDevice.Clear(Sky);
        if (frame is null)
        {
            return;
        }
        Switch(frame.Model, frame.Settings?.SkyMap);
        var renderer = Deferred();
        var drawn = 0;
        if (level is not null && renderer is not null)
        {
            var alpha = (float)frame.Timing.Alpha(now);
            var aspect = Math.Max(GraphicsDevice.Viewport.AspectRatio, 0.1f);
            StepParticles();
            drawn = renderer.Render(GraphicsDevice, level, Frame(frame, alpha, aspect));
        }
        DrawCover(frame.Cover);
        SetTitle($"OpenMagicka - {levelName}" + (frame.SceneName is { } scene ? $" / {scene}" : "") + (level is null ? " (loading)" : $", {drawn} batches, {frame.Models.Count} models, {effects?.Running ?? 0} effects")
            + (frame.Error is { } error ? $" - stopped: {error}" : ""));
        if (screenshot is not null && level is not null && renderer is not null && effects is { IsReady: true } && ++framesShown == ScreenshotAfterFrames)
        {
            ExitCode = Screenshots.Save(GraphicsDevice, screenshot) && frame.Error is null ? 0 : 1;
            Console.WriteLine($"{title}; saved {screenshot}");
            RequestExit();
        }
    }

    private DeferredFrame Frame(PlayFrame frame, float alpha, float aspect)
    {
        var (view, projection, eye) = ViewerProjection.Game(frame.Previous, frame.Current, alpha, aspect);
        var time = (float)(clock.Elapsed.TotalSeconds % 1);
        var beholder = eye + GameCamera.Direction * GameCamera.EyeOfTheBeholderDistance;
        var globals = new GlobalEffectValues(view, projection, eye, beholder, time);
        var ground = Vector3.Lerp(frame.Previous.Ground, frame.Current.Ground, alpha);
        return new DeferredFrame(globals, level!.Sun, SceneLighting.Fog(frame.Settings), new Vector3(Sky.R, Sky.G, Sky.B) / 255f, ground,
            (float)(clock.Elapsed.TotalSeconds % 3600), effects?.Pool, PostProcessing.For(frame.Settings), models?.Instances(frame.Models),
            CharacterRimLight.For(frame.Settings));
    }

    /// <summary>The screen's cover (a fade, a scene change) over everything.</summary>
    private void DrawCover(ScreenCover cover)
    {
        if (cover.Amount <= 0 || sprites is null || white is null)
        {
            return;
        }
        var color = new Color(cover.Color.X, cover.Color.Y, cover.Color.Z) * Math.Clamp(cover.Amount, 0, 1);
        var viewport = GraphicsDevice.Viewport;
        sprites.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend);
        sprites.Draw(white, new Rectangle(0, 0, viewport.Width, viewport.Height), color);
        sprites.End();
    }

    private DeferredRenderer? Deferred()
    {
        if (deferred is null && deferredLoading is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully)
            {
                deferred = done.Result;
            }
            else
            {
                Console.Error.WriteLine($"OpenMagicka: the deferred pipeline is not available: {done.Exception?.GetBaseException().Message ?? "cancelled"}");
                Fail();
            }
            deferredLoading = null;
        }
        return deferred;
    }

    /// <summary>The effects the level passed on, stepped with the frame time (a screenshot's by a fixed frame, so a scene gives the same picture).</summary>
    private void StepParticles()
    {
        var seconds = clock.Elapsed.TotalSeconds;
        var dt = screenshot is not null ? 1 / 60f : (float)(seconds - lastSeconds);
        lastSeconds = seconds;
        effects?.Step(dt);
        if (effects is { Error: { } error } && reportedEffects.Add(error))
        {
            Console.Error.WriteLine($"OpenMagicka: no particles: the effects did not load: {error}");
        }
        foreach (var name in effects?.Missing.Where(reportedEffects.Add).ToList() ?? [])
        {
            Console.Error.WriteLine($"OpenMagicka: no visual effect named {name}");
        }
    }

    /// <summary>Starts loading the scene's model when the level is in another, and shows it once it is on the GPU.</summary>
    private void Switch(string? model, SkyMapSettings? sky)
    {
        if (loading is { IsCompleted: true } done)
        {
            loading = null;
            level?.Dispose();
            level = null;
            shownModel = loadingModel;
            if (done.IsCompletedSuccessfully)
            {
                level = done.Result;
                foreach (var warning in level.Warnings)
                {
                    Console.Error.WriteLine($"OpenMagicka: skipped: {warning}");
                }
            }
            else
            {
                Console.Error.WriteLine($"OpenMagicka: {loadingModel}: {done.Exception?.GetBaseException().Message ?? "cancelled"}");
                Fail();
            }
        }
        if (loading is null && model is not null && !string.Equals(model, shownModel, StringComparison.OrdinalIgnoreCase))
        {
            loadingModel = model;
            loading = Task.Run(() => GpuLevel.Load(loader, model, GpuWork, sky));
        }
    }

    private void Fail()
    {
        if (screenshot is not null)
        {
            ExitCode = 1;
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
