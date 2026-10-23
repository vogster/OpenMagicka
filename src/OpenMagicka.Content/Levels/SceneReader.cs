using System.Xml;
using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Levels;

/// <summary>
/// Reads a scene file (docs/formats/LEVELS_TRIGGERS.md §4). Element and attribute names are
/// compared ignoring case (except SkyMap's <c>color</c>); an unknown element at the top is an
/// error, as are two triggers or interactables with the same key.
/// </summary>
public static class SceneReader
{
    /// <param name="path">The scene's content path; its file name is the scene's name and the model is next to it.</param>
    public static SceneDefinition Read(Stream stream, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var document = LegacyXml.Load(stream, "scene");
        var scene = LegacyXml.Children(document, "Scene").FirstOrDefault()
            ?? throw new InvalidDataException("no Scene element in the scene file");

        bool forceNavMesh = false, forceCamera = false;
        foreach (var attribute in LegacyXml.AttributesOf(scene))
        {
            if (LegacyXml.Is(attribute, "forcenavmesh"))
            {
                forceNavMesh = LegacyXml.Bool(attribute.Value);
            }
            else if (LegacyXml.Is(attribute, "forcecamera"))
            {
                forceCamera = LegacyXml.Bool(attribute.Value);
            }
        }

        var settings = SceneSettings.Default;
        string? model = null;
        RulesetDefinition? ruleset = null;
        var waves = new List<WaveDefinition>();
        var triggers = new List<TriggerDefinition>();
        var interactables = new List<InteractableDefinition>();
        var keys = new HashSet<int>();
        foreach (XmlNode child in scene.ChildNodes)
        {
            if (child is XmlComment)
            {
                continue;
            }
            switch (child.Name.ToLowerInvariant())
            {
                case "model": model = Resolve(path, child.InnerText); break;
                case "ruleset":
                    if (ReadRuleset(child) is { } replaced)
                    {
                        ruleset = replaced;
                        waves.Clear();   // the waves belonged to the replaced ruleset object
                    }
                    break;
                case "wave" when ruleset?.Type == RulesetType.Survival: waves.Add(ReadWave(child)); break;
                case "trigger":
                    var trigger = TriggerReader.ReadTrigger(child);
                    triggers.Add(Unique(trigger, trigger.Key, keys));
                    break;
                case "oninteract":
                    var interactable = TriggerReader.ReadInteractable(child);
                    interactables.Add(Unique(interactable, interactable.Key, keys));
                    break;
                default: settings = SceneSettingsReader.Apply(settings, child, path); break;
            }
        }

        return new SceneDefinition(
            Path.GetFileNameWithoutExtension(path.Split('/', '\\')[^1]).ToLowerInvariant(),
            model ?? throw new InvalidDataException("the scene has no <Model>"),
            forceNavMesh, forceCamera, settings, ruleset, waves, triggers, interactables);
    }

    /// <summary>The type attribute picks the ruleset; null for an unknown or missing type (the previous one stays).</summary>
    private static RulesetDefinition? ReadRuleset(XmlNode node)
    {
        RulesetDefinition? ruleset = null;
        foreach (var type in LegacyXml.Attributes(node, "type"))
        {
            RulesetType? parsed = type.ToLowerInvariant() switch
            {
                "survival" => RulesetType.Survival,
                "timedobjective" => RulesetType.TimedObjective,
                "versus" => RulesetType.Versus,
                _ => null,
            };
            ruleset = parsed is { } t ? new RulesetDefinition(t, ScriptNode.From(node)) : ruleset;
        }
        return ruleset;
    }

    /// <summary>
    /// Every child but comments is a block (whatever its name): area (not "any") and delay
    /// attributes, then actions as in a trigger; more than one variant (random group) fails.
    /// </summary>
    private static WaveDefinition ReadWave(XmlNode node)
    {
        var blocks = new List<WaveBlock>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlComment)
            {
                continue;
            }
            string? area = null;
            var delay = 0f;
            foreach (var attribute in LegacyXml.AttributesOf(child))
            {
                if (LegacyXml.Is(attribute, "area"))
                {
                    area = attribute.Value.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : attribute.Value;
                }
                else if (LegacyXml.Is(attribute, "delay"))
                {
                    delay = LegacyXml.Float(attribute.Value);
                }
            }
            var actions = TriggerReader.ReadActions(child);
            if (actions.Random.Count(g => !g.Looped) > 1)
            {
                throw new InvalidDataException("a survival wave block cannot choose between random groups");
            }
            blocks.Add(new WaveBlock(area, delay, actions));
        }
        return new WaveDefinition(blocks);
    }

    private static T Unique<T>(T item, int key, HashSet<int> keys) =>
        keys.Add(key) ? item : throw new InvalidDataException($"two triggers or interactables share the key {key}");

    /// <summary>
    /// A path written relative to the file's directory. Trailing spaces and dots are dropped, as
    /// Windows does when the original opens the file (Tsar_Mountaindale.lvl has
    /// "tmd_s1.xml  "); an empty path names no file.
    /// </summary>
    internal static string Resolve(string path, string relative)
    {
        var trimmed = relative.TrimEnd(' ');
        if (trimmed.Split('/', '\\')[^1].Trim('.').Length > 0)
        {
            trimmed = trimmed.TrimEnd(' ', '.');
        }
        if (trimmed.Length == 0)
        {
            throw new InvalidDataException("empty path");
        }
        return ContentFileSystem.Combine(path, trimmed) ?? throw new InvalidDataException($"\"{relative}\" leaves the content directory");
    }
}

