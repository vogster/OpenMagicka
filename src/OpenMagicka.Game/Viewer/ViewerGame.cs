using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine;
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
    private ViewerSimulation? simulation;
    private BasicEffect? effect;
    private GpuLevel? level;
    private int shownScene = -1;
    private int loadingScene = -1;
    private Task<GpuLevel>? loading;
    private string status = "";
    private string title = "";
    private int framesShown;

    /// <param name="scene">The first scene's name (the first scene when null or unknown).</param>
    /// <param name="screenshot">Save the first scene's frame there and exit.</param>
    public ViewerGame(SceneCatalog catalog, ContentLoader loader, string? scene, string? screenshot = null)
        : base(() => new ViewerFrame())
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loader);
        this.catalog = catalog;
        this.loader = loader;
        this.screenshot = screenshot;
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
            Console.Error.WriteLine($"OpenMagicka: scene skipped: {problem}");
        }
        return (catalog, new ContentLoader(files, XnbReaderRegistry.Magicka));
    }

    protected override ISimulation<ViewerFrame> CreateSimulation() =>
        simulation = new ViewerSimulation(this, catalog.Scenes.Count, startScene);

    protected override void LoadContent()
    {
        base.LoadContent();
        effect = new BasicEffect(GraphicsDevice);
        effect.EnableDefaultLighting();
        if (catalog.Scenes.Count == 0)
        {
            Fail("no scenes found in the content");
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
        base.UnloadContent();
    }

    protected override void Render(ViewerFrame? frame, TimeSpan now)
    {
        GraphicsDevice.Clear(Sky);
        if (frame is null || catalog.Scenes.Count == 0 || effect is null)
        {
            return;
        }
        Switch(frame.Scene);
        var drawn = 0;
        if (level is not null)
        {
            var camera = FreeCamera.Lerp(frame.Previous, frame.Current, (float)frame.Timing.Alpha(now));
            var aspect = Math.Max(GraphicsDevice.Viewport.AspectRatio, 0.1f);
            var far = Math.Max((level.Bounds.Max - level.Bounds.Min).Length() * 3, 1000);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), aspect, Math.Max(far / 20000, 0.5f), far);
            drawn = level.Draw(GraphicsDevice, effect, camera.View, projection);
        }
        if (shownScene >= 0)
        {
            var scene = catalog.Scenes[shownScene];
            SetTitle($"OpenMagicka viewer - {scene.Level} / {scene.Scene} ({shownScene + 1} of {catalog.Scenes.Count})"
                + (level is null ? "" : $", {drawn} of {level.BatchCount} batches") + status);
        }
        // A few frames in, the camera has framed the scene: the shot shows it.
        if (screenshot is not null && level is not null && ++framesShown == ScreenshotAfterFrames)
        {
            SaveScreenshot(screenshot);
            Console.WriteLine($"{title}; saved {screenshot}");
            RequestExit();
        }
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
                status = level.Warnings.Count == 0 ? "" : $" ({level.Warnings.Count} textures missing)";
                simulation?.LevelBounds.Value = (shownScene, level.Bounds);
                foreach (var warning in level.Warnings)
                {
                    Console.Error.WriteLine($"OpenMagicka: texture skipped: {warning}");
                }
            }
            else
            {
                var reason = done.Exception?.GetBaseException().Message ?? "cancelled";
                status = $" - failed: {reason}";
                Console.Error.WriteLine($"OpenMagicka: {catalog.Scenes[shownScene].Model}: {reason}");
                Fail($"the scene did not load: {reason}");
            }
        }
        if (loading is null && wanted != shownScene)
        {
            loadingScene = wanted;
            var model = catalog.Scenes[wanted].Model;
            loading = Task.Run(() => GpuLevel.Load(loader, model, GpuWork));
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
