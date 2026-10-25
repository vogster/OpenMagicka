using System.Collections.Frozen;
using System.Globalization;
using System.Xml;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Content.Levels;

/// <summary>
/// Reads a dialog file (<c>Levels/Dialog_*.xml</c>, docs/formats/LEVELS_TRIGGERS.md §11). The
/// schema is strict like the original's: an unknown element or attribute at the Dialog,
/// Interact or message level is an error. Element names are compared ignoring case.
/// </summary>
public static class DialogReader
{
    private const int DefaultWidth = 600;

    public static IReadOnlyList<DialogDefinition> Read(Stream stream)
    {
        var document = LegacyXml.Load(stream, "dialog file");
        var root = LegacyXml.Children(document, "Dialogs").FirstOrDefault()
            ?? throw new InvalidDataException("no Dialogs element in the dialog file");
        var dialogs = new List<DialogDefinition>();
        var ids = new Dictionary<int, string?>();
        foreach (var node in Children(root, "Dialog"))
        {
            var dialog = ReadDialog(node);
            if (!ids.TryAdd(dialog.Key, dialog.Id))
            {
                throw new InvalidDataException($"dialogs {Describe(ids[dialog.Key])} and {Describe(dialog.Id)} share the key {dialog.Key}");
            }
            dialogs.Add(dialog);
        }
        return ReadOnly.List(dialogs);
    }

    private static DialogDefinition ReadDialog(XmlNode node)
    {
        string? id = null;
        var icon = InteractType.Talk;
        var repeatFrom = 0;
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "id": id = attribute.Value.ToLowerInvariant(); break;
                case "icontext": icon = LegacyXml.Enum<InteractType>(attribute.Value); break;
                case "repeatfrom": repeatFrom = LegacyXml.Int(attribute.Value); break;
                default: throw Unexpected(attribute, node);
            }
        }
        var interacts = Children(node, "Interact").Select(i => ReadInteract(i, icon)).ToArray();
        return new DialogDefinition(id, id is null ? 0 : LegacyStringHash.Compute(id), icon, repeatFrom, interacts);
    }

    private static DialogInteract ReadInteract(XmlNode node, InteractType icon)
    {
        bool locks = false, focusOwner = true;
        var count = 1;
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "lock": locks = LegacyXml.Bool(attribute.Value); break;
                case "focusowner": focusOwner = LegacyXml.Bool(attribute.Value); break;
                case "count": count = LegacyXml.Int(attribute.Value); break;
                default: throw Unexpected(attribute, node);
            }
        }
        return new DialogInteract(locks, focusOwner, count, icon, Children(node, "message").Select(ReadMessage).ToArray());
    }

    /// <summary>The window's flags use TryParse: a bad bool means false, a bad width keeps 600.</summary>
    private static DialogMessage ReadMessage(XmlNode node)
    {
        string? character = null;
        var type = MessageType.Say;
        bool facing = true, forceOnScreen = true;
        var width = DefaultWidth;
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "character": character = attribute.Value.ToLowerInvariant(); break;
                case "type": type = LegacyXml.Enum<MessageType>(attribute.Value); break;
                case "forceonscreen": bool.TryParse(attribute.Value, out forceOnScreen); break;
                case "facing": bool.TryParse(attribute.Value, out facing); break;
                case "width": width = int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : width; break;
                default: throw Unexpected(attribute, node);
            }
        }
        // Every child node is a line whatever its name; comments and text have no attributes and fail.
        return new DialogMessage(character, type, facing, forceOnScreen, width, node.ChildNodes.Cast<XmlNode>().Select(ReadLine).ToArray());
    }

    private static DialogLine ReadLine(XmlNode node)
    {
        var ttl = -1f;
        var animation = Animation.talk_casual0;
        var bank = SoundBanks.Characters;
        string? sound = null;
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "ttl": ttl = LegacyXml.Float(attribute.Value); break;
                case "anim": animation = LegacyXml.Enum<Animation>(attribute.Value); break;
                case "sound":
                    var parts = attribute.Value.ToLowerInvariant().Split('/');
                    (bank, sound) = parts.Length switch
                    {
                        1 => (bank, parts[0]),
                        2 => (LegacyXml.Enum<SoundBanks>(parts[0]), parts[1]),
                        _ => throw new InvalidDataException($"invalid sound \"{attribute.Value}\": expected [bank/]cue"),
                    };
                    break;
            }
        }
        var text = node.InnerText.ToLowerInvariant().Split(',');
        return new DialogLine(text[0], text[1..], bank, sound, animation, ttl);
    }

    /// <summary>Children other than comments; each must have the given name.</summary>
    private static IEnumerable<XmlNode> Children(XmlNode parent, string name) =>
        parent.ChildNodes.Cast<XmlNode>().Where(n => n is not XmlComment)
            .Select(n => n is XmlElement && LegacyXml.Is(n, name) ? n
                : throw new InvalidDataException($"{n.NodeType} \"{n.Name}\" where a <{name}> is expected"));

    internal static string Describe(string? id) => id is null ? "(no id)" : $"\"{id}\"";

    private static InvalidDataException Unexpected(XmlAttribute attribute, XmlNode node) =>
        new($"unexpected attribute \"{attribute.Name}\" on <{node.Name}>");
}

