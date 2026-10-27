using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Particles;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Game.Play;

/// <summary>
/// A level drawn (OM-75, OM-53), on the render thread: the original pipeline through the stage's camera, the entities'
/// models (as of the last tick), the screen's cover. The pipeline, the models and the effects' library are made once
/// and kept from level to level; a scene's model loads in the background when the level goes to it.
/// </summary>
public sealed class PlayView : IDisposable
{
    /// <summary>What shows where the level draws nothing: black, as the original clears its frame (the viewer keeps its blue).</summary>
    public static readonly Color Sky = Color.Black;

    private readonly ContentLoader loader;
    private readonly RenderQueue<GraphicsDevice> gpu;
    private readonly ShaderLibrary shaders;
    private readonly Task<EffectLibrary> library;
    private readonly ModelCache models;
    private readonly SpriteBatch sprites;
    private readonly Texture2D white;
    private readonly List<Matrix> parts = [];
    private readonly HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private Task<DeferredRenderer>? deferredLoading;
    private DeferredRenderer? deferred;
    private GpuLevel? level;
    private string? shownModel, loadingModel;
    private Task<GpuLevel>? loading;
    private EffectStage? effects;
    private double lastSeconds;
    private string language = Content.Localization.LanguagePack.Fallback;
    private Task<(string Language, Dictionary<string, (Content.PolygonHead.BitmapFontContent Font, Content.Xna.Texture2DContent Atlas)> Fonts)>? fontsLoading;

    public PlayView(GraphicsDevice device, ContentLoader loader, RenderQueue<GraphicsDevice> gpu)
    {
        ArgumentNullException.ThrowIfNull(device);
        (this.loader, this.gpu) = (loader ?? throw new ArgumentNullException(nameof(loader)), gpu ?? throw new ArgumentNullException(nameof(gpu)));
        sprites = new SpriteBatch(device);
        white = new Texture2D(device, 1, 1);
        white.SetData([Color.White]);
        shaders = new ShaderLibrary(new ShaderSource(loader, GraphicsDriver.HasOwnFna3d()), gpu);
        var made = shaders;
        deferredLoading = Task.Run(() => DeferredRenderer.Create(made, gpu, loader));
        models = new ModelCache(loader, gpu);
        library = Task.Run(() => EffectLibrary.Load(loader.Files));
    }

    /// <summary>Why the pipeline or a scene could not be drawn (the screen then shows the sky); none.</summary>
    public string? Failure { get; private set; }

    /// <summary>The scene's model is shown (the screen has more than the sky).</summary>
    public bool IsShowing => level is not null && deferred is not null;

    /// <summary>The level's effects are ready (a screenshot waits for them).</summary>
    public bool EffectsReady => effects is { IsReady: true };

    public int Batches { get; private set; }

    public int Effects => effects?.Running ?? 0;

    /// <summary>The GUI pass of the pipeline, once it is made (the menu draws with it too); none before or without it.</summary>
    public GuiPass? Gui => Deferred()?.Gui;

    /// <summary>The language the screen's text (speech boxes, the HUD's words) is drawn in; its fonts load in the background.</summary>
    public string Language
    {
        get => language;
        set => language = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The GUI pass takes the language's fonts once they are read (the old ones until then).</summary>
    private void FollowLanguage(GraphicsDevice device, GuiPass gui)
    {
        if (fontsLoading is { IsCompleted: true } done)
        {
            fontsLoading = null;
            if (done.IsCompletedSuccessfully)
            {
                gui.SetFonts(device, done.Result.Language, done.Result.Fonts);
            }
            else
            {
                Console.Error.WriteLine($"OpenMagicka: no {language} fonts for the screen's text: {done.Exception?.GetBaseException().Message}");
                language = gui.Language;                                      // not tried again
            }
        }
        if (fontsLoading is null && gui.Language != language)
        {
            var (wanted, from) = (language, loader);
            fontsLoading = Task.Run(() => (wanted, GuiPass.ReadFonts(from, wanted)));
        }
    }

    /// <summary>The shaders the pipeline is made with (the menu's pass takes its effect from them).</summary>
    public ShaderLibrary Shaders => shaders;

    /// <summary>The serial of the level shown (the game's count of levels begun); 0 with none.</summary>
    public int Level { get; private set; }

    /// <summary>A level begins: its effects come from its channel; the scene shown before goes.</summary>
    public void Begin(EffectChannel channel, int serial = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);
        End();
        effects = new EffectStage(library, channel);
        Level = serial;
    }

    /// <summary>The level ends: its scene and effects go (the pipeline and the models stay).</summary>
    public void End()
    {
        if (loading is { } pending)
        {
            loading = null;
            _ = pending.ContinueWith(t => { if (t.IsCompletedSuccessfully) { _ = gpu.Enqueue(_ => t.Result.Dispose()); } }, TaskScheduler.Default);
        }
        level?.Dispose();
        (level, shownModel, loadingModel, effects, Level) = (null, null, null, null, 0);
        Failure = deferred is null && deferredLoading is null ? Failure : null; // a scene's failure is the level's; the pipeline's stays
    }

