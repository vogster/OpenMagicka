using System.Xml;
using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Particles;

/// <summary>
/// Reads a visual effect from <c>Effects/**/*.xml</c> the way the original does
/// (docs/engine/ARCHITECTURE.md §5.2). Values the original cannot parse, and the node layouts
/// that make it fail, are <see cref="InvalidDataException"/>s.
/// </summary>
public static class VisualEffectReader
{
    private const int DefaultKeyFramesPerSecond = 10;
    private const int DefaultVersion = 1;

    public static VisualEffectContent Read(Stream stream)
    {
        var document = LegacyXml.Load(stream, "visual effect");
        var type = VisualEffectType.Single;
        var duration = 0f;
        var keyFramesPerSecond = DefaultKeyFramesPerSecond;
        var version = DefaultVersion;
        var emitters = new List<EmitterContent>();
        EffectLightContent? light = null;

        foreach (var effect in LegacyXml.Children(document, "Effect"))
        {
            foreach (var attribute in LegacyXml.AttributesOf(effect))
            {
                if (LegacyXml.Is(attribute, "type"))
                {
                    type = LegacyXml.Enum<VisualEffectType>(attribute.Value);
                }
                else if (LegacyXml.Is(attribute, "duration"))
                {
                    duration = LegacyXml.Float(attribute.Value);
                }
                else if (LegacyXml.Is(attribute, "keyFramesPerSecond"))
                {
                    keyFramesPerSecond = LegacyXml.Int(attribute.Value);
                }
                else if (LegacyXml.Is(attribute, "version"))
                {
                    version = LegacyXml.Int(attribute.Value);
                }
            }
            foreach (XmlNode child in effect.ChildNodes)
            {
                // Emitter names are class names in the original, so they are case-sensitive.
                if (child.Name == "ContinuousEmitter")
                {
                    emitters.Add(EmitterReader.Read(child, EmitterKind.Continuous, keyFramesPerSecond, version));
                }
                else if (child.Name == "PulseEmitter")
                {
                    emitters.Add(EmitterReader.Read(child, EmitterKind.Pulse, keyFramesPerSecond, version));
                }
                else if (LegacyXml.Is(child, "Light"))
                {
                    light = ReadLight(child, light ?? EffectLightContent.Off);
                }
            }
        }
        return new VisualEffectContent(type, duration, emitters, light);
    }

    private static EffectLightContent ReadLight(XmlNode node, EffectLightContent light)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlComment)
            {
                continue;
            }
            foreach (var value in LegacyXml.Attributes(child, "Value"))
            {
                light = Apply(light, child.Name.ToLowerInvariant(), value);
            }
        }
        return light;
    }

    private static EffectLightContent Apply(EffectLightContent light, string property, string value) => property switch
    {
        "positionx" => light with { Offset = light.Offset with { X = LegacyXml.Float(value) } },
        "positiony" => light with { Offset = light.Offset with { Y = LegacyXml.Float(value) } },
        "positionz" => light with { Offset = light.Offset with { Z = LegacyXml.Float(value) } },
        "diffuser" => light with { Diffuse = light.Diffuse with { X = LegacyXml.Float(value) } },
        "diffuseg" => light with { Diffuse = light.Diffuse with { Y = LegacyXml.Float(value) } },
        "diffuseb" => light with { Diffuse = light.Diffuse with { Z = LegacyXml.Float(value) } },
        "ambientr" => light with { Ambient = light.Ambient with { X = LegacyXml.Float(value) } },
        "ambientg" => light with { Ambient = light.Ambient with { Y = LegacyXml.Float(value) } },
        "ambientb" => light with { Ambient = light.Ambient with { Z = LegacyXml.Float(value) } },
        "specular" => light with { Specular = LegacyXml.Float(value) },
        "radius" => light with { Radius = LegacyXml.Float(value) },
        "variationtype" => light with { VariationType = LegacyXml.Enum<LightVariationType>(value) },
        "variationspeed" => light with { VariationSpeed = LegacyXml.Float(value) },
        "variationamount" => light with { VariationAmount = LegacyXml.Float(value) },
        "spawntransitiontype" => light with { SpawnTransition = LegacyXml.Enum<LightTransitionType>(value) },
        "spawntransitiontime" => light with { SpawnTransitionTime = LegacyXml.Float(value) },
        "despawntransitiontype" => light with { DespawnTransition = LegacyXml.Enum<LightTransitionType>(value) },
        "despawntransitiontime" => light with { DespawnTransitionTime = LegacyXml.Float(value) },
        _ => light,
    };
}