/// <summary>The scene's look and sound elements.</summary>
internal static class SceneSettingsReader
{
    public static SceneSettings Apply(SceneSettings settings, XmlNode node, string path)
    {
        var attributes = node is XmlElement ? LegacyXml.AttributesOf(node).ToArray() : [];
        // The last attribute with one of the names wins; the others are left as they are.
        float F(float current, params string[] names) =>
            attributes.Where(a => names.Any(n => LegacyXml.Is(a, n))).Aggregate(current, (_, a) => LegacyXml.Float(a.Value));
        return node.Name.ToLowerInvariant() switch
        {
            "indoor" => settings with { Indoor = LegacyXml.Bool(node.InnerText) },
            "magnify" => settings with { Magnify = LegacyXml.Float(node.InnerText) },
            "killplaneposition" => settings with { KillPlanePosition = LegacyXml.Float(node.InnerText) },
            "reverb" => attributes.Aggregate(settings, (s, a) => a.Name.ToLowerInvariant() switch
            {
                "roomtype" => s with { RoomType = LegacyXml.Enum<RoomType>(a.Value) },
                "mix" => s with { ReverbMix = LegacyXml.Float(a.Value) },
                _ => s,
            }),
            "fog" => settings with
            {
                Fog = attributes.Aggregate(settings.Fog ?? new FogSettings(0, 0, Vector4.Zero), (f, a) => a.Name.ToLowerInvariant() switch
                {
                    "start" => f with { Start = LegacyXml.Float(a.Value) },
                    "end" => f with { End = LegacyXml.Float(a.Value) },
                    "color" => f with { Color = Color4(a.Value) },
                    _ => f,
                }),
            },
            "filter" => settings with
            {
                Filter = new FilterSettings(F(settings.Filter.Brightness, "brightness"), F(settings.Filter.Contrast, "contrast"),
                    F(settings.Filter.Saturation, "saturation")),
            },
            "bloom" => settings with
            {
                Bloom = new BloomSettings(F(settings.Bloom.Threshold, "threshold"),
                    F(settings.Bloom.Multiplier, "multiplier", "multiplyer"), F(settings.Bloom.Blur, "blur")),
            },
            "rimlight" => settings with
            {
                RimLight = new RimLightSettings(F(settings.RimLight.Glow, "glow"), F(settings.RimLight.Power, "power"), F(settings.RimLight.Bias, "bias")),
            },
            "skymap" => settings with { SkyMap = ReadSkyMap(node, attributes, path) },
            "clouds" => settings with { Clouds = attributes.Aggregate(settings.Clouds, (c, a) => Cloud(c, a, path)) },
            _ => throw new InvalidDataException($"unknown scene element <{node.Name}>"),
        };
    }

    private static SkyMapSettings ReadSkyMap(XmlNode node, XmlAttribute[] attributes, string path)
    {
        var color = Vector3.One;
        foreach (var attribute in attributes.Where(a => a.Name == "color"))
        {
            var parts = Leading(attribute.Value, 3);
            color = new Vector3(parts[0], parts[1], parts[2]);
        }
        return new SkyMapSettings(SceneReader.Resolve(path, node.InnerText), color);
    }

    private static CloudSettings Cloud(CloudSettings clouds, XmlAttribute attribute, string path) => attribute.Name.ToLowerInvariant() switch
    {
        "intensity" => clouds with { Intensity = LegacyXml.Float(attribute.Value) },
        "scale" => clouds with { Scale = Components(attribute.Value, 1, 2) is var s && s.Length == 1 ? new Vector2(s[0]) : new Vector2(s[0], s[1]) },
        "movement" => clouds with { Movement = Pair(Components(attribute.Value, 2, 2)) },
        "texture" => clouds with { Texture = SceneReader.Resolve(path, attribute.Value) },
        "worldrelative" => clouds with { WorldRelative = LegacyXml.Bool(attribute.Value) },
        _ => clouds,
    };

    /// <summary>r,g,b with an optional alpha (default 1); further components are not read, as in the original.</summary>
    private static Vector4 Color4(string value)
    {
        var c = Leading(value, 3, optional: 1);
        return new Vector4(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1);
    }

    /// <summary>The first <paramref name="count"/> (+ optional) comma-separated numbers; later ones are not parsed.</summary>
    private static float[] Leading(string value, int count, int optional = 0)
    {
        var parts = value.Split(',');
        if (parts.Length < count)
        {
            throw new InvalidDataException($"\"{value}\" needs at least {count} comma-separated numbers");
        }
        return parts.Take(count + optional).Select(LegacyXml.Float).ToArray();
    }

    private static Vector2 Pair(float[] xy) => new(xy[0], xy[1]);

    private static float[] Components(string value, int min, int max)
    {
        var parts = value.Split(',');
        if (parts.Length < min || parts.Length > max)
        {
            throw new InvalidDataException($"\"{value}\" needs {min}..{max} comma-separated numbers");
        }
        return parts.Select(LegacyXml.Float).ToArray();
    }
}
