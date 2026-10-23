using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game.Viewer;

/// <summary>
/// The level viewer (OM-22): flies a free camera through every scene of the game's levels,
/// drawing their static geometry. Scenes load in the background; the window title says which
/// scene is shown and how many batches are drawn. With a screenshot path it saves the first
/// scene's frame and exits (exit code 1 when that fails).
/// </summary>
public sealed class ViewerGame : ThreadedGame<ViewerFrame>
{
    private const int ScreenshotAfterFrames = 10;
    private static readonly Color Sky = new(40, 44, 60);

    private readonly ContentLoader loader;
    private readonly SceneCatalog catalog;
    private readonly int startScene;
    private readonly string? screenshot;
    private readonly bool gameCamera;
    private ViewerSimulation? simulation;
    private BasicEffect? effect;
    private ShaderLibrary? shaders;
    private Task<DeferredRenderer>? deferredLoading;
    private DeferredRenderer? deferred;
    private bool useDeferred = true;
    private bool rWasDown;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private GpuLevel? level;
    private int shownScene = -1;
    private int loadingScene = -1;
    private Task<GpuLevel>? loading;
    private string status = "";
    private string title = "";
    private int framesShown;
    private ViewerParticles? particles;
    private bool particlesStarted;
    private double lastSeconds;

