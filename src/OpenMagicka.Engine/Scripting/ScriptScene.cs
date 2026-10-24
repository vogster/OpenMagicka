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
    public ScriptScene(SceneDefinition scene, ScriptLevel level, ScriptRegistry registry, GameWorld world, bool looped = false, int seed = 0,
        ScriptPlaces? places = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(world);
        (Definition, Level, World, Random) = (scene, level, world, new Random(seed));
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
        foreach (var action in triggers.Values.SelectMany(t => t.Variants).SelectMany(v => v).Distinct())
        {
            action.Initialize();                                              // as the original: the conditions are never initialized
        }
    }

    public ScriptPlaces Places { get; }

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
        if (ReferenceEquals(World.Entities.Places, Places))
        {
            World.Entities.Places = null;
        }
    }

    private void OnMindTrigger(Gameplay.AI.Agent agent, int trigger) => ExecuteTrigger(trigger, agent.Owner);

    public SceneDefinition Definition { get; }

    public ScriptLevel Level { get; }

    public GameWorld World { get; }

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

    /// <summary>The scene's frame: who is where, then each trigger in key order.</summary>
    public void Update(float dt)
    {
        Places.Refill(World.Entities);
        foreach (var trigger in triggers.Values)
        {
            trigger.Update(dt);
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

    internal void Executed(ScriptAction action) => ActionExecuted?.Invoke(action);

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
