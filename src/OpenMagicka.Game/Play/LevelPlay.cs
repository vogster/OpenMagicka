using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Characters;
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
/// whose wizard a stand-in walk moves until the controls come (OM-31); each tick the level runs, then the
/// stage's camera follows the players (or the followed entity) on the scene's camera mesh, the effects
/// are passed on (<see cref="EffectDirector"/>), and the screen's fades and scene transitions are stepped
/// into a cover.
/// </summary>
public sealed class LevelPlay : IDisposable
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
        return new LevelPlay(definition, new ContentLevel(files, loader), services, wizard, library.LookOf, GibShapes(loader));
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
        Func<object, Engine.Rendering.ModelLook?>? looks = null, Func<string, (Numerics.Vector3 Centre, float Radius)?>? gibShapes = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Looks = looks ?? (_ => null);
        Session = new GameSession();
        World = new GameWorld(session: Session);
        Player = Session.AddPlayer();
        Player.Avatar = new Character(wizard.Template, World.Physics, new Numerics.Vector3(0, 50, 0), animations: wizard.Animations) { IsPlayer = true, Player = Player };
        Level = new PlayedLevel(definition, content, World, services);
        Effects = new EffectDirector(World, Level, Channel, gibShapes ?? (_ => null));
    }

    /// <summary>The effects passed to the renderer, and back.</summary>
    public EffectChannel Channel { get; } = new();

    public EffectDirector Effects { get; }

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
        StepCamera(0, snap: true);
    }

    /// <summary>
    /// A tick: the wizard walks (right is +X, forward is −Z, as on the screen; each −1..1) unless its
    /// events or the stage hold it, the level runs, the camera follows, the cover is stepped.
    /// </summary>
    public void Tick(float dt, float right, float forward)
    {
        if (Player.Avatar is { IsDead: false, IsInEvent: false } avatar)
        {
            var way = Level.Level.Stage.IsInputLimited ? Numerics.Vector3.Zero : new Numerics.Vector3(right, 0, -forward);   // held: it stands
            avatar.Body.Movement = way.LengthSquared() > 1 ? Numerics.Vector3.Normalize(way) : way;
        }
        Level.Tick(dt);
        var changed = false;
        foreach (var request in Level.TakeRequests())
        {
            changed |= request is SceneChangedRequest;
            Requests.Add(request);                                            // for the sound, the dialogs and the HUD (OM-49, OM-51)
        }
        if (changed)
        {
            Effects.Clear();
            meshScene = -1;                                                   // the camera stands over the players at once
        }
        Effects.Update();
        StepCamera(dt, snap: false);
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
        var followed = stage.Followed is { Manager: not null } entity ? Xna(entity.Position) : (Xna.Vector3?)null;
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
        new(Xna(avatar.Position), avatar.Body.IsTouchingGround ? avatar.Position.Y + avatar.Body.HeightOffset : null);

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

    private static Xna.Vector3 Xna(Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public void Dispose()
    {
        Effects.Dispose();
        Level.End();
        World.Dispose();
    }
}
