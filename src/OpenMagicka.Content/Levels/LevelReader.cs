using System.Xml;

namespace OpenMagicka.Content.Levels;

/// <summary>
/// Reads a level file (<c>.lvl</c>) with everything the original's two readers take from it: the
/// menu's (names, loading image, avatars, scenes) and the game's (start, dialogs, extra items).
/// Element and attribute names are compared ignoring case; unknown elements and attributes are
/// ignored; when an element repeats, the last one wins (scenes accumulate).
/// docs/formats/LEVELS_TRIGGERS.md §3.
/// </summary>
public static class LevelReader
{
    /// <param name="path">The level's content path; scene and dialog paths are relative to its directory.</param>
    public static LevelDefinition Read(Stream stream, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var document = LegacyXml.Load(stream, "level");
        var level = LegacyXml.Children(document, "Level").FirstOrDefault()
            ?? throw new InvalidDataException("no Level element in the level file");

        string? name = null, description = null, shortDescription = null, loading = null, dialogs = null;
        var preferredAvatar = LevelDefinition.DefaultAvatar;
        var start = LevelStart.Default;
        var scenes = new List<string>();
        var allowedAvatars = new List<string>();
        var items = new List<AvatarItem>();
        var scenesSeen = false;
        foreach (XmlNode child in level.ChildNodes)
        {
            switch (child.Name.ToLowerInvariant())
            {
                // The menu's streaming reader takes these only before the first <Scenes>.
                case "name" when !scenesSeen: name = child.InnerText; break;
                case "description" when !scenesSeen: description = child.InnerText; break;
                case "shortdescription": shortDescription = child.InnerText; break;
                case "loading" when !scenesSeen: loading = child.InnerText; break;
                case "start": start = ReadStart(child, start); break;
                case "dialogs": dialogs = Resolve(path, child.InnerText); break;
                case "scenes": scenes.AddRange(ReadScenes(child, path)); scenesSeen = true; break;
                case "preferredavatar": preferredAvatar = Required(child.InnerText, "PreferredAvatar").ToLowerInvariant(); break;
                case "allowedavatars": allowedAvatars.AddRange(ReadAvatars(child)); break;
                case "additionalitems": items.AddRange(ReadItems(child)); break;
            }
        }

        description = Required(description, "Description");
        return new LevelDefinition(
            Required(name, "Name"), description, string.IsNullOrEmpty(shortDescription) ? description : shortDescription,
            Required(loading, "Loading"), start, dialogs,
            scenes.Count > 0 ? scenes : throw new InvalidDataException("the level has no scenes"),
            preferredAvatar, allowedAvatars, items);
    }

    private static LevelStart ReadStart(XmlNode node, LevelStart start)
    {
        foreach (var attribute in LegacyXml.AttributesOf(node))
        {
            start = attribute.Name.ToLowerInvariant() switch
            {
                "scene" => start with { Scene = attribute.Value.ToLowerInvariant() },
                "area" => start with { Area = attribute.Value },
                "spawnplayers" => start with { SpawnPlayers = LegacyXml.Bool(attribute.Value) },
                "noitems" => start with { NoItems = LegacyXml.Bool(attribute.Value) },
                "forcecamera" => start with { ForceCamera = LegacyXml.Bool(attribute.Value) },
                "spawnfairy" => start with { SpawnFairy = LegacyXml.Bool(attribute.Value) },
                _ => start,
            };
        }
        return start;
    }

    /// <summary>Every child but comments must be a Scene element naming a scene file.</summary>
    private static IEnumerable<string> ReadScenes(XmlNode node, string path)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlComment)
            {
                continue;
            }
            if (!LegacyXml.Is(child, "Scene") || child is not XmlElement)
            {
                throw new InvalidDataException($"<{child.Name}> where a <Scene> is expected in <Scenes>");
            }
            yield return Resolve(path, Required(child.InnerText, "Scene"));
        }
    }

    private static IEnumerable<string> ReadAvatars(XmlNode node) =>
        LegacyXml.Children(node, "Avatar").Select(a => Required(a.InnerText, "Avatar").ToLowerInvariant()).ToArray();

    /// <summary>Item elements with both an item and a bone; others are dropped, as in the original.</summary>
    private static IEnumerable<AvatarItem> ReadItems(XmlNode node)
    {
        foreach (var child in LegacyXml.Children(node, "Item").Where(c => c is XmlElement))
        {
            string? item = null, bone = null;
            foreach (var attribute in LegacyXml.AttributesOf(child))
            {
                if (LegacyXml.Is(attribute, "item"))
                {
                    item = attribute.Value;
                }
                else if (LegacyXml.Is(attribute, "bone"))
                {
                    bone = attribute.Value;
                }
            }
            if (!string.IsNullOrEmpty(item) && !string.IsNullOrEmpty(bone))
            {
                yield return new AvatarItem(item, bone);
            }
        }
    }

    private static string Required(string? text, string element) =>
        string.IsNullOrEmpty(text) ? throw new InvalidDataException($"the level's <{element}> is missing or empty") : text;

    private static string Resolve(string path, string relative) => SceneReader.Resolve(path, relative);
}
