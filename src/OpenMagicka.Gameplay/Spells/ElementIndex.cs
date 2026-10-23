namespace OpenMagicka.Gameplay.Spells;

/// <summary>Elements by index (docs/gameplay/SPELLS.md §1.1, §10.1): index = log2 of the flag.</summary>
public static class ElementIndex
{
    /// <summary>The number of elements.</summary>
    public const int Count = 11;

    /// <summary>The index of a magick entry (<see cref="Elements.All"/>), beyond the elements.</summary>
    public const int Magick = 15;

    /// <summary><c>round(log2(mask))</c>: a mixed mask lands on a neighbouring element's index.</summary>
    public static int Of(Elements element)
    {
        if (element == Elements.All)
        {
            return Magick;
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual((int)element, 0, nameof(element));
        return (int)(Math.Log2((double)element) + 0.5);
    }

    /// <summary>Whether the value is exactly one of the 11 elements.</summary>
    public static bool IsSingle(Elements element) =>
        element is > Elements.None and <= Elements.Poison && ((int)element & ((int)element - 1)) == 0;
}
