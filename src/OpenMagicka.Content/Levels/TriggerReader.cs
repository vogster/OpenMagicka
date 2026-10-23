using System.Globalization;
using System.Xml;

namespace OpenMagicka.Content.Levels;

/// <summary>
/// Reads <c>&lt;Trigger&gt;</c> and <c>&lt;OnInteract&gt;</c> (docs/formats/LEVELS_TRIGGERS.md §5).
/// Actions and conditions stay <see cref="ScriptNode"/>s for the trigger engine; what makes the
/// original fail while loading them structurally is rejected here.
/// </summary>
internal static class TriggerReader
{
    public static TriggerDefinition ReadTrigger(XmlNode node)
    {
        string? id = null;
        var autoRun = true;
        var repeat = -1f;
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "id": id = attribute.Value.ToLowerInvariant(); break;
                case "autorun": autoRun = LegacyXml.Bool(attribute.Value); break;
                case "repeat": repeat = ParseRepeat(attribute.Value); break;
                default: throw new InvalidDataException($"unexpected attribute \"{attribute.Name}\" on a trigger");
            }
        }

        var conditions = new List<IReadOnlyList<ScriptNode>>();
        ActionBlock? actions = null;
        foreach (XmlNode child in node.ChildNodes)
        {
            if (LegacyXml.Is(child, "If"))
            {
                conditions.Add(ReadConditions(child));
            }
            else if (LegacyXml.Is(child, "Then"))
            {
                actions = ReadActions(child);
            }
        }
        // The original leaves the actions unset and fails when the scene initializes its triggers.
        return new TriggerDefinition(id, Key(id), autoRun, repeat, conditions,
            actions ?? throw new InvalidDataException($"trigger \"{id}\" has no <then>"));
    }

    public static InteractableDefinition ReadInteractable(XmlNode node)
    {
        string? locator = null;
        var type = InteractType.None;
        var enabled = true;
        var anim = new List<IReadOnlyList<string>>();
        var phys = new List<string>();
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "locator": locator = attribute.Value.ToLowerInvariant(); break;
                case "type": type = LegacyXml.Enum<InteractType>(attribute.Value); break;
                case "enabled": enabled = LegacyXml.Bool(attribute.Value); break;
                case "animhighlight": anim.AddRange(List(attribute.Value).Select(path => (IReadOnlyList<string>)path.Split('/'))); break;
                case "physhighlight": phys.AddRange(List(attribute.Value)); break;
                default: throw new InvalidDataException($"unexpected attribute \"{attribute.Name}\" on <{node.Name}>");
            }
        }
        return new InteractableDefinition(locator, Key(locator), type, enabled, anim, phys, ReadActions(node));
    }

    /// <summary>
    /// <c>float.TryParse</c> with NumberStyles.Number (no exponent), else a bool (true = 0, every
    /// frame), else -1 (once): the original only logs the bad value.
    /// </summary>
    private static float ParseRepeat(string value) =>
        float.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var seconds) && !Overflowed(seconds, value) ? seconds
        : bool.TryParse(value, out var every) && every ? 0f
        : -1f;

    /// <summary>.NET Framework's TryParse fails on a number outside the float range; modern .NET gives infinity.</summary>
    private static bool Overflowed(float result, string value) =>
        float.IsInfinity(result) && !value.Trim().TrimStart('+', '-').Equals("Infinity", StringComparison.OrdinalIgnoreCase);

    /// <summary>A comma list, lowered and without spaces.</summary>
    private static string[] List(string value) => value.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Split(',');

    private static int Key(string? id) => id is null ? 0 : LegacyStringHash.Compute(id);

    /// <summary>One AND block: every child but comments is a condition.</summary>
    private static IReadOnlyList<ScriptNode> ReadConditions(XmlNode node) =>
        node.ChildNodes.Cast<XmlNode>().Where(c => c is not XmlComment).Select(c => Element(c, "condition")).ToArray();

    /// <summary>
    /// Plain actions, <c>&lt;random&gt;</c> groups and <c>&lt;looped&gt;</c> blocks (which hold the
    /// same two). Comments are skipped only at the top: inside random and looped the original
    /// takes them for actions and fails.
    /// </summary>
    public static ActionBlock ReadActions(XmlNode node)
    {
        var regular = new List<ScriptAction>();
        var random = new List<RandomGroup>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlComment)
            {
                continue;
            }
            if (LegacyXml.Is(child, "Looped"))
            {
                foreach (XmlNode inner in child.ChildNodes)
                {
                    Add(inner, looped: true);
                }
            }
            else
            {
                Add(child, looped: false);
            }
        }
        return new ActionBlock(regular, random);

        void Add(XmlNode action, bool looped)
        {
            if (LegacyXml.Is(action, "Random"))
            {
                random.Add(new RandomGroup(action.ChildNodes.Cast<XmlNode>().Select(a => Element(a, "action")).ToArray(), looped));
            }
            else
            {
                regular.Add(new ScriptAction(Element(action, "action"), looped));
            }
        }
    }

    private static ScriptNode Element(XmlNode node, string what) =>
        node is XmlElement ? ScriptNode.From(node) : throw new InvalidDataException($"{node.NodeType} node where an {what} is expected");
}
