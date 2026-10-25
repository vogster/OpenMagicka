using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>What the screen is covered with this tick: a colour and how much of it (0: none).</summary>
public readonly record struct ScreenCover(Numerics.Vector4 Color, float Amount);

/// <summary>
/// A level at play in the game (OM-75), on the logic thread: the level, its world and its one player,
/// whose wizard the player's controls drive (OM-31); each tick the level runs, then the
/// stage's camera follows the players (or the followed entity) on the scene's camera mesh, the effects
/// are passed on (<see cref="EffectDirector"/>), and the screen's fades and scene transitions are stepped
/// into a cover.
/// </summary>
public sealed partial class LevelPlay : IDisposable
{
    public const string WizardAsset = "data/characters/wizard";

    private readonly Dictionary<int, CameraMesh?> meshes = [];
    private ScreenFade? fade;
    private SceneTransition? transition;
    private float fadeAmount, fadeFrom, fadeTime, transitionAmount;
    private int meshScene = -1;

    /// <summary>Plays a level of the game's content (a .lvl path), its first player a wizard.</summary>
    public static LevelPlay Open(LevelDefinition definition, ContentFileSystem files, ContentLoader loader)
    {
        var library = new TemplateLibrary(loader);
        var bestiary = new ContentBestiary(library);
        var services = new LevelServices(bestiary, new ContentScriptAssets(library), ScriptRegistry.Engine());
        var wizard = bestiary.Find(WizardAsset) ?? throw new InvalidDataException($"the game has no {WizardAsset}");
        return new LevelPlay(definition, new ContentLevel(files, loader), services, wizard, library.LookOf, GibShapes(loader),
            AnimatedModels.Loader(loader, SpellVisuals.ModelAssets), LevelDialogs.Load(files, loader, definition, LanguagePack.Fallback));
    }