    /// <summary>A frame of the level, interpolated to now; with a fixed step when <paramref name="fixedStep"/> (a screenshot's).</summary>
    public void Render(GraphicsDevice device, PlayFrame frame, TimeSpan now, bool fixedStep = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(frame);
        device.Clear(Sky);
        Switch(frame.Model, frame.Settings?.SkyMap);
        Batches = 0;
        if (Deferred() is { Gui: { } gui })
        {
            FollowLanguage(device, gui);
        }
        if (level is not null && Deferred() is { } renderer)
        {
            var alpha = (float)frame.Timing.Alpha(now);
            var aspect = Math.Max(device.Viewport.AspectRatio, 0.1f);
            StepParticles(fixedStep);
            Batches = renderer.Render(device, level, Frame(frame, alpha, aspect));
        }
        if (level is null || Deferred() is not { DrawsTransitions: true })
        {
            DrawCover(device, frame.Cover);                                   // no pipeline to draw it in: a plain cover over the picture
        }
    }

    private DeferredFrame Frame(PlayFrame frame, float alpha, float aspect)
    {
        var (view, projection, eye) = ViewerProjection.Game(frame.Previous, frame.Current, alpha, aspect);
        var time = (float)(clock.Elapsed.TotalSeconds % 1);
        var beholder = eye + GameCamera.Direction * GameCamera.EyeOfTheBeholderDistance;
        var globals = new GlobalEffectValues(view, projection, eye, beholder, time);
        var ground = Vector3.Lerp(frame.Previous.Ground, frame.Current.Ground, alpha);
        var placed = Parts(frame);
        return new DeferredFrame(globals, level!.Sun, SceneLighting.Fog(frame.Settings), new Vector3(Sky.R, Sky.G, Sky.B) / 255f, ground,
            (float)(clock.Elapsed.TotalSeconds % 3600), effects?.Pool, PostProcessing.For(frame.Settings), models.Instances(frame.Models),
            CharacterRimLight.For(frame.Settings), placed, frame.Spells, models.Get, frame.Gui,
            placed is not null && frame.Highlighted.Count == frame.Parts.Count ? frame.Highlighted : null,   // the glow only with the parts it lines up with
            frame.Transition);
    }

    /// <summary>The level's parts where the frame has them, when the frame is of the level shown (none: at rest).</summary>
    private IReadOnlyList<Matrix>? Parts(PlayFrame frame)
    {
        if (level is null || !string.Equals(frame.Model, shownModel, StringComparison.OrdinalIgnoreCase) || frame.Parts.Count != level.PartUnrests.Count)
        {
            return null;
        }
        parts.Clear();
        parts.AddRange(frame.Parts.Select(ModelCache.Xna));
        return parts;
    }

    /// <summary>The screen's cover (a fade, a scene change) over everything.</summary>
    public void DrawCover(GraphicsDevice device, ScreenCover cover)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (cover.Amount <= 0)
        {
            return;
        }
        var color = new Color(cover.Color.X, cover.Color.Y, cover.Color.Z) * Math.Clamp(cover.Amount, 0, 1);
        var viewport = device.Viewport;
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
                Failure = $"the deferred pipeline is not available: {done.Exception?.GetBaseException().Message ?? "cancelled"}";
                Console.Error.WriteLine($"OpenMagicka: {Failure}");
            }
            deferredLoading = null;
        }
        return deferred;
    }

    /// <summary>The effects the level passed on, stepped with the frame time (a screenshot's by a fixed frame, so a scene gives the same picture).</summary>
    private void StepParticles(bool fixedStep)
    {
        var seconds = clock.Elapsed.TotalSeconds;
        var dt = fixedStep ? 1 / 60f : (float)(seconds - lastSeconds);
        lastSeconds = seconds;
        effects?.Step(dt);
        if (effects is { Error: { } error } && reported.Add(error))
        {
            Console.Error.WriteLine($"OpenMagicka: no particles: the effects did not load: {error}");
        }
        foreach (var name in effects?.Missing.Where(reported.Add).ToList() ?? [])
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
                Failure = $"{loadingModel}: {done.Exception?.GetBaseException().Message ?? "cancelled"}";
                Console.Error.WriteLine($"OpenMagicka: {Failure}");
            }
        }
        if (loading is null && model is not null && !string.Equals(model, shownModel, StringComparison.OrdinalIgnoreCase))
        {
            loadingModel = model;
            loading = Task.Run(() => GpuLevel.Load(loader, model, gpu, sky));
        }
    }

    public void Dispose()
    {
        End();
        models.Dispose();
        deferred?.Dispose();
        if (deferred is null && deferredLoading is { IsCompletedSuccessfully: true } built)
        {
            built.Result.Dispose();
        }
        shaders.Dispose();
        white.Dispose();
        sprites.Dispose();
    }
}
