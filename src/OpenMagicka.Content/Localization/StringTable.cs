using System.Collections.Frozen;
using System.Globalization;

namespace OpenMagicka.Content.Localization;

/// <summary>
/// The strings of one language, keyed by ID = legacy hash of the lower-case key including its
/// '#' (docs/engine/ARCHITECTURE.md §10). Texts may reference other strings as <c>#key;</c>.
/// The key <c>#players</c> always yields the list of connected players. Immutable.
/// </summary>
public sealed class StringTable
{
    /// <summary>Nesting limit for references; the original recurses without one and overflows on a cycle.</summary>
    public const int MaxReferenceDepth = 16;

    /// <summary>Upper bound on references expanded in one call (the shipped strings need a few).</summary>
    public const int MaxExpansions = 1024;

    /// <summary>
    /// A replacement that would make a text longer than this (in chars) is not made, and the scan
    /// stops; with <see cref="MaxExpansions"/> this bounds the time and memory of one call.
    /// </summary>
    public const int MaxExpandedLength = 1 << 16;

    private const string WhiteText = "[c=1,1,1]";
    private const string ColorEnd = "[/c]";

    private static readonly int PlayersId = Id("#players");

    private readonly FrozenDictionary<int, string> strings;
    private readonly string players;

    private StringTable(FrozenDictionary<int, string> strings, string players)
    {
        this.strings = strings;
        this.players = players;
    }

    public static StringTable Empty { get; } = new(FrozenDictionary<int, string>.Empty, "");

    public int Count => strings.Count;

    public static int Id(string key) => LegacyStringHash.Lower(key);

    /// <summary>A table of the given entries; a later entry replaces an earlier one with the same ID.</summary>
    public static StringTable Create(IEnumerable<LocEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            map[Id(entry.Key)] = entry.Text;
        }
        return new StringTable(map.ToFrozenDictionary(), "");
    }

    /// <summary>The same strings with the text that <c>#players</c> stands for.</summary>
    public StringTable WithPlayers(string playerList)
    {
        ArgumentNullException.ThrowIfNull(playerList);
        return new StringTable(strings, playerList);
    }

    /// <summary>Player names joined as "A, B &amp; C".</summary>
    public static string PlayerList(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count <= 1
            ? string.Concat(names)
            : string.Join(", ", names.Take(names.Count - 1)) + " & " + names[^1];
    }

    /// <summary>The text exactly as stored in the table (not <c>#players</c>).</summary>
    public bool TryGetRaw(int id, out string text) => strings.TryGetValue(id, out text!);

    /// <summary>The text with each written <c>\n</c> turned into a line break; references are kept.</summary>
    public string Get(int id) =>
        id == PlayersId ? players
        : strings.TryGetValue(id, out var text) ? text.Replace(@"\n", "\n", StringComparison.Ordinal)
        : NotFound(id);

    public string Get(string key) => Get(Id(key));

    /// <summary>
    /// The text with every <c>#key;</c> replaced by that string, recursively, each wrapped in
    /// <paramref name="colorCode"/> ... <c>[/c]</c> when a colour is given. Written <c>\n</c> stay as they are.
    /// </summary>
    public string GetWithReferences(int id, string? colorCode = null)
    {
        return Expand(id, colorCode, 0, new Budget());
    }

    /// <summary>Replaces every <c>#key;</c> in free text by that string (<see cref="Get(int)"/>) in white.</summary>
    public string ParseReferences(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ReplaceReferences(text, id => WhiteText + Get(id) + ColorEnd, new Budget());
    }

    /// <summary>A text colour code "[c=r,g,b]" or "[c=r,g,b,a]" with the original's number format.</summary>
    public static string ColorCode(params ReadOnlySpan<float> components)
    {
        if (components.Length is not (3 or 4))
        {
            throw new ArgumentException("a colour has 3 or 4 components", nameof(components));
        }
        var parts = new string[components.Length];
        for (var i = 0; i < components.Length; i++)
        {
            parts[i] = components[i].ToString("G7", CultureInfo.InvariantCulture);
        }
        return "[c=" + string.Join(',', parts) + "]";
    }

    private string Expand(int id, string? colorCode, int depth, Budget budget)
    {
        if (id == PlayersId)
        {
            return players;
        }
        if (!strings.TryGetValue(id, out var text))
        {
            return NotFound(id);
        }
        return ReplaceReferences(text, reference =>
        {
            var inner = depth < MaxReferenceDepth ? Expand(reference, colorCode, depth + 1, budget) : "";
            return colorCode is null ? inner : colorCode + inner + ColorEnd;
        }, budget);
    }

    /// <summary>
    /// The original's scan: take the first '#', and the first ';' after it; if there is one, the
    /// text between them (with the '#') is a key, replaced, and the scan restarts at the beginning.
    /// The text before the replaced '#' holds no '#', so restarting at that '#' finds the same one.
    /// </summary>
    private static string ReplaceReferences(string text, Func<int, string> replacement, Budget budget)
    {
        for (var start = text.IndexOf('#'); start >= 0 && budget.Left > 0; start = text.IndexOf('#', start))
        {
            var end = text.IndexOf(';', start + 1);
            if (end < 0)
            {
                break;
            }
            budget.Left--;
            var inserted = replacement(Id(text[start..end]));
            if ((long)text.Length - (end + 1 - start) + inserted.Length > MaxExpandedLength)
            {
                break;
            }
            text = string.Concat(text.AsSpan(0, start), inserted, text.AsSpan(end + 1));
        }
        return text;
    }

    private static string NotFound(int id) => "NOT FOUND: " + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>References still allowed to be expanded in the current call.</summary>
    private sealed class Budget
    {
        public int Left = MaxExpansions;
    }
}
