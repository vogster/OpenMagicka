namespace OpenMagicka.Content;

/// <summary>
/// The string hash of the 32-bit .NET Framework 2.0-3.5 (<c>String.GetHashCode</c>), which
/// Magicka uses to turn names (effects, sounds, locators, triggers, templates) into IDs. Modern
/// .NET randomizes string hashes, so the algorithm is reproduced here: two accumulators walk the
/// string's UTF-16 code units two at a time (a missing second unit reads as 0) and are combined
/// at the end. Description: docs/formats/XNB_READERS.md §0.4.
/// </summary>
public static class LegacyStringHash
{
    private const int Seed = (5381 << 16) + 5381;
    private const int Multiplier = 1566083941;

    public static int Compute(ReadOnlySpan<char> value)
    {
        int h1 = Seed, h2 = Seed;
        var i = 0;
        var remaining = value.Length;
        while (remaining > 2)
        {
            h1 = Step(h1, Word(value, i));
            h2 = Step(h2, Word(value, i + 2));
            i += 4;
            remaining -= 4;
        }
        if (remaining > 0)
        {
            h1 = Step(h1, Word(value, i));
        }
        return unchecked(h1 + h2 * Multiplier);
    }

    public static int Compute(string value) => Compute(value.AsSpan());

    /// <summary>
    /// Hash of the invariant lower-case form, as the original does for most names. The game's
    /// names are ASCII; for other characters case mapping can differ between platforms.
    /// </summary>
    public static int Lower(string value) => Compute(value.ToLowerInvariant());

    private static int Step(int hash, int word) => unchecked(((hash << 5) + hash + (hash >> 27)) ^ word);

    /// <summary>Two code units as one little-endian 32-bit word; past the end reads as 0.</summary>
    private static int Word(ReadOnlySpan<char> value, int index)
    {
        var low = index < value.Length ? value[index] : 0;
        var high = index + 1 < value.Length ? value[index + 1] : 0;
        return low | (high << 16);
    }
}
