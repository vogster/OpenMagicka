using OpenMagicka.Content.Levels;
using OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Dialogs;

/// <summary>
/// A dialog's state through the level (docs/engine/ARCHITECTURE.md §18): the interact its next start takes, how many it
/// has taken (not reset when it wraps), whether one played to its end, and each interact's own state.
/// </summary>
public sealed class DialogRun
{
    public DialogRun(DialogDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Interacts = [.. definition.Interacts.Select(i => new InteractRun(this, i))];
    }

    public DialogDefinition Definition { get; }

    public IReadOnlyList<InteractRun> Interacts { get; }

    public int Next { get; internal set; }

    /// <summary>How many interacts it has taken (at most its count; not reset when it wraps).</summary>
    public int Interacted { get; internal set; }

    public bool IsFinished { get; internal set; }

    /// <summary>
    /// Takes the next interact, as the original's: one shown more times (<c>count</c>) stays next till its last showing;
    /// past the last, a dialog that repeats goes back to <c>repeatFrom</c> with every interact afresh; none past the end
    /// of one that does not.
    /// </summary>
    public InteractRun? Pop()
    {
        if (Next >= Interacts.Count)
        {
            return null;
        }
        var taken = Interacts[Next];
        if (taken.Definition.Count > 0 && taken.Definition.Count - taken.RepeatCounter <= 1)
        {
            Next++;
            Interacted = Math.Min(Interacted + 1, Interacts.Count);
        }
        if (Next >= Interacts.Count && Definition.RepeatFrom >= 0)
        {
            Next = Math.Min(Definition.RepeatFrom, Interacts.Count);
            foreach (var interact in Interacts)
            {
                interact.Reset();
            }
        }
        return taken;
    }

    /// <summary>The interact the next start takes, or none.</summary>
    public InteractRun? Peek => Next < Interacts.Count ? Interacts[Next] : null;
}

/// <summary>An interact's state: the message it plays, how often it has played through, and who drives it.</summary>
public sealed class InteractRun(DialogRun dialog, DialogInteract definition)
{
    public DialogRun Dialog { get; } = dialog;

    public DialogInteract Definition { get; } = definition;

    public int Current { get; internal set; }

    public int RepeatCounter { get; internal set; }

    /// <summary>The player whose presses advance it (a player who started it); none: only time does.</summary>
    public Player? Interactor { get; internal set; }

    public DialogAnchor Anchor { get; internal set; }

    /// <summary>The next line of each message.</summary>
    internal int[] Lines { get; } = new int[definition.Messages.Count];

    /// <summary>The sound each message's last line started (its request's id), or 0.</summary>
    internal int[] Cues { get; } = new int[definition.Messages.Count];

    internal void Reset()
    {
        (Current, RepeatCounter) = (0, 0);
        Array.Clear(Lines);
    }
}
