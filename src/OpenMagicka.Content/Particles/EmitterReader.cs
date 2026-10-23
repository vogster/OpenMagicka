using System.Collections.Frozen;
using System.Xml;
using Microsoft.Xna.Framework;
using P = OpenMagicka.Content.Particles.EmitterParameter;

namespace OpenMagicka.Content.Particles;

/// <summary>Reads one <c>ContinuousEmitter</c> or <c>PulseEmitter</c> element (docs/engine/ARCHITECTURE.md §5.2).</summary>
internal static class EmitterReader
{
    private const string ValueAttribute = "value";

    /// <summary>Parameters read in degrees; a continuous emitter must have them all.</summary>
    private static readonly P[] Angles =
    [
        P.RotationY, P.HueMin, P.HueMax, P.RotationMin, P.RotationMax, P.RotationSpeedMin, P.RotationSpeedMax,
        P.SpreadConeAngle, P.SpreadArcHorizontalAngle, P.SpreadArcVerticalMin, P.SpreadArcVerticalMax,
    ];

    /// <summary>Version 1 colours: these become constant 1 after ColorA* are taken from Alpha*.</summary>
    private static readonly P[] Version1Ones =
    [
        P.ColorR, P.ColorG, P.ColorB, P.ValueMin, P.ValueMax, P.ValueDistribution, P.AlphaMin, P.AlphaMax, P.AlphaDistribution,
    ];

    /// <summary>Parameter names per emitter type; matched case-sensitively, as the original's field lookup is.</summary>
    private static readonly FrozenDictionary<string, P> ContinuousParameters =
        Enum.GetValues<P>().Where(p => p != P.TriggerTime).ToFrozenDictionary(p => p.ToString(), StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, P> PulseParameters =
        Enum.GetValues<P>().Where(p => p != P.ParticlesPerSecond).ToFrozenDictionary(p => p.ToString(), StringComparer.Ordinal);

    public static EmitterContent Read(XmlNode node, EmitterKind kind, int keyFramesPerSecond, int version)
    {
        var names = kind == EmitterKind.Pulse ? PulseParameters : ContinuousParameters;
        var parameters = new Dictionary<P, ParameterCurve>();
        var state = new FlagState();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (names.TryGetValue(child.Name, out var parameter))
            {
                parameters[parameter] = kind == EmitterKind.Pulse ? ReadConstant(child) : ReadCurve(child, keyFramesPerSecond);
            }
            else
            {
                ReadFlag(child, kind, version, state);
            }
        }

        if (version < 2)
        {
            Move(parameters, P.AlphaMin, P.ColorAMin);
            Move(parameters, P.AlphaMax, P.ColorAMax);
            Move(parameters, P.AlphaDistribution, P.ColorADistribution);
            foreach (var parameter in Version1Ones)
            {
                parameters[parameter] = ParameterCurve.Constant(1);
            }
        }
        foreach (var angle in Angles)
        {
            if (!parameters.TryGetValue(angle, out var curve))
            {
                // A pulse parameter is a float field, 0 when absent; a continuous one is a key
                // array, and the original fails converting an absent one.
                if (kind == EmitterKind.Pulse)
                {
                    continue;
                }
                throw new InvalidDataException($"continuous emitter without {angle}");
            }
            parameters[angle] = new ParameterCurve(curve.Keys.Select(k => k with { Value = MathHelper.ToRadians(k.Value) }).ToArray());
        }
        return new EmitterContent(kind, state.Flags, state.Particle, state.NrOfParticles, parameters.ToFrozenDictionary());
    }

    /// <summary>A key list with child nodes, else a constant from the value attribute.</summary>
    private static ParameterCurve ReadCurve(XmlNode node, int keyFramesPerSecond)
    {
        if (!node.HasChildNodes)
        {
            return ReadConstant(node);
        }
        var keys = new Keyframe[node.ChildNodes.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = node.ChildNodes[i]!;
            if (key is not XmlElement)
            {
                throw new InvalidDataException($"<{node.Name}> holds a {key.NodeType} node where a key is expected");
            }
            var time = 0f;
            var value = 0f;
            foreach (var attribute in LegacyXml.AttributesOf(key))
            {
                if (LegacyXml.Is(attribute, "time"))
                {
                    time = (float)LegacyXml.Int(attribute.Value) / keyFramesPerSecond;
                }
                else if (LegacyXml.Is(attribute, ValueAttribute))
                {
                    value = LegacyXml.Float(attribute.Value);
                }
            }
            keys[i] = new Keyframe(time, value);
        }
        return new ParameterCurve(keys);
    }

    private static ParameterCurve ReadConstant(XmlNode node)
    {
        var value = 0f;
        foreach (var text in LegacyXml.Attributes(node, ValueAttribute))
        {
            value = LegacyXml.Float(text);
        }
        return ParameterCurve.Constant(value);
    }

    /// <summary>Any other child; like the original, a text or comment node here is an error.</summary>
    private static void ReadFlag(XmlNode node, EmitterKind kind, int version, FlagState state)
    {
        foreach (var value in LegacyXml.Attributes(node, ValueAttribute))
        {
            var name = node.Name;
            if (Is(name, "BlendMode"))
            {
                state.Set(EmitterFlags.AlphaBlended, value.Equals("Alpha", StringComparison.OrdinalIgnoreCase));
            }
            else if (Is(name, "SpreadType"))
            {
                state.Set(EmitterFlags.ConeSpread, value.Equals("Cone", StringComparison.OrdinalIgnoreCase));
            }
            else if (Is(name, "RotationAligned"))
            {
                state.Set(EmitterFlags.RotationAligned, LegacyXml.Bool(value));
            }
            else if (Is(name, "ColorControlAlpha"))
            {
                state.Set(EmitterFlags.Hsv, !LegacyXml.Bool(value));
            }
            else if (Is(name, "HSV"))
            {
                state.Set(EmitterFlags.Hsv, LegacyXml.Bool(value));
            }
            else if (Is(name, "Colorize"))
            {
                state.Set(EmitterFlags.Colorize, LegacyXml.Bool(value));
            }
            else if (Is(name, "RelativeVelocity"))
            {
                if (kind == EmitterKind.Continuous || version >= 2)
                {
                    state.Set(EmitterFlags.RelativeVelocity, LegacyXml.Bool(value));
                }
            }
            else if (Is(name, "Particle"))
            {
                state.Particle = LegacyXml.Byte(value);
            }
            else if (Is(name, "NrOfParticles") && kind == EmitterKind.Pulse)
            {
                state.NrOfParticles = LegacyXml.Int(value);
            }
            else if (Is(name, "SpawnLights") && version >= 2)
            {
                state.Set(EmitterFlags.SpawnLights, LegacyXml.Bool(value));
            }
            else if (Is(name, "LightSizeRelative") && version >= 2)
            {
                state.Set(EmitterFlags.LightSizeRelative, LegacyXml.Bool(value));
            }
        }
    }

    private static bool Is(string name, string expected) => name.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static void Move(Dictionary<P, ParameterCurve> parameters, P from, P to)
    {
        if (parameters.TryGetValue(from, out var curve))
        {
            parameters[to] = curve;
        }
        else
        {
            parameters.Remove(to);
        }
    }

    /// <summary>Flags collected while one emitter element is read.</summary>
    private sealed class FlagState
    {
        public EmitterFlags Flags { get; private set; }

        public byte Particle { get; set; }

        public int NrOfParticles { get; set; }

        public void Set(EmitterFlags flag, bool on) => Flags = on ? Flags | flag : Flags & ~flag;
    }
}
