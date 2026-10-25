using OpenMagicka.Content.Levels;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A scene's scripts at run time (LEVELS_TRIGGERS.md §5): its triggers and interactables in one table
/// by key, updated each frame in ascending key order (not file order), over the level's counters and
/// timers and the scene's world.
/// </summary>
public sealed class ScriptScene
{
    private readonly SortedList<int, ScriptTrigger> triggers = [];

    /// <param name="looped">A replayed campaign: the looped blocks are in.</param>
    /// <param name="places">The scene's areas and locators (from its level model); none for a bare scene.</param>
    /// <param name="game">What the scripts read of the game beyond the world; a quiet one when none is given.</param>
    /// <param name="assets">Where the scripts find their items and props (the actions look them up as they are initialized).</param>
    public ScriptScene(SceneDefinition scene, ScriptLevel level, ScriptRegistry registry, GameWorld world, bool looped = false, int seed = 0,
        ScriptPlaces? places = null, ScriptGame? game = null, IScriptAssets? assets = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(world);
        (Definition, Level, World, Random, Game, Looped, Assets) = (scene, level, world, new Random(seed), game ?? new ScriptGame(), looped, assets);
        foreach (var trigger in scene.Triggers)
        {
            var conditions = trigger.Conditions.Select(block => block.Where(n => n.Type == System.Xml.XmlNodeType.Element)
                .Select(registry.MakeCondition).ToArray()).ToArray();
            Add(new ScriptTrigger(this, trigger.Key, trigger.Id, trigger.AutoRun, trigger.Repeat, conditions, Variants(trigger.Actions, registry, looped)));
        }
        foreach (var point in scene.Interactables)
        {
            Add(new ScriptInteractable(this, point.Key, point.Locator, point.Enabled, Variants(point.Actions, registry, looped)));
        }
        Places = places ?? new ScriptPlaces(null);
        Effects = new SceneEffects(Places);
        foreach (var action in triggers.Values.SelectMany(t => t.Variants).SelectMany(v => v).Distinct())
        {
            action.Initialize();                                              // as the original: the conditions are never initialized
        }
    }

    public ScriptPlaces Places { get; }

    /// <summary>The named effects: the level's and its parts', started now, and those the scripts spawn.</summary>
    public SceneEffects Effects { get; }

    /// <summary>The level model's lights the scripts turned off or on.</summary>
    public SceneLights Lights { get; } = new();

    /// <summary>Where scripts find the items they put in the world; none: they make none.</summary>
    public IScriptAssets? Assets { get; set; }

    /// <summary>The level's stage (it outlives a scene change): the camera, the fade, the cutscene, the input locks and the requests.</summary>
    public ScriptStage Stage => Level.Stage;

    /// <summary>
    /// A cutscene begins (§9.3): the timers that play stop, the HUD goes, the dialogs end unless kept, and
    /// the world near the camera is calmed. <paramref name="onSkip"/> is what a skip sets off (0: no skip).
    /// </summary>
    public void BeginCutscene(int onSkip, bool skipBarMove, bool killDialogs)
    {
        Game.InCutscene = true;
        (Stage.IsUiEnabled, Stage.IsCutsceneSkipped, Stage.CutsceneSkipTrigger, Stage.CutsceneSkipsBarMove) = (false, false, onSkip, skipBarMove);
        if (killDialogs)
        {
            Stage.Request(new EndDialogsRequest());
        }
        var ground = Stage.Camera.Ground;
        Gameplay.Entities.CutsceneCalm.Calm(World.Entities, new System.Numerics.Vector3(ground.X, ground.Y, ground.Z));
    }

    public void EndCutscene(bool skipBarMove)
    {
        Game.InCutscene = false;
        (Stage.IsUiEnabled, Stage.IsCutsceneSkipped, Stage.CutsceneSkipsBarMove, Stage.ShowsSkipTip) = (true, false, skipBarMove, false);
        Stage.Request(new EndDialogsRequest());
    }

