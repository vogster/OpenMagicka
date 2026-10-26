using System.Numerics;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// One of a level's scenes (docs/formats/LEVELS_TRIGGERS.md §6.1): its file, and once entered its
/// places and scripts, which live on (not reset) until the level ends; what waits while it is left.
/// </summary>
public sealed class LevelScene
{
    internal LevelScene(SceneDefinition definition) => (Definition, Key) = (definition, Content.LegacyStringHash.Lower(definition.Name));

    public SceneDefinition Definition { get; }

    /// <summary>The hash of its lowered name, what spawn points name it by.</summary>
    public int Key { get; }

    /// <summary>Its scripts, made when it is first entered.</summary>
    public ScriptScene? Scripts { get; internal set; }

    public LevelModelContent? Model { get; internal set; }

    /// <summary>Its nav mesh, made on its first entry (the minds' paths keep pointing into it).</summary>
    public Gameplay.Navigation.NavMesh? NavMesh { get; internal set; }

    /// <summary>It has not been entered yet: its model's props stand when it is.</summary>
    public bool IsFirstEntry { get; internal set; } = true;

    /// <summary>The entities kept while it is left (its NPCs, props and items, with saveNPCs).</summary>
    internal List<Entity> Kept { get; } = [];

    /// <summary>The entities a checkpoint's load gave it, made when it is next entered.</summary>
    internal List<EntityState> Pending { get; } = [];

    /// <summary>The actions a checkpoint's load gave it to replay when it is next entered (its startup actions).</summary>
    internal List<ScriptAction> Replay { get; } = [];
}

/// <summary>
/// A level at play (§6.1): its scenes in one world, one at a time. A scene change (its <c>&lt;Start&gt;</c>,
/// a <c>changeScene</c>) waits for the screen's transition out, then leaves the scene (its scripts out of
/// the world; its living NPCs, props and items kept and suspended with saveNPCs, else gone; the players
/// waiting; its collision out) and enters the next (its collision; its model's props the first time;
/// what it kept back; its repeat timers reset; the players at the spawn point's locators, on the floor;
/// the camera at their centre), then the transition comes back in. A frame runs the world, the level's
/// timers, the scene; the stage's requests other systems answer are passed on (<see cref="TakeRequests"/>).
/// </summary>
public sealed partial class PlayedLevel
{
    private readonly Dictionary<int, LevelScene> scenes = [];
    private readonly ILevelContent content;
    private readonly Queue<ScriptRequest> requests = new();
    private readonly List<Entity> waitingAvatars = [];
    private IReadOnlyList<StaticMesh> collision = [];
    private readonly ScriptRegistry registry;
    private (SpawnPoint Point, ScriptTransition Transition, float Time, bool Keep, Action<ScriptScene>? Entered)? pending;
    private float transitionLeft, transitionInLeft;

    public PlayedLevel(LevelDefinition definition, ILevelContent content, GameWorld world, LevelServices? services = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        World = world ?? throw new ArgumentNullException(nameof(world));
        Services = services ?? new LevelServices();
        Game = Services.Game ?? new ScriptGame();
        registry = Services.Registry ?? ScriptRegistry.Engine();
        Level.Stage.Requested += OnRequested;
        foreach (var path in definition.Scenes)
        {
            var scene = new LevelScene(content.Scene(path));
            scenes.TryAdd(scene.Key, scene);                                  // the original fails on a second scene of a name
        }
    }

    public LevelDefinition Definition { get; }

    public GameWorld World { get; }

    public LevelServices Services { get; }

    public ScriptGame Game { get; }

    /// <summary>The level's counters, timers and stage, shared by its scenes.</summary>
    public ScriptLevel Level { get; } = new();

    public IEnumerable<LevelScene> Scenes => scenes.Values;

    public LevelScene? Find(int key) => scenes.GetValueOrDefault(key);

    public LevelScene? Find(string name) => Find(Content.LegacyStringHash.Lower(name));

    /// <summary>The scene being played; none before the level starts.</summary>
    public LevelScene? Current { get; private set; }

    /// <summary>A scene change waits for its transition.</summary>
    public bool IsChanging => pending is not null;

    /// <summary>
    /// The level starts at its <c>&lt;Start&gt;</c> (the first scene when it names none), with no transition. A
    /// start in a scene the level lacks fails.
    /// </summary>
    public void Start()
    {
        if (Current is not null || Definition.Scenes.Count == 0)
        {
            return;
        }
        var first = Path.GetFileNameWithoutExtension(Definition.Scenes[0]);
        var start = SpawnPoint.Of(Definition.Start, first);
        if (!scenes.ContainsKey(start.Scene))
        {
            throw new InvalidDataException($"level {Definition.Name}: no scene \"{Definition.Start.Scene}\" to start in");
        }
        ChangeScene(start, ScriptTransition.None, 0, keep: false);
    }

