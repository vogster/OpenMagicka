namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// What a conjure did to an element queue, step by step, for the HUD's animation (docs/engine/ARCHITECTURE.md §19):
/// the element joins at the end, then pairs resolve. Indices are positions in the queue as it stands at that step
/// (the joining element at the end, or at <see cref="ElementQueue.Capacity"/> when the queue was full).
/// </summary>
public abstract record QueueChange;

/// <summary>An element joins the queue's end (always the first change of a conjure).</summary>
public sealed record QueueAdded(Elements Element) : QueueChange;

/// <summary>Two opposite entries cancel: the one at <paramref name="Source"/> goes to <paramref name="Target"/>, and both leave.</summary>
public sealed record QueueCancelled(int Source, int Target) : QueueChange;

/// <summary>The entry at <paramref name="Source"/> merges into <paramref name="Target"/>, which becomes <paramref name="Result"/>.</summary>
public sealed record QueueMerged(Elements Result, int Source, int Target) : QueueChange;