    /// <param name="scene">The first scene's name (the first scene when null or unknown).</param>
    /// <param name="screenshot">Save the first scene's frame there and exit.</param>
    /// <param name="gameCamera">Start with the game's camera instead of the free one.</param>
    public ViewerGame(SceneCatalog catalog, ContentLoader loader, string? scene, string? screenshot = null, bool gameCamera = false)
        : base(() => new ViewerFrame())
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loader);
        this.catalog = catalog;
        this.loader = loader;
        this.screenshot = screenshot;
        this.gameCamera = gameCamera;
        startScene = Math.Max(catalog.Find(scene), 0);
        Window.Title = "OpenMagicka viewer";
    }

    /// <summary>0, or 1 when a requested screenshot could not be taken.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The catalog and loader for a content folder; scenes that fail to read are reported on stderr.</summary>
    public static (SceneCatalog Catalog, ContentLoader Loader) Open(string contentDirectory)
    {
        var files = new ContentFileSystem(contentDirectory);
        var catalog = SceneCatalog.Load(files);
        foreach (var problem in catalog.Problems)
        {
            Console.Error.WriteLine($"OpenMagicka: skipped: {problem}");
        }
        return (catalog, new ContentLoader(files, XnbReaderRegistry.Magicka));
    }

    protected override ISimulation<ViewerFrame> CreateSimulation() =>
        simulation = new ViewerSimulation(this, catalog.Items.Count, startScene, catalog.SectionStarts, gameCamera);

    protected override void LoadContent()
    {
        base.LoadContent();
        effect = new BasicEffect(GraphicsDevice);
        effect.EnableDefaultLighting();
        shaders = new ShaderLibrary(new ShaderSource(loader), GpuWork);
        var library = shaders;
        deferredLoading = Task.Run(() => DeferredRenderer.Create(library, GpuWork, loader));
        particles = new ViewerParticles(Task.Run(() => OpenMagicka.Content.Particles.EffectLibrary.Load(loader.Files)));
        if (catalog.Items.Count == 0)
        {
            Fail("no scenes or models found in the content");
        }
    }

    protected override void UnloadContent()
    {
        level?.Dispose();
        if (loading is { IsCompletedSuccessfully: true } finished)
        {
            finished.Result.Dispose();   // loaded but never shown
        }
        effect?.Dispose();
        deferred?.Dispose();
        if (deferred is null && deferredLoading is { IsCompletedSuccessfully: true } built)
        {
            built.Result.Dispose();
        }
        shaders?.Dispose();
        base.UnloadContent();
    }

    protected override void Render(ViewerFrame? frame, TimeSpan now)
    {
        GraphicsDevice.Clear(Sky);
        if (frame is null || catalog.Items.Count == 0 || effect is null)
        {
            return;
        }
        Switch(frame.Scene);
        var drawn = 0;
        if (level is not null)
        {
            var alpha = (float)frame.Timing.Alpha(now);
            var aspect = Math.Max(GraphicsDevice.Viewport.AspectRatio, 0.1f);
            var renderer = Deferred() is { } built && useDeferred ? built : null;
            var view = View(frame, alpha, aspect, renderer is not null);
            StepParticles();
            drawn = renderer is not null
                ? renderer.Render(GraphicsDevice, level, Frame(view))
                : level.Draw(GraphicsDevice, effect, view.View, view.Projection);
        }
        if (shownScene >= 0)
        {
            var scene = catalog.Items[shownScene];
            SetTitle($"OpenMagicka viewer - {scene.Level} / {scene.Scene} ({shownScene + 1} of {catalog.Items.Count})"
                + (deferred is not null && useDeferred ? " [original shaders]" : " [simple view]")
                + (frame.CurrentGame is null ? "" : " [game camera]")
                + (level is null ? "" : $", {drawn} of {level.BatchCount} batches")
                + (particles is null || !particlesStarted ? "" : $", {particles.Pool.LastActive + 1} particles") + status);
        }
        // A few frames in, the camera has framed the scene: the shot shows it.
        if (screenshot is not null && level is not null && deferredLoading is null && particlesStarted && ++framesShown == ScreenshotAfterFrames)
        {
            SaveScreenshot(screenshot);
            Console.WriteLine($"{title}; saved {screenshot}");
            RequestExit();
        }
    }

    /// <summary>The original pipeline once its effects are on the GPU; R toggles it with the simple view.</summary>
    private DeferredRenderer? Deferred()
    {
        var rDown = Input.Value.IsActive && Input.Value.Keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.R);
        if (rDown && !rWasDown)
        {
            useDeferred = !useDeferred;
        }
        rWasDown = rDown;
        if (deferred is null && deferredLoading is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully)
            {
                deferred = done.Result;
                if (deferred.ParticlesUnavailable is { } reason)
                {
                    Console.Error.WriteLine($"OpenMagicka: no particles: {reason}");
                }
                if (deferred.PostUnavailable is { } postReason)
                {
                    Console.Error.WriteLine($"OpenMagicka: no filter or bloom: {postReason}");
                }
            }
            else
            {
                Console.Error.WriteLine($"OpenMagicka: the deferred pipeline is not available: {done.Exception?.GetBaseException().Message ?? "cancelled"}");
                useDeferred = false;
            }
            deferredLoading = null;
        }
        return deferred;
    }

    /// <summary>The game camera's view when it is on, otherwise the free camera's.</summary>
    private ViewerView View(ViewerFrame frame, float alpha, float aspect, bool originalPipeline)
    {
        if (frame.CurrentGame is { } current)
        {
            var (gameView, gameProjection, eye) = ViewerProjection.Game(frame.PreviousGame ?? current, current, alpha, aspect);
            return new ViewerView(gameView, gameProjection, eye, GameCamera.Direction, eye + GameCamera.Direction * GameCamera.EyeOfTheBeholderDistance);
        }
        var camera = FreeCamera.Lerp(frame.Previous, frame.Current, alpha);
        var (near, far) = ViewerProjection.ClipPlanes((level!.Bounds.Max - level.Bounds.Min).Length(), originalPipeline);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), aspect, near, far);
        return new ViewerView(camera.View, projection, camera.Position, Vector3.Normalize(camera.Forward), camera.Position);
    }

    private DeferredFrame Frame(ViewerView view)
    {
        var scene = catalog.Items[shownScene];
        var time = (float)(clock.Elapsed.TotalSeconds % 1);
        var globals = new GlobalEffectValues(view.View, view.Projection, view.Position, view.EyeOfTheBeholder, time);
        var focus = SunShadowView.Focus(view.Position, view.Forward, ViewerSimulation.GroundHeight(level!.Bounds));
        return new DeferredFrame(globals, level.Sun, SceneLighting.Fog(scene.Settings), new Vector3(Sky.R, Sky.G, Sky.B) / 255f, focus,
            (float)(clock.Elapsed.TotalSeconds % 3600),   // the liquids' time, wrapped to keep float precision
            particles?.Pool, PostProcessing.For(scene.Settings));
    }

    /// <summary>Starts the shown level's effects once the effect library is in, then steps them with the frame time.</summary>
    private void StepParticles()
    {
        var seconds = clock.Elapsed.TotalSeconds;
        // A screenshot steps by a fixed frame, so the same scene gives the same picture.
        var dt = screenshot is not null ? 1 / 60f : (float)(seconds - lastSeconds);
        lastSeconds = seconds;
        if (particles is null || level is null)
        {
            return;
        }
        if (!particlesStarted)
        {
            particlesStarted = particles.Start(level.Effects);
            if (particlesStarted && particles.Error is { } error)
            {
                Console.Error.WriteLine($"OpenMagicka: no particles: the effects did not load: {error}");
            }
            foreach (var name in particles.Missing.Distinct())
            {
                Console.Error.WriteLine($"OpenMagicka: no visual effect named {name}");
            }
            return;
        }
        particles.Step(dt);
    }

    /// <summary>Starts loading the wanted scene, and shows it once it is on the GPU (render thread).</summary>
    private void Switch(int wanted)
    {
        if (loading is { IsCompleted: true } done)
        {
            loading = null;
            level?.Dispose();
            level = null;
            shownScene = loadingScene;
            if (done.IsCompletedSuccessfully)
            {
                level = done.Result;
                particlesStarted = false;
                status = level.Warnings.Count == 0 ? "" : $" ({level.Warnings.Count} warnings)";
                simulation?.Loaded.Value = new LoadedScene(shownScene, level.Bounds)
                {
                    Mesh = level.CameraMesh, Magnify = catalog.Items[shownScene].Settings?.Magnify,
                };
                foreach (var warning in level.Warnings)
                {
                    Console.Error.WriteLine($"OpenMagicka: skipped: {warning}");
                }
            }
            else
            {
                var reason = done.Exception?.GetBaseException().Message ?? "cancelled";
                status = $" - failed: {reason}";
                Console.Error.WriteLine($"OpenMagicka: {catalog.Items[shownScene].Model}: {reason}");
                Fail($"the scene did not load: {reason}");
            }
        }
        if (loading is null && wanted != shownScene)
        {
            loadingScene = wanted;
            var (model, sky) = (catalog.Items[wanted].Model, catalog.Items[wanted].Settings?.SkyMap);
            loading = Task.Run(() => GpuLevel.Load(loader, model, GpuWork, sky));
        }
    }

    /// <summary>In screenshot mode a failure ends the run with exit code 1; interactively it only shows in the title.</summary>
    private void Fail(string reason)
    {
        if (screenshot is not null)
        {
            Console.Error.WriteLine($"OpenMagicka: no screenshot: {reason}");
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

    /// <summary>Saves the back buffer as an opaque PNG (textures with alpha leave alpha below 1 in it).</summary>
    private void SaveScreenshot(string path)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = 255;
        }
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        try
        {
            using var file = File.Create(path);
            texture.SaveAsPng(file, width, height);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: cannot save {path}: {e.Message}");
            ExitCode = 1;
        }
    }
}

/// <summary>What one frame is drawn from: the matrices, the eye, its direction and the effects' eye of the beholder.</summary>
internal readonly record struct ViewerView(Matrix View, Matrix Projection, Vector3 Position, Vector3 Forward, Vector3 EyeOfTheBeholder);
