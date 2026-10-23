using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

/// <summary>Reading helpers shared by Magicka's readers.</summary>
internal static class ReaderExtensions
{
    /// <summary>A u32 count followed by that many items; minBytes guards against huge allocations.</summary>
    public static T[] ReadList<T>(this XnbContentReader r, int minBytes, Func<XnbContentReader, T> read)
    {
        var items = new T[r.ReadCount(minBytes)];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = read(r);
        }
        return items;
    }

    public static int ReadNonNegative(this XnbContentReader r)
    {
        var value = r.ReadInt32();
        return value >= 0 ? value : throw new InvalidDataException($"negative value {value}");
    }
}