    /// <summary>
    /// A player asks to skip the cutscene: the first time the tip shows; after that its skip trigger runs
    /// once. One with no skip trigger cannot be skipped, and a network client does not skip (the host
    /// does). True when it skipped.
    /// </summary>
    public bool SkipCutscene()
    {
        if (!Game.InCutscene || Stage.IsCutsceneSkipped || !IsHost || Stage.CutsceneSkipTrigger == 0)
        {
            return false;
        }
        if (!Stage.ShowsSkipTip)
        {
            Stage.ShowsSkipTip = true;
            return false;
        }
        Stage.IsCutsceneSkipped = true;
        var trigger = Stage.CutsceneSkipTrigger;
        Stage.CutsceneSkipTrigger = 0;
        ExecuteTrigger(trigger, null);
        return true;
    }

    /// <summary>The camera holds the players' input (a camera move's lock), or lets it go.</summary>
    internal void LockInputToCamera(bool locked)
    {
        if (locked)
        {
            Stage.LimitInput(Stage);
        }
        else
        {
            Stage.UnlimitInput(Stage);
        }
    }

    /// <summary>Whether it is the world's scene now (between <see cref="Enter"/> and <see cref="Leave"/>).</summary>
    public bool IsEntered { get; private set; }

    /// <summary>
    /// The scene is entered: every repeat cooldown starts over, and the world's minds reach its triggers
    /// and use its places. Entering again changes nothing more.
    /// </summary>
    public void Enter()
    {
        foreach (var trigger in triggers.Values)
        {
            trigger.ResetTimers();
        }
        if (!IsEntered)
        {
            IsEntered = true;
            World.Entities.Places = Places;
            World.Entities.Minds.TriggerRaised += OnMindTrigger;
            World.Entities.ScriptTrigger += OnEntityTrigger;
        }
    }

    /// <summary>The scene is left: the minds no longer reach its triggers, nor its places.</summary>
    public void Leave()
    {
        if (!IsEntered)
        {
            return;
        }
        IsEntered = false;
        World.Entities.Minds.TriggerRaised -= OnMindTrigger;
        World.Entities.ScriptTrigger -= OnEntityTrigger;
        if (ReferenceEquals(World.Entities.Places, Places))
        {
            World.Entities.Places = null;
        }
    }

    private void OnMindTrigger(Gameplay.AI.Agent agent, int trigger) => ExecuteTrigger(trigger, agent.Owner);

    private void OnEntityTrigger(Gameplay.Entities.Entity source, int trigger) => ExecuteTrigger(trigger, source as Character);

    public SceneDefinition Definition { get; }

    public ScriptLevel Level { get; }

    public GameWorld World { get; }

    public ScriptGame Game { get; }

    /// <summary>A replayed campaign: the looped blocks are in, and isCampaignLooped holds.</summary>
    public bool Looped { get; }

    /// <summary>The variants' (and the scripts') dice.</summary>
    public Random Random { get; }

    /// <summary>A network client runs no autorun: it plays what the host fires.</summary>
    public bool IsHost { get; set; } = true;

    /// <summary>The triggers and interactables, in the order they update.</summary>
    public IEnumerable<ScriptTrigger> Triggers => triggers.Values;

    /// <summary>A trigger fired: which, the variant, and by whom (the host's broadcast, OM-59).</summary>
    public event Action<ScriptTrigger, int, Character?>? TriggerFired;

    /// <summary>An action ran (the checkpoint's log of what was done, OM-47).</summary>
    public event Action<ScriptAction>? ActionExecuted;

    public ScriptTrigger? Find(int key) => triggers.GetValueOrDefault(key);

    public ScriptTrigger? Find(string id) => Find(Hash(id));

