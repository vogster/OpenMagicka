using OpenMagicka.Content;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>How a condition compares the world's value with its own: the world's is less, equal or greater.</summary>
public enum CompareMethod { Less = -1, Equal = 0, Greater = 1 }

/// <summary>
/// A trigger's condition (LEVELS_TRIGGERS.md §5.2): one kind per tag, its attributes bound to its
/// public settable properties; <see cref="Invert"/> turns the answer round.
/// </summary>
public abstract class ScriptCondition
{
    public ScriptScene Scene { get; internal set; } = null!;

    public bool Invert { get; set; }

    /// <summary>Whether it holds, for the character that set the trigger off (none for autorun).</summary>
    public virtual bool IsMet(Character? sender) => Invert ^ Holds(sender);

    protected abstract bool Holds(Character? sender);

    /// <summary>The world's value against the condition's, by the method.</summary>
    protected static bool Compare<T>(CompareMethod method, T world, T value) where T : IComparable<T> => method switch
    {
        CompareMethod.Less => world.CompareTo(value) < 0,
        CompareMethod.Equal => world.CompareTo(value) == 0,
        CompareMethod.Greater => world.CompareTo(value) > 0,
        _ => false,
    };

    /// <summary>The script hash of a name, lowered (the original's string hash).</summary>
    protected static int Hash(string name) => LegacyStringHash.Lower(name);
}

/// <summary>
/// A trigger's action (§5.3): when the trigger fires it is queued, and it runs after its delay,
/// once per firing, the runs spaced by the delay (with no delay, in the same frame). One kind per
/// tag, its attributes bound to its public settable properties.
/// </summary>
public abstract class ScriptAction
{
    private float countdown;
    private int queued;

    public ScriptScene Scene { get; internal set; } = null!;

    /// <summary>The trigger (or interactable) it belongs to.</summary>
    public ScriptTrigger Parent { get; internal set; } = null!;

    public float Delay { get; set; }

    /// <summary>The script's element, for kinds that read their children (a spawn's events) when initialized.</summary>
    public Content.Levels.ScriptNode? Node { get; internal set; }

    /// <summary>Whether no run is waiting.</summary>
    public bool IsIdle => queued == 0;

    /// <summary>Called once the scene is built (an action may look up what it names).</summary>
    public virtual void Initialize()
    {
    }

    /// <summary>The trigger fired: one more run, the first after the delay. Kinds that need the sender keep it.</summary>
    public virtual void OnTrigger(Character? sender)
    {
        if (queued <= 0)
        {
            (queued, countdown) = (1, Delay);
        }
        else
        {
            queued++;
        }
    }

    /// <summary>
    /// The waiting runs whose time has come run, each the delay after the last. Only the runs waiting as
    /// the frame began: one that sets its own trigger off again with no delay waits a frame (the original
    /// would loop for ever).
    /// </summary>
    public void Update(float dt)
    {
        countdown -= dt;
        var due = queued;
        while (countdown <= 0 && queued > 0 && due-- > 0)
        {
            queued--;
            countdown += Delay;
            Execute();
            Scene.Executed(this);
        }
    }

    /// <summary>Drops the waiting runs (what an action keeps per run stays, as the original).</summary>
    public void ClearDelayed() => queued = 0;

    /// <summary>
    /// Its replay when a checkpoint is loaded (the original's QuickExecute, LEVELS_TRIGGERS.md §6.2): by
    /// default nothing (what it did is in the saved state, or passed); kinds that rebuild the world say.
    /// </summary>
    public virtual void Replay()
    {
    }

    /// <summary>What it keeps besides its waiting runs (a spawn's survivors), copied for a checkpoint.</summary>
    protected virtual object? SaveTag() => null;

    protected virtual void LoadTag(object? tag)
    {
    }

    /// <summary>Its waiting runs and what it keeps, for a checkpoint.</summary>
    public readonly record struct State(float Countdown, int Queued, object? Tag);

    public State Save() => new(countdown, queued, SaveTag());

    public void Load(State state)
    {
        (countdown, queued) = (state.Countdown, state.Queued);
        LoadTag(state.Tag);
    }

    /// <summary>No run waits (an ignored trigger's, as the original: what it keeps stays).</summary>
    internal void ResetRuns() => (countdown, queued) = (0, 0);

    protected abstract void Execute();

    /// <summary>The script hash of a name, lowered (the original's string hash).</summary>
    protected static int Hash(string name) => LegacyStringHash.Lower(name);
}

/// <summary>A tag no kind is known for yet (none now; the actions: OM-45, OM-46): it never holds.</summary>
internal sealed class UnknownCondition(string tag) : ScriptCondition
{
    public string Tag { get; } = tag;

    protected override bool Holds(Character? sender) => false;
}

/// <summary>A tag no kind is known for yet: it does nothing.</summary>
internal sealed class UnknownAction(string tag) : ScriptAction
{
    public string Tag { get; } = tag;

    protected override void Execute()
    {
    }
}
