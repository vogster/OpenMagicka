using System.Globalization;
using System.Xml;

namespace OpenMagicka.Content;

/// <summary>
/// Loads the game's XML files the way the original's <c>XmlDocument.Load</c> sees them:
/// whitespace-only nodes dropped and characters not checked (one shipped effect holds
/// <c>&amp;#x1F;</c>). DTDs are refused and nothing external is resolved. Also the original's
/// number formats and node lookups.
/// </summary>
internal static class LegacyXml
{
    public static XmlDocument Load(Stream stream, string what)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CheckCharacters = false,
        };
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);
            return document;
        }
        catch (Exception e) when (e is XmlException or ArgumentException)
        {
            // XmlDocument reports a bad declaration (version="2.0") as an ArgumentException.
            throw new InvalidDataException($"{what} is not valid XML: {e.Message}", e);
        }
    }

    /// <summary>Child nodes whose (prefixed) name equals <paramref name="name"/> ignoring case.</summary>
    public static IEnumerable<XmlNode> Children(XmlNode parent, string name) =>
        parent.ChildNodes.Cast<XmlNode>().Where(n => Is(n, name));

    public static bool Is(XmlNode node, string name) => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The node's attributes; text, comments and processing instructions have none, and the
    /// original fails where it expects them.
    /// </summary>
    public static IEnumerable<XmlAttribute> AttributesOf(XmlNode node) =>
        node.Attributes?.Cast<XmlAttribute>() ?? throw new InvalidDataException($"{node.NodeType} node \"{node.Name}\" where an element is expected");

    /// <summary>Values of the attributes with this name ignoring case, in document order.</summary>
    public static IEnumerable<string> Attributes(XmlNode node, string name) =>
        AttributesOf(node).Where(a => Is(a, name)).Select(a => a.Value);

    /// <summary>
    /// .NET Framework <c>float.Parse(s, InvariantCulture)</c>: Float and AllowThousands. A number
    /// outside the float range overflows there (modern .NET returns infinity); the infinity
    /// symbols themselves are accepted.
    /// </summary>
    public static float Float(string value) => Parse(value, v =>
    {
        var result = float.Parse(v, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        if (float.IsInfinity(result) && !v.Trim().TrimStart('+', '-').Equals("Infinity", StringComparison.OrdinalIgnoreCase))
        {
            throw new OverflowException($"{v} is outside the float range");
        }
        return result;
    });

    /// <summary><c>int.Parse(s)</c>: optional whitespace and sign.</summary>
    public static int Int(string value) => Parse(value, v => int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static byte Byte(string value) => Parse(value, v => byte.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static bool Bool(string value) => Parse(value, bool.Parse);

    /// <summary><c>Enum.Parse(type, s, ignoreCase: true)</c>.</summary>
    public static T Enum<T>(string value) where T : struct, System.Enum => Parse(value, v => System.Enum.Parse<T>(v, ignoreCase: true));

    private static T Parse<T>(string value, Func<string, T> parse)
    {
        try
        {
            return parse(value);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"cannot read \"{value}\" as {typeof(T).Name}", e);
        }
    }
}
