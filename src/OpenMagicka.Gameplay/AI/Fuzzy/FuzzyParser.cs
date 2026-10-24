using System.Globalization;

namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>
/// The original's reading of a fuzzy expression (ENTITIES_AI.md §7.5): every operator outside a
/// function's brackets gets a priority, its bracket depth above its own rank (^ 200, ! 150, * / 100,
/// + − 50); the operator with the least priority splits the text, and each side is read the same way.
/// A text with no operator is a number or a variable.
/// </summary>
internal static class FuzzyParser
{
    private readonly record struct Operator(char Symbol, int Position, int Priority);

    private static readonly Dictionary<char, int> Ranks = new() { ['^'] = 200, ['!'] = 150, ['*'] = 100, ['/'] = 100, ['+'] = 50, ['-'] = 50 };

    public static FuzzyExpression Parse(string text, bool leftToRight)
    {
        var operators = new List<Operator>();
        var depth = 0;
        var inFunction = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(')
            {
                if (inFunction)
                {
                    throw new FormatException($"a bracket within a function's: {text}");
                }
                inFunction = FuzzyVariable.EndsWithName(text, i);
                depth++;
            }
            else if (c == ')')
            {
                (inFunction, depth) = (false, depth - 1);
            }
            else if (!inFunction && Ranks.TryGetValue(c, out var rank))
            {
                operators.Add(new Operator(c, i, (depth << 8) | rank));
            }
        }
        var sorted = operators.ToArray();
        if (leftToRight)
        {
            Array.Sort(sorted, (a, b) => a.Priority != b.Priority ? a.Priority - b.Priority : b.Position - a.Position);   // the last of a rank splits first
        }
        else
        {
            LegacySort.QuickSort(sorted, (a, b) => a.Priority - b.Priority);
        }
        return Read(text, [.. sorted], 0, text.Length);
    }

    /// <summary>
    /// The first operator of the sorted list within the span splits it. The original sorts with the
    /// framework's unstable quicksort, so among operators of one priority the order is that sort's;
    /// we sort the same way (<see cref="LegacySort"/>).
    /// </summary>
    private static FuzzyExpression Read(string text, List<Operator> operators, int start, int count)
    {
        var index = operators.FindIndex(o => o.Position >= start && o.Position < start + count);
        if (index < 0)
        {
            var span = text.Substring(start, count);
            var first = span.Replace("(", "").Replace(")", "").Trim().Split(' ')[0];
            return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? new FuzzyConstant(number)
                : FuzzyVariable.Read(span);
        }
        var op = operators[index];
        operators.RemoveAt(index);
        var right = Read(text, operators, op.Position + 1, count - (op.Position + 1 - start));
        return op.Symbol == '!'
            ? new FuzzyNot(right)                                             // as the original: what stands left of a ! is dropped
            : new FuzzyBinary(op.Symbol, Read(text, operators, start, op.Position - start), right);
    }
}

/// <summary>
/// The quicksort of .NET Framework 2.0–3.5's generic arrays (median of three, then Hoare
/// partitioning, the smaller side first): the order among equal keys the original's sort leaves.
/// </summary>
internal static class LegacySort
{
    public static void QuickSort<T>(T[] keys, Comparison<T> compare)
    {
        if (keys.Length > 1)
        {
            QuickSort(keys, compare, 0, keys.Length - 1);
        }
    }

    private static void QuickSort<T>(T[] keys, Comparison<T> compare, int left, int right)
    {
        do
        {
            var i = left;
            var j = right;
            var middle = i + ((j - i) >> 1);
            SwapIfGreater(keys, compare, i, middle);
            SwapIfGreater(keys, compare, i, j);
            SwapIfGreater(keys, compare, middle, j);
            var pivot = keys[middle];
            do
            {
                while (compare(keys[i], pivot) < 0)
                {
                    i++;
                }
                while (compare(pivot, keys[j]) < 0)
                {
                    j--;
                }
                if (i > j)
                {
                    break;
                }
                if (i < j)
                {
                    (keys[i], keys[j]) = (keys[j], keys[i]);
                }
                i++;
                j--;
            }
            while (i <= j);
            if (j - left <= right - i)
            {
                if (left < j)
                {
                    QuickSort(keys, compare, left, j);
                }
                left = i;
            }
            else
            {
                if (i < right)
                {
                    QuickSort(keys, compare, i, right);
                }
                right = j;
            }
        }
        while (left < right);
    }

    private static void SwapIfGreater<T>(T[] keys, Comparison<T> compare, int a, int b)
    {
        if (a != b && compare(keys[a], keys[b]) > 0)
        {
            (keys[a], keys[b]) = (keys[b], keys[a]);
        }
    }
}