    /// <summary>
    /// The scene's frame: who is where, each trigger in key order, the effects (on their parts' last
    /// places), then the animated parts, as the original.
    /// </summary>
    public void Update(float dt)
    {
        Places.Refill(World.Entities);
        foreach (var trigger in triggers.Values)
        {
            trigger.Update(dt);
        }
        Effects.Update(World.Physics);
        Places.UpdateParts(dt);
        Stage.Update(dt);
        Game.BossFight.Update();
        if (Game.HealthBar.Update(dt) is var ended and not 0)
        {
            ExecuteTrigger(ended, null);
        }
    }

    /// <summary>Fires a trigger by key, as the script's executeTrigger does (its conditions and repeat rule hold); a missing one is nothing.</summary>
    public bool ExecuteTrigger(int key, Character? sender, bool ignoreConditions = false) =>
        IsHost && Find(key) is { } trigger && trigger.Execute(sender, ignoreConditions);

    /// <summary>A character interacts with the point at a locator (by key).</summary>
    public void Interact(int key, Character? interactor)
    {
        if (Find(key) is ScriptInteractable point)
        {
            point.Interact(interactor);
        }
    }

    /// <summary>The script hash of a name as scripts write it (lowered).</summary>
    public static int Hash(string name) => Content.LegacyStringHash.Lower(name);

    internal void Fired(ScriptTrigger trigger, int variant, Character? sender) => TriggerFired?.Invoke(trigger, variant, sender);

    internal void Executed(ScriptAction action)
    {
        if (action is SpawnAction)
        {
            log.Remove(action);                                                // as the original: a spawn run again moves to the end
        }
        log.Add(action);
        ActionExecuted?.Invoke(action);
    }

    private readonly List<ScriptAction> log = [];

    /// <summary>The actions that ran, in order (what a checkpoint replays).</summary>
    public IReadOnlyList<ScriptAction> Log => log;

    /// <summary>The scene's scripts for a checkpoint: each trigger's state by key, the log, and the animated parts' states.</summary>
    public sealed record Snapshot(IReadOnlyDictionary<int, ScriptTrigger.State> Triggers, IReadOnlyList<ScriptAction> Log,
        IReadOnlyList<ScenePart.State> Parts);

    public Snapshot Save() => new(triggers.Values.ToDictionary(t => t.Key, t => t.Save()), [.. log], [.. Places.AllParts.Select(p => p.Save())]);

    /// <summary>
    /// The scripts back as saved, the ignored triggers as if they never fired; returns what to replay (the log,
    /// the ignored triggers' actions left out), as the original.
    /// </summary>
    public IReadOnlyList<ScriptAction> Load(Snapshot snapshot, IReadOnlyCollection<int>? ignored)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var trigger in triggers.Values)
        {
            if (ignored?.Contains(trigger.Key) == true)
            {
                trigger.Reset();
            }
            else if (snapshot.Triggers.TryGetValue(trigger.Key, out var state))
            {
                trigger.Load(state);
            }
        }
        foreach (var (part, state) in Places.AllParts.Zip(snapshot.Parts))
        {
            part.Load(state);
        }
        Places.UpdateParts(0);                                                 // the parts in their saved poses before the replay
        Lights.Reset();                                                       // as the original: the replay switches them again
        log.Clear();
        log.AddRange(snapshot.Log);
        return [.. snapshot.Log.Where(a => ignored?.Contains(a.Parent.Key) != true)];
    }

    private void Add(ScriptTrigger trigger)
    {
        if (!triggers.TryAdd(trigger.Key, trigger))
        {
            throw new InvalidDataException($"scene {Definition.Name}: two triggers or points share the key of \"{trigger.Id}\"");
        }
    }

    private static ScriptAction[][] Variants(ActionBlock block, ScriptRegistry registry, bool looped)
    {
        var plain = block.Regular.Where(a => looped || !a.Looped).Select(a => registry.MakeAction(a.Node)).ToArray();
        var groups = block.Random.Where(g => looped || !g.Looped).Select(g => g.Actions.Select(registry.MakeAction).ToArray()).ToArray();
        return groups.Length == 0 ? [plain] : [.. groups.Select(g => g.Concat(plain).ToArray())];   // the plain ones shared, as the original
    }
}