    /// <summary>A model's bounding sphere (its meshes' together), by asset, each read once; null for one that is no model.</summary>
    public static Func<string, (Numerics.Vector3 Centre, float Radius)?> GibShapes(ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var known = new Dictionary<string, (Numerics.Vector3, float)?>(StringComparer.OrdinalIgnoreCase);
        return asset =>
        {
            if (!known.TryGetValue(asset, out var shape))
            {
                shape = null;
                try
                {
                    if (loader.Files.Exists(asset + ".xnb") && loader.Load(asset).Root is Content.Xna.ModelContent { Meshes.Count: > 0 } model)
                    {
                        var sphere = model.Meshes.Select(m => m.Bounds).Aggregate(Microsoft.Xna.Framework.BoundingSphere.CreateMerged);
                        shape = sphere.Radius > 0 ? (new Numerics.Vector3(sphere.Center.X, sphere.Center.Y, sphere.Center.Z), sphere.Radius) : null;
                    }
                }
                catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or FormatException or ArgumentException)
                {
                    shape = null;                                             // a model that does not read is no piece
                }
                known[asset] = shape;
            }
            return shape;
        };
    }

    /// <param name="wizard">The first player's template and clips.</param>
    /// <param name="looks">What the templates look like (none: nothing is drawn as a model).</param>
    public LevelPlay(LevelDefinition definition, ILevelContent content, LevelServices services, (CharacterTemplate Template, Gameplay.Animation.CharacterAnimations? Animations) wizard,
        Func<object, Engine.Rendering.ModelLook?>? looks = null, Func<string, (Numerics.Vector3 Centre, float Radius)?>? gibShapes = null,
        Func<string, AnimatedModel?>? spellModels = null, LevelDialogs? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Looks = looks ?? (_ => null);
        Session = new GameSession();
        World = new GameWorld(session: Session);
        Player = Session.AddPlayer();
        Player.Avatar = new Character(wizard.Template, World.Physics, new Numerics.Vector3(0, 50, 0), animations: wizard.Animations) { IsPlayer = true, Player = Player };
        Level = new PlayedLevel(definition, content, World, services);
        Effects = new EffectDirector(World, Level, Channel, gibShapes ?? (_ => null));
        Spells = new SpellVisuals(World, Effects, spellModels, Looks);
        SetUpDialogs(dialogs);
        SetUpHud();
    }

    /// <summary>The effects passed to the renderer, and back.</summary>
    public EffectChannel Channel { get; } = new();

    public EffectDirector Effects { get; }

    /// <summary>The spells' own pictures (OM-76).</summary>
    public SpellVisuals Spells { get; }

    public GameSession Session { get; }

    /// <summary>What the templates look like (the frame's models are drawn by it).</summary>
    public Func<object, Engine.Rendering.ModelLook?> Looks { get; }

    public GameWorld World { get; }

    public Player Player { get; }

    public PlayedLevel Level { get; }

    public GameCamera Camera => Level.Level.Stage.Camera;

    /// <summary>The scene now, or null before the start.</summary>
    public LevelScene? Scene => Level.Current;

    public ScreenCover Cover { get; private set; }

    /// <summary>The level's requests for the systems beyond the world, since the frame last took them (logic thread).</summary>
    public List<ScriptRequest> Requests { get; } = [];

    public void Start()
    {
        Level.Start();
        Effects.Update();
        Spells.Update(0);
        StepCamera(0, snap: true);
    }

    /// <summary>The first player's keyboard-and-mouse controls (OM-31).</summary>
    public KeyboardMouseControl Controls { get; private set; } = new(new ControlBindings());

    /// <summary>The first player's pad controls (OM-31).</summary>
    public GamePadControl Pad { get; } = new();

    /// <summary>The pad's bindings (the original's defaults).</summary>
    public PadBindings PadBindings { get; } = new();

    /// <summary>The controls bound as given (the player's own file).</summary>
    public void Bind(ControlBindings bindings) => Controls = new KeyboardMouseControl(bindings);

    /// <summary>
    /// A tick: the player's controls act on its wizard (its pad when given, else its keyboard and mouse;
    /// neither: it is left as it is), the level runs,
    /// the camera follows, the cover is stepped. The cursor is read against the camera as it was over the
    /// window of that size.
    /// </summary>
    public void Tick(float dt, ControlFrame? input = null, Xna.Point screen = default, PadFrame? pad = null)
    {
        var talking = Talk(input, pad);                                       // the player drives a dialog: its wizard stands
        if (talking)
        {
            if (Player.Avatar is { IsDead: false } talker)
            {
                ControlTargets.LetGo(talker);                                 // it stands, its hands off
            }
            Controls.Reset();
            Pad.Reset();
        }
        else if (pad is { } padFrame && Scene?.Scripts is { } padScripts)
        {
            Pad.Update(padFrame, Player, padScripts);
        }
        else if (input is { } frame && Scene?.Scripts is { } scripts && Player.Avatar is { } avatar && screen.X > 0 && screen.Y > 0)
        {
            Controls.Update(frame, Player, scripts, Cursor(Camera, frame.Mouse.X, frame.Mouse.Y, screen, avatar.Position.Y), dt);
        }
        Level.Tick(dt);
        var changed = false;
        foreach (var request in Level.TakeRequests())
        {
            changed |= request is SceneChangedRequest;
            if (!Take(request) && !TakeSound(request))
            {
                Requests.Add(request);                                        // for the HUD (OM-54)
            }
        }
        if (changed)
        {
            Controls.Reset();
            Pad.Reset();
            Effects.Clear();
            Spells.Clear();
            meshScene = -1;                                                   // the camera stands over the players at once
        }
        Effects.Update();
        Spells.Update(dt);
        StepCutscene(dt, input, pad);
        StepDialogs(dt);
        StepPrompt(dt, input, pad is not null);
        StepHud(dt, pad is not null);
        StepCamera(dt, snap: false);
        StepSound(dt);
        StepCover(dt);
    }

    /// <summary>The camera's mesh and zoom are the scene's (taken when it changes); it follows the living players' centre, or the followed entity.</summary>
    private void StepCamera(float dt, bool snap)
    {
        var stage = Level.Level.Stage;
        if (Scene is { } scene && scene.Key != meshScene)
        {
            meshScene = scene.Key;
            var zoom = scene.Definition.Settings?.Magnify is > 0 and var m && float.IsFinite(m) ? m : 1;
            stage.Camera = (stage.Camera with { Mesh = Mesh(scene) }).WithDefaultMagnification(zoom);
            snap = true;
        }
        var players = (Session.Players).Where(p => p.Playing && p.Avatar is { IsDead: false, Manager: not null })
            .Select(p => Subject(p.Avatar!)).ToArray();
        var followed = stage.Followed is { Manager: not null } entity ? ToXna(entity.Position) : (Xna.Vector3?)null;
        var inputs = CameraInputs.None with
        {
            Players = CameraFocus.Players(players, [], stage.Camera.Ground, []),
            Followed = followed,
            LocalPlayers = [.. players.Select(p => p.Position)],
        };
        if (snap && inputs.Players is { } at && stage.Camera.Behaviour == CameraBehaviour.FollowPlayers)
        {
            stage.Camera = stage.Camera.At(at);
        }
        stage.Camera = stage.Camera.Update(dt, inputs);
    }

    private CameraMesh? Mesh(LevelScene scene)
    {
        if (!meshes.TryGetValue(scene.Key, out var mesh))
        {
            try
            {
                mesh = scene.Model?.CameraMesh is { } content ? new CameraMesh(content) : null;
            }
            catch (InvalidDataException)
            {
                mesh = null;                                                  // as the viewer: a bad camera mesh is no confines
            }
            meshes[scene.Key] = mesh;
        }
        return mesh;
    }

    private static CameraSubject Subject(Character avatar) =>
        new(ToXna(avatar.Position), avatar.Body.IsTouchingGround ? avatar.Position.Y + avatar.Body.HeightOffset : null);

    /// <summary>
    /// A new fade runs from where the cover is to all (in) or none (out) over its time; a scene change's
    /// transition covers the screen over its time out, and uncovers it over its time in. The cover is the
    /// stronger of the two, in the fade's colour (black for a transition alone).
    /// </summary>
    private void StepCover(float dt)
    {
        var stage = Level.Level.Stage;
        if (!ReferenceEquals(stage.Fade, fade))
        {
            fade = stage.Fade;
            (fadeFrom, fadeTime) = (fadeAmount, 0);
            fadeAmount = fade is null ? 0 : fadeAmount;                       // cleared (a checkpoint's restart): the screen is uncovered
        }
        if (fade is { } current)
        {
            fadeTime += dt;
            var done = current.Time > 0 ? Math.Clamp(fadeTime / current.Time, 0, 1) : 1;
            fadeAmount = float.Lerp(fadeFrom, current.In ? 1 : 0, done);
        }
        if (!ReferenceEquals(stage.Transition, transition))
        {
            transition = stage.Transition;
        }
        transitionAmount = transition is { } change
            ? Math.Clamp(transitionAmount + (change.Out ? 1 : -1) * (change.Time > 0 ? dt / change.Time : 1), 0, 1)
            : 0;
        var color = fade?.Color ?? new Numerics.Vector4(0, 0, 0, 1);
        Cover = new ScreenCover(color with { W = 1 }, Math.Max(fadeAmount, transitionAmount));
    }

    private static Xna.Vector3 ToXna(Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The cursor's ray, from the camera's eye through the point on the screen to the far plane, and where
    /// it crosses the level at a height (the avatar's), as the original picks; a ray that never crosses it
    /// points nowhere (the avatar is not turned).
    /// </summary>
    public static CursorPick Cursor(GameCamera camera, int x, int y, Xna.Point screen, float height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var aspect = screen.Y > 0 ? (float)screen.X / screen.Y : 1;
        var inverse = Xna.Matrix.Invert(camera.View * camera.Projection(aspect));
        var far = Xna.Vector4.Transform(new Xna.Vector4(-1 + 2f * x / Math.Max(screen.X, 1), 1 - 2f * y / Math.Max(screen.Y, 1), 1, 1), inverse);
        var end = new Xna.Vector3(far.X, far.Y, far.Z) / far.W;
        var ray = new Xna.Ray(camera.Eye, end - camera.Eye);
        var ground = ray.Intersects(new Xna.Plane(Xna.Vector3.Up, -height)) is { } distance ? ray.Position + ray.Direction * distance : new Xna.Vector3(float.NaN);
        var origin = new Numerics.Vector3(camera.Eye.X, camera.Eye.Y, camera.Eye.Z);
        return new CursorPick(new Gameplay.Entities.Segment(origin, new Numerics.Vector3(end.X, end.Y, end.Z) - origin),
            new Numerics.Vector3(ground.X, ground.Y, ground.Z));
    }

    public void Dispose()
    {
        Spells.Dispose();
        Effects.Dispose();
        DisposeSound();
        DisposeHud();
        Level.End();
        World.Dispose();
    }
}
