using System.Xml;

namespace OpenMagicka.Content.Localization;

/// <summary>One localized string: its key as written (starting with '#') and its text.</summary>
public sealed record LocEntry(string Key, string Text);

/// <summary>
/// Reads a <c>*.loctable.xml</c> string table, an Excel 2003 SpreadsheetML workbook
/// (docs/engine/ARCHITECTURE.md §10). In every <c>Workbook/Worksheet/Table/Row</c> with at least
/// two child nodes, the text of the first is the key and the text of the second is the string.
/// Cells are taken by position (<c>ss:Index</c> is ignored) and their text includes nested markup.
/// Element names are compared ignoring case. Rows whose key does not start with '#' (headers)
/// and rows with an empty string are skipped. Whitespace-only nodes are dropped, other
/// whitespace is kept as written (<see cref="LegacyXml"/>).
/// </summary>
public static class LocTableReader
{
    public static IReadOnlyList<LocEntry> Read(Stream stream)
    {
        var document = LegacyXml.Load(stream, "string table");
        var workbook = Children(document, "Workbook").FirstOrDefault()
            ?? throw new InvalidDataException("no Workbook element in the string table");

        var entries = new List<LocEntry>();
        foreach (var row in Children(workbook, "Worksheet").SelectMany(w => Children(w, "Table")).SelectMany(t => Children(t, "Row")))
        {
            if (row.ChildNodes.Count < 2)
            {
                continue;
            }
            var key = row.ChildNodes[0]!.InnerText;
            var text = row.ChildNodes[1]!.InnerText;
            if (key.StartsWith('#') && text.Length > 0)
            {
                entries.Add(new LocEntry(key, text));
            }
        }
        return entries;
    }

    private static IEnumerable<XmlNode> Children(XmlNode parent, string name) => LegacyXml.Children(parent, name);
}
