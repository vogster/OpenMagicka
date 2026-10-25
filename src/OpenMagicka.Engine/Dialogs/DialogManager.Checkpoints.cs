namespace OpenMagicka.Engine.Dialogs;

/// <summary>
/// The dialogs at a checkpoint, as the original's: which are finished, each one's next interact and interacts taken,
/// and each interact's message and showings. No box, sound, camera or lock is kept.
/// </summary>
public sealed record DialogsCheckpoint(IReadOnlyDictionary<int, DialogsCheckpoint.Dialog> Dialogs)
{
    public sealed record Dialog(bool Finished, int Next, int Interacted, IReadOnlyList<(int Current, int RepeatCounter)> Interacts);
}

public sealed partial class DialogManager
{
    public object Save() => new DialogsCheckpoint(runs.ToDictionary(p => p.Key, p => new DialogsCheckpoint.Dialog(p.Value.IsFinished, p.Value.Next,
        p.Value.Interacted, [.. p.Value.Interacts.Select(i => (i.Current, i.RepeatCounter))])));

    /// <summary>
    /// Back to a checkpoint: every dialog ends, each starts over unfinished (its interacts taken kept, as the original's)
    /// with its interacts from their first message, then the checkpoint's state comes back, every message from its first line.
    /// </summary>
    public void Restore(object? saved)
    {
        EndAll();
        foreach (var run in runs.Values)
        {
            (run.Next, run.IsFinished) = (0, false);
            foreach (var interact in run.Interacts)
            {
                interact.Reset();
            }
        }
        if (saved is not DialogsCheckpoint checkpoint)
        {
            return;
        }
        foreach (var (key, state) in checkpoint.Dialogs)
        {
            if (Run(key) is not { } run)
            {
                continue;
            }
            (run.IsFinished, run.Next, run.Interacted) = (state.Finished, state.Next, state.Interacted);
            for (var i = 0; i < run.Interacts.Count && i < state.Interacts.Count; i++)
            {
                var interact = run.Interacts[i];
                (interact.Current, interact.RepeatCounter) = state.Interacts[i];
            }
        }
    }
}
