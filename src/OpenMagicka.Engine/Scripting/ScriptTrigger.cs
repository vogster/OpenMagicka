using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A scene trigger at run time (LEVELS_TRIGGERS.md §5.1–5.4). Its variants are each a random group
/// followed by the plain actions, which are the same objects in every variant: as the original, each
/// variant updates them, so with several groups a plain action's delay runs out that many times faster.
/// </summary>
public class ScriptTrigger
{
    private readonly ScriptCondition[][] conditions;
    private readonly ScriptAction[][] variants;
    private float sinceRepeat;

    internal ScriptTrigger(ScriptScene scene, int key, string? id, bool autoRun, float repeat, ScriptCondition[][] conditions, ScriptAction[][] variants)
    {
        (Scene, Key, Id, AutoRun, Repeat, this.conditions, this.variants) = (scene, key, id, autoRun, repeat, conditions, variants);
        foreach (var action in variants.SelectMany(v => v))
        {
            (action.Scene, action.Parent) = (scene, this);
        }
        foreach (var condition in conditions.SelectMany(c => c))
        {
            condition.Scene = scene;
        }
    }

    public ScriptScene Scene { get; }

    /// <summary>The script hash of its id (0 without one).</summary>
    public int Key { get; }

    public string? Id { get; }

    /// <summary>Tried every frame (when not a network client).</summary>
    public bool AutoRun { get; }

    /// <summary>−1 once, 0 every frame its conditions hold, N a cooldown in seconds.</summary>
    public float Repeat { get; }

    public bool HasTriggered { get; private set; }

    public IReadOnlyList<IReadOnlyList<ScriptAction>> Variants => variants;

    public IReadOnlyList<IReadOnlyList<ScriptCondition>> Conditions => conditions;

    /// <summary>Its frame: the repeat cooldown runs, an autorun one tries, and every variant's actions run.</summary>
    internal virtual void Update(float dt)
    {
        sinceRepeat -= dt;
        if (AutoRun && Scene.IsHost)
        {
            Execute(null, ignoreConditions: false);
        }
        foreach (var variant in variants)
        {
            foreach (var action in variant)
            {
                action.Update(dt);
            }
        }
    }

    /// <summary>
    /// Fires when allowed (asked to ignore conditions, never fired, or a repeating one past its cooldown)
    /// and its conditions hold, or are ignored: one variant at random is queued. True when it fired.
    /// </summary>
    public virtual bool Execute(Character? sender, bool ignoreConditions)
    {
        if (!(ignoreConditions || !HasTriggered || (Repeat > -1e-45f && sinceRepeat < float.Epsilon)))
        {
            return false;
        }
        if (!ignoreConditions && !Holds(sender))
        {
            return false;
        }
        (HasTriggered, sinceRepeat) = (true, Repeat);
        Fire(sender);
        return true;
    }

    /// <summary>Its conditions: any block whose conditions all hold (no blocks: it holds).</summary>
    public bool Holds(Character? sender)
    {
        var holds = true;
        foreach (var block in conditions)
        {
            holds = true;
            foreach (var condition in block)
            {
                if (!condition.IsMet(sender))
                {
                    holds = false;
                    break;
                }
            }
            if (holds)
            {
                break;
            }
        }
        return holds;
    }

    /// <summary>Queues one variant, chosen at random among several.</summary>
    protected void Fire(Character? sender)
    {
        var chosen = variants.Length > 1 ? Scene.Random.Next(variants.Length) : 0;
        Scene.Fired(this, chosen, sender);
        foreach (var action in variants[chosen])
        {
            action.OnTrigger(sender);
        }
    }

    public void ClearDelayedActions()
    {
        foreach (var action in variants.SelectMany(v => v))
        {
            action.ClearDelayed();
        }
    }

    /// <summary>The repeat cooldown starts over (a scene is entered).</summary>
    internal void ResetTimers() => sinceRepeat = 0;
}

/// <summary>
/// A point the player can interact with (§5.5): no conditions, never autorun; each interaction
/// while enabled queues a variant. Enabled is toggled by enablePoi and disablePoi.
/// </summary>
public sealed class ScriptInteractable : ScriptTrigger
{
    internal ScriptInteractable(ScriptScene scene, int key, string? locator, bool enabled, ScriptAction[][] variants)
        : base(scene, key, locator, autoRun: false, repeat: -1, [], variants) => Enabled = enabled;

    public bool Enabled { get; set; }

    /// <summary>A character interacts with it; nothing while it is disabled (repeats have no limit).</summary>
    public void Interact(Character? interactor)
    {
        if (Enabled)
        {
            Fire(interactor);
        }
    }
}