    /// <summary>The level ends: what waits outside is let go, and the world is emptied.</summary>
    public void End()
    {
        Current?.Scripts?.Leave();
        foreach (var entity in scenes.Values.SelectMany(s => s.Kept).Concat(waitingAvatars).Distinct().ToList())
        {
            World.Entities.Discard(entity);
        }
        foreach (var scene in scenes.Values)
        {
            scene.Kept.Clear();
        }
        waitingAvatars.Clear();
        World.Entities.Clear();
        Current = null;
    }

    /// <summary>
    /// A move to another scene: after the transition out (none, or no time: at the end of this frame),
    /// as the original. A scene the level lacks crashes the original; here nothing happens.
    /// </summary>
    public void ChangeScene(SpawnPoint to, ScriptTransition transition, float time, bool keep, Action<ScriptScene>? entered = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (!scenes.ContainsKey(to.Scene))
        {
            return;
        }
        pending = (to, transition, time, keep, entered);
        var fades = transition != ScriptTransition.None && time >= float.Epsilon;
        transitionLeft = fades ? time : 0;
        Level.Stage.Transition = fades ? new SceneTransition(transition, time, Out: true) : null;
        if (Current is null)
        {
            Change();
        }
    }

    /// <summary>A frame: the world, the level's timers, the scene; then a scene change whose transition is over.</summary>
    public void Tick(float dt)
    {
        if (Current?.Scripts is not { } scripts)
        {
            return;
        }
        World.Tick(dt);
        Level.Update(dt, Game.InCutscene);
        scripts.Update(dt);
        KeepUnarmed();
        foreach (var request in Level.Stage.TakeRequests())
        {
            if (request is ChangeSceneRequest change)
            {
                ChangeScene(new SpawnPoint(Content.LegacyStringHash.Lower(change.Scene), change.SpawnPoints, change.SpawnPlayers), change.Transition,
                    change.Time, change.SaveNpcs, change.Entered);
                continue;
            }
            requests.Enqueue(request);                                        // a checkpoint was taken as it was asked (OnRequested)
        }
        if (pending is not null && (transitionLeft -= dt) <= 0)
        {
            Change();
        }
        else if (Level.Stage.Transition is { Out: false } && (transitionInLeft -= dt) <= 0)
        {
            Level.Stage.Transition = null;
        }
    }

    /// <summary>A checkpoint is taken the moment its action runs, as the original's (then passed on, for the saves, the text and the fairy).</summary>
    private void OnRequested(ScriptRequest request)
    {
        if (request is CheckpointRequest checkpoint)
        {
            TakeCheckpoint(checkpoint.SpawnPoints, checkpoint.Ignore);
        }
    }

    /// <summary>The requests for the systems beyond the world, since the last time, in order.</summary>
    public IReadOnlyList<ScriptRequest> TakeRequests()
    {
        var taken = requests.ToArray();
        requests.Clear();
        return taken;
    }

    private void Change()
    {
        if (pending is not { } change || World.Physics.IsStepping)
        {
            return;
        }
        pending = null;
        var (from, to) = (Current, scenes[change.Point.Scene]);
        Prepare(to);                                                          // before the old scene goes: a model that fails leaves it as it was
        if (from is not null)
        {
            Leave(from, change.Keep);
        }
        Game.Tutorial.Forget();                                               // the old scene's dialog hint goes, its trigger never set off
        Enter(to, change.Point);
        requests.Enqueue(new SceneChangedRequest(from?.Key ?? 0, to.Key, change.Keep));
        change.Entered?.Invoke(to.Scripts!);
        var fades = change.Transition != ScriptTransition.None && change.Time >= float.Epsilon;
        Level.Stage.Transition = fades ? new SceneTransition(change.Transition, change.Time, Out: false) : null;
        transitionInLeft = fades ? change.Time : 0;
    }

    /// <summary>
    /// While a stretch without weapons lasts, a player's wizard made since (a checkpoint's fresh one, one revived) gives
    /// up the weapons its template gave it, as the original's do when they are made.
    /// </summary>
    private void KeepUnarmed()
    {
        if (!Game.UnequipWeaponsSection)
        {
            return;
        }
        BeginUnequipWeaponsSectionAction.Disarm(Services.Assets, (World.Session?.Players ?? []).Select(p => p.Avatar).OfType<Character>()
            .Where(a => a.ItemIn(Character.WeaponSlot)?.Template.Id.Equals(Path.GetFileName(BeginUnequipWeaponsSectionAction.Unarmed),
                StringComparison.OrdinalIgnoreCase) != true));
    }

    /// <summary>A scene's model, nav mesh and scripts, made the first time it is gone to.</summary>
    private void Prepare(LevelScene scene)
    {
        scene.Model ??= scene.Definition.Model is { Length: > 0 } asset ? content.Model(asset) : null;
        if (scene.Scripts is null)
        {
            scene.Scripts = new ScriptScene(scene.Definition, Level, registry, World, Services.Looped, Services.Seed, new ScriptPlaces(scene.Model), Game,
                Services.Assets);
            scene.NavMesh = SceneSetup.Navigation(scene.Model, scene.Scripts.Places);
        }
    }

