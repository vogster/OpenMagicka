using System.Xml;

namespace OpenMagicka.Content.Levels;

public readonly record struct ScriptAttribute(string Name, string Value);

/// <summary>
/// An immutable copy of an XML node of a level script (an action, a condition, a ruleset or
/// wave block), kept for the trigger engine to interpret. Elements keep their attributes in
/// document order and their child nodes (elements, and text as "#text" nodes); Text is the
/// node's inner text. Whitespace-only nodes are dropped, as in the original.
/// </summary>
public sealed class ScriptNode
{
    private ScriptNode(XmlNodeType type, string name, IReadOnlyList<ScriptAttribute> attributes, IReadOnlyList<ScriptNode> children, string text)
    {
        Type = type;
        Name = name;
        Attributes = attributes;
        Children = children;
        Text = text;
    }

    public XmlNodeType Type { get; }

    /// <summary>The element name as written; "#text", "#comment" and so on for other nodes.</summary>
    public string Name { get; }

    public IReadOnlyList<ScriptAttribute> Attributes { get; }

    public IReadOnlyList<ScriptNode> Children { get; }

    public string Text { get; }

    /// <summary>The value of the first attribute with this name ignoring case, or null.</summary>
    public string? Attribute(string name) =>
        Attributes.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } a ? a.Value : null;

    public bool Is(string name) => Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>A node from a document checked by <see cref="LegacyXml.Load"/>, so its depth is bounded.</summary>
    internal static ScriptNode From(XmlNode node)
    {
        var attributes = node.Attributes is { } list
            ? list.Cast<XmlAttribute>().Select(a => new ScriptAttribute(a.Name, a.Value)).ToArray()
            : [];
        var children = node.ChildNodes.Cast<XmlNode>().Select(From).ToArray();
        // InnerText, built from the children: element text joins its children's, without
        // comments and processing instructions; other nodes hold their value.
        var text = node is XmlElement
            ? string.Concat(children.Where(c => c.Type is not (XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)).Select(c => c.Text))
            : node.Value ?? "";
        return new ScriptNode(node.NodeType, node.Name, Array.AsReadOnly(attributes), Array.AsReadOnly(children), text);
    }
}