/// <summary>
/// The dialogs of a level: its own file merged with <c>Levels/Dialog_Fairy.xml</c>, which is
/// always loaded. A dialog id in both is an error, as in the original.
/// </summary>
public sealed class DialogCollection
{
    public const string FairyDialogs = "Levels/Dialog_Fairy.xml";

    private readonly FrozenDictionary<int, DialogDefinition> dialogs;

    private DialogCollection(FrozenDictionary<int, DialogDefinition> dialogs) => this.dialogs = dialogs;

    public int Count => dialogs.Count;

    /// <param name="levelDialogs">The level's dialog file (content path), or null when it has none.</param>
    public static DialogCollection Load(ContentFileSystem files, string? levelDialogs)
    {
        ArgumentNullException.ThrowIfNull(files);
        var merged = new Dictionary<int, DialogDefinition>();
        foreach (var path in levelDialogs is null ? [FairyDialogs] : new[] { levelDialogs, FairyDialogs })
        {
            foreach (var dialog in Read(files, path))
            {
                if (!merged.TryAdd(dialog.Key, dialog))
                {
                    throw new InvalidDataException($"{path}: dialog {DialogReader.Describe(dialog.Id)} is already defined");
                }
            }
        }
        return new DialogCollection(merged.ToFrozenDictionary());
    }

    /// <summary>Dialogs given as they are (a test's); a key twice is an error.</summary>
    public static DialogCollection Of(IEnumerable<DialogDefinition> dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var merged = new Dictionary<int, DialogDefinition>();
        foreach (var dialog in dialogs)
        {
            if (!merged.TryAdd(dialog.Key, dialog))
            {
                throw new InvalidDataException($"dialog {DialogReader.Describe(dialog.Id)} is already defined");
            }
        }
        return new DialogCollection(merged.ToFrozenDictionary());
    }

    public DialogDefinition Get(int key) =>
        dialogs.TryGetValue(key, out var dialog) ? dialog : throw new KeyNotFoundException($"no dialog with key {key}");

    public bool TryGet(int key, out DialogDefinition dialog) => dialogs.TryGetValue(key, out dialog!);

    public bool TryGet(string id, out DialogDefinition dialog)
    {
        ArgumentNullException.ThrowIfNull(id);
        return TryGet(LegacyStringHash.Lower(id), out dialog);
    }

    private static IReadOnlyList<DialogDefinition> Read(ContentFileSystem files, string path)
    {
        try
        {
            using var stream = new MemoryStream(files.ReadAllBytes(path));
            return DialogReader.Read(stream);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }
}
