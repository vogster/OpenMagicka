using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace OpenMagicka.Content;

/// <summary>Copies that content records hand out, so that no caller can change them afterwards.</summary>
internal static class ReadOnly
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ReadOnlyCollection<T>([.. items]);
    }

    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> items)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        return items as FrozenDictionary<TKey, TValue> ?? items.ToFrozenDictionary();
    }
}
