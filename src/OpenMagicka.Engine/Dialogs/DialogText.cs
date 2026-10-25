using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;

namespace OpenMagicka.Engine.Dialogs;

/// <summary>
/// A dialog line's text, as the original resolves it when the level loads (docs/formats/LEVELS_TRIGGERS.md §11): the loc
/// string of its key, each <c>#N;</c> replaced by the string of its N-th argument and each other <c>#key;</c> by that key's
/// string, in white; what is put in is scanned again, from the start. Then wrapped to the message's width.
/// </summary>
public static class DialogText
{
    public const string White = "[c=1,1,1]", ColorEnd = "[/c]";

    /// <summary>How many references a line may take in (a cycle would never end).</summary>
    public const int MaxReferences = StringTable.MaxExpansions;

    public static string Resolve(DialogLine line, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(strings);
        var text = strings.Get(line.Key);
        for (var taken = 0; taken < MaxReferences && text.Length < StringTable.MaxExpandedLength; taken++)
        {
            var start = text.IndexOf('#');
            var end = start < 0 ? -1 : text.IndexOf(';', start);
            if (end < 0)
            {
                break;
            }
            var token = text[(start + 1)..end];
            var key = int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n >= 1 && n <= line.Arguments.Count ? StringTable.Id(line.Arguments[n - 1]) : (int?)null
                : StringTable.Id("#" + token);
            text = string.Concat(text.AsSpan(0, start), key is { } id ? White + strings.Get(id) + ColorEnd : "", text.AsSpan(end + 1));
        }
        return text;
    }
}
