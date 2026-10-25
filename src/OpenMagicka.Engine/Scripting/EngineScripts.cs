using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

public enum CounterChange { Inc, Dec, Set }

/// <summary><c>counter name= action=INC|DEC|SET value=</c>: changes a level counter.</summary>
public sealed class CounterAction : ScriptAction
{
    public string Name { get; set; } = "";

    public CounterChange Action { get; set; }

    public int Value { get; set; }

    protected override void Execute()
    {
        var id = Hash(Name);
        switch (Action)
        {
            case CounterChange.Inc:
                Scene.Level.AddToCounter(id, Value);
                break;
            case CounterChange.Dec:
                Scene.Level.AddToCounter(id, -Value);
                break;
            case CounterChange.Set:
                Scene.Level.SetCounter(id, Value);
                break;
        }
    }
}

/// <summary><c>addTimer name= value= playedTimer=</c>: starts a level timer (one already going is left).</summary>
public sealed class AddTimerAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Name { get; set; } = "";

    public float Value { get; set; }

    public bool PlayedTimer { get; set; }

    protected override void Execute() => Scene.Level.AddTimer(Hash(Name), PlayedTimer, Value);
}

/// <summary><c>timer name= value= paused=</c>: sets a timer's time and/or pauses it (what is given).</summary>
public sealed class TimerAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Name { get; set; } = "";

    public float? Value { get; set; }

    public bool? Paused { get; set; }

    protected override void Execute()
    {
        var id = Hash(Name);
        if (Value is { } value)
        {
            Scene.Level.SetTimer(id, value);
        }
        if (Paused is { } paused)
        {
            Scene.Level.PauseTimer(id, paused);
        }
    }
}

/// <summary>
/// <c>executeTrigger trigger=</c>: fires another trigger, its conditions and repeat rule holding, as
/// the one who set this off. The senders wait in order, one per run; as the original, clearing the
/// delayed runs leaves them, so later runs take older senders.
/// </summary>
public sealed class ExecuteTriggerAction : ScriptAction
{
    private readonly Queue<Character?> senders = new();

    public string Trigger { get; set; } = "";

    public override void OnTrigger(Character? sender)
    {
        base.OnTrigger(sender);
        senders.Enqueue(sender);
    }

    protected override void Execute()
    {
        var sender = senders.Count > 0 ? senders.Dequeue() : null;
        Scene.ExecuteTrigger(Hash(Trigger), sender is { Body.Body.IsRemoved: true } ? null : sender);   // ours: one gone (a restart's) sends nothing
    }

    /// <summary>The senders still waiting, copied for a checkpoint.</summary>
    protected override object? SaveTag() => senders.ToList();

    protected override void LoadTag(object? tag)
    {
        senders.Clear();
        foreach (var sender in tag as List<Character?> ?? [])
        {
            senders.Enqueue(sender);
        }
    }
}

/// <summary><c>clearDelayedActions trigger=</c>: drops the waiting runs of a trigger, of all ("all", or none named), or of all but this one ("allbutthis").</summary>
public sealed class ClearDelayedActionsAction : ScriptAction
{
    public string Trigger { get; set; } = "";

    protected override void Execute()
    {
        var name = Trigger;
        foreach (var trigger in Scene.Triggers)
        {
            if (name is "" or "all" || (name == "allbutthis" && trigger != Parent) || trigger.Key == Hash(name))
            {
                trigger.ClearDelayedActions();
            }
        }
    }
}

/// <summary><c>enablePoi id=</c>: an interactable can be used again.</summary>
public sealed class EnablePoiAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";

    protected override void Execute()
    {
        if (Scene.Find(Hash(Id)) is ScriptInteractable point)
        {
            point.Enabled = true;
        }
    }
}

/// <summary><c>disablePoi id=</c>: an interactable can no longer be used.</summary>
public sealed class DisablePoiAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";

    protected override void Execute()
    {
        if (Scene.Find(Hash(Id)) is ScriptInteractable point)
        {
            point.Enabled = false;
        }
    }
}

/// <summary><c>counter name= compareMethod= value=</c>: a level counter against a value.</summary>
public sealed class CounterCondition : ScriptCondition
{
    public string Name { get; set; } = "";

    public CompareMethod CompareMethod { get; set; }

    public int Value { get; set; }

    protected override bool Holds(Character? sender) => Compare(CompareMethod, Scene.Level.Counter(Hash(Name)), Value);
}

/// <summary><c>timer name= compareMethod= value=</c>: a level timer against a value.</summary>
public sealed class TimerCondition : ScriptCondition
{
    public string Name { get; set; } = "";

    public CompareMethod CompareMethod { get; set; }

    public float Value { get; set; }

    protected override bool Holds(Character? sender) => Compare(CompareMethod, Scene.Level.Timer(Hash(Name)), Value);
}