    private void Leave(LevelScene scene, bool keep)
    {
        scene.Scripts?.Leave();
        if (scene.Scripts is { } scripts)
        {
            LevelCollision.UnloadParts(scripts.Places);
        }
        var (avatars, kept) = World.Entities.Store(keep);
        waitingAvatars.AddRange(avatars);
        scene.Kept.AddRange(kept);
        foreach (var mesh in collision)
        {
            World.Physics.RemoveLevelMesh(mesh);
        }
        collision = [];
        (World.Entities.NavMesh, World.Entities.Bestiary) = (null, null);
    }

    private void Enter(LevelScene scene, SpawnPoint point, bool replay = true)
    {
        Current = scene;
        collision = scene.Model is { } model ? LevelCollision.Load(World.Physics, model) : [];
        scene.Scripts!.Places.UpdateParts(0);                                 // the parts where they stand, their collision placed there
        collision = [.. collision, .. LevelCollision.LoadParts(World.Physics, scene.Scripts.Places)];
        (World.Entities.Bestiary, World.Entities.NavMesh) = (Services.Bestiary, scene.NavMesh);
        if (scene.IsFirstEntry)
        {
            scene.IsFirstEntry = false;
            SceneSetup.PlaceProps(World, scene.Model, Services.Assets);
        }
        foreach (var entity in scene.Kept)
        {
            World.Entities.Restore(entity);
        }
        scene.Kept.Clear();
        foreach (var state in scene.Pending)
        {
            if (World.Entities.Entities.Count < EntityManager.Capacity - GameSession.MaxPlayers)
            {
                World.Entities.Add(state.Make(World));                        // the players' room kept
            }
        }
        scene.Pending.Clear();
        scene.Scripts!.Enter();
        PlacePlayers(point, scene.Scripts);
        if (replay)
        {
            ReplayEntry(scene);
        }
    }

    /// <summary>The scene's actions to replay (a checkpoint's), as the original: the startup actions, last of the entry.</summary>
    private static void ReplayEntry(LevelScene scene)
    {
        foreach (var action in scene.Replay)
        {
            action.Replay();
        }
        scene.Replay.Clear();
    }

    /// <summary>
    /// The living players at the spawn point's locators, as the original: player i at locator i, or at the
    /// nearest lower one found, 2 along x for each player past it (none found: 2 along x per player from
    /// the origin); 1 up, then onto the floor up to 9 below; turned the locator's way. The camera goes to
    /// the centre of the four spots. A dead player's corpse stays behind (a revival raises a new wizard).
    /// </summary>
    private void PlacePlayers(SpawnPoint point, ScriptScene scene)
    {
        var avatars = waitingAvatars.ToList();
        waitingAvatars.Clear();
        var players = World.Session?.Players ?? [];
        foreach (var player in players)
        {
            var (at, facing) = Spot(point, scene.Places, player.Id);
            if (player.Avatar is not { } avatar || avatar.Body.Body.IsRemoved)
            {
                continue;
            }
            var waited = avatars.Remove(avatar);
            if (avatar.IsDead)
            {
                continue;                                                     // as the original: the dead are not brought in
            }
            if (!point.SpawnPlayers)
            {
                if (waited)
                {
                    World.Entities.Restore(avatar);                          // ours: the players come back where they were (the original leaves them out)
                }
                continue;
            }
            var from = at + Vector3.UnitY;
            var floor = World.Physics.CastSegment(from, from - Vector3.UnitY * 10, levelOnly: true);
            var body = avatar.Body;
            body.Body.Position = floor is { } hit ? hit.Position - Vector3.UnitY * body.HeightOffset : at;
            if (waited)
            {
                World.Entities.Restore(avatar);
            }
            else if (avatar.Manager is null)
            {
                World.Entities.Add(avatar);                                   // the level's start brings the players in
            }
            (body.Body.Velocity, body.Movement) = (Vector3.Zero, Vector3.Zero);
            body.Face(facing);
            avatar.PlayEvents(null);
        }
        foreach (var avatar in avatars.Where(a => !a.IsDead && a is not Character { Body.Body.IsRemoved: true }))
        {
            World.Entities.Restore(avatar);
        }
        if (point.SpawnPlayers)
        {
            var ground = Enumerable.Range(0, GameSession.MaxPlayers).Aggregate(Vector3.Zero, (sum, i) => sum + Spot(point, scene.Places, i).At) / GameSession.MaxPlayers;
            Level.Stage.Camera = Level.Stage.Camera.Release(0).At(new Microsoft.Xna.Framework.Vector3(ground.X, ground.Y, ground.Z));
        }
    }

    private static (Vector3 At, Vector3 Facing) Spot(SpawnPoint point, ScriptPlaces places, int player)
    {
        for (var found = Math.Min(player, point.Locators.Count - 1); found >= 0; found--)
        {
            if (places.LocatorTransform(point.Locators[found]) is { } locator)
            {
                return (locator.Translation + Vector3.UnitX * (2 * (player - found)) + Vector3.UnitY, ScriptPlaces.Forward(locator));
            }
        }
        return (Vector3.UnitX * (2 * (player + 1)) + Vector3.UnitY, -Vector3.UnitZ);
    }
}
