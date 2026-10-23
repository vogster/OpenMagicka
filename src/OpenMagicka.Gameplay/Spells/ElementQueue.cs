using System.Collections.Immutable;

namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// The queue of conjured elements (docs/gameplay/SPELLS.md §2): up to <see cref="Capacity"/>
/// entries. Immutable: adding returns the new queue and whether it grew.
/// </summary>
public sealed class ElementQueue
{
    /// <summary>The player's capacity.</summary>
    public const int PlayerCapacity = 5;

    public ElementQueue(int capacity)
        : this(capacity, [])
    {
    }

    private ElementQueue(int capacity, ImmutableArray<Spell> entries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        (Capacity, Entries) = (capacity, entries);
    }

    /// <summary>An empty queue of the player's capacity.</summary>
    public static ElementQueue Empty { get; } = new(PlayerCapacity);

    public int Capacity { get; }

    public ImmutableArray<Spell> Entries { get; }

    public int Count => Entries.Length;

    /// <summary>Adds one element with magnitude 1 (<see cref="Add"/>).</summary>
    public (ElementQueue Queue, bool Added) Conjure(Elements element, bool lightningPrecedence = false)
    {
        if (!ElementIndex.IsSingle(element))
        {
            throw new ArgumentOutOfRangeException(nameof(element), element, "conjure one element at a time");
        }
        return Add(Spell.Of(element), lightningPrecedence);
    }

    /// <summary>
    /// Adds an entry (a weapon's queue adds normalised spells). Below capacity it is appended, then
    /// opposites cancel, then revertable pairs merge back into Water, then combinable pairs merge,
    /// each pass once, from the end. A full queue instead removes the latest opposite of the entry,
    /// or merges it into the latest revertable or combinable entry, or drops it. With
    /// <paramref name="lightningPrecedence"/>, which the game passes when lightning strikes a
    /// weapon, the latest entry that is not Lightning becomes Lightning when nothing else applied,
    /// whatever the entry added. Added is true when the queue grew by one; an unchanged queue is
    /// returned as it is.
    /// </summary>
    public (ElementQueue Queue, bool Added) Add(Spell entry, bool lightningPrecedence = false)
    {
        var list = Entries.ToList();
        bool changed;
        if (list.Count < Capacity)
        {
            list.Add(entry);
            Normalize(list);
            changed = true;
        }
        else
        {
            changed = AddToFull(list, entry, lightningPrecedence);
        }
        return (changed ? new ElementQueue(Capacity, [.. list]) : this, list.Count == Count + 1);
    }

    /// <summary>The queue replaced by one magick entry, as casting a recognised magick does.</summary>
    public ElementQueue WithMagick(MagickType magick)
    {
        if (magick is <= MagickType.None or MagickType.NrOfMagicks)
        {
            throw new ArgumentOutOfRangeException(nameof(magick), magick, "not a magick");
        }
        return new(Capacity, [Spell.OfMagick(magick)]);
    }

    public ElementQueue Cleared() => Count == 0 ? this : new(Capacity);

    private static void Normalize(List<Spell> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var j = FindFrom(list, i - 1, list[i].Element, ElementRelations.Opposites);
            if (j >= 0)
            {
                list.RemoveAt(i);
                list.RemoveAt(j);
                i--;   // both removed: the walk goes on with the entry that was before i
            }
        }
        Merge(list, ElementRelations.Revertable);
        Merge(list, ElementRelations.Combinable);
    }

    /// <summary>Each entry from the end merges into the latest earlier entry it relates to.</summary>
    private static void Merge(List<Spell> list, Func<Elements, Elements, bool> related)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var j = FindFrom(list, i - 1, list[i].Element, related);
            if (j >= 0)
            {
                list[j] = list[i] + list[j];
                list.RemoveAt(i);
            }
        }
    }

    /// <summary>The full-queue rule; returns whether the queue changed.</summary>
    private bool AddToFull(List<Spell> list, Spell spell, bool lightningPrecedence)
    {
        var last = Capacity - 1;
        var j = FindFrom(list, last, spell.Element, ElementRelations.Opposites);
        if (j >= 0)
        {
            list.RemoveAt(j);
            return true;
        }
        j = FindFrom(list, last, spell.Element, ElementRelations.Revertable);
        if (j < 0)
        {
            j = FindFrom(list, last, spell.Element, ElementRelations.Combinable);
        }
        if (j >= 0)
        {
            list[j] = spell + list[j];
            return true;
        }
        if (lightningPrecedence)
        {
            j = list.FindLastIndex(last, e => e.Element != Elements.Lightning);
            if (j >= 0)
            {
                list[j] = Spell.Of(Elements.Lightning);
                return true;
            }
        }
        return false;
    }

    /// <summary>The latest entry at or before <paramref name="last"/> that relates to <paramref name="element"/>, or -1.</summary>
    private static int FindFrom(List<Spell> list, int last, Elements element, Func<Elements, Elements, bool> related)
    {
        for (var i = Math.Min(last, list.Count - 1); i >= 0; i--)
        {
            if (related(element, list[i].Element))
            {
                return i;
            }
        }
        return -1;
    }
}
