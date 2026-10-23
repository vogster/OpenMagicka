using System.Text;
using OpenMagicka.Content.Particles;
using P = OpenMagicka.Content.Particles.EmitterParameter;

namespace OpenMagicka.Tests.Particles;

public class VisualEffectReaderTests
{
    /// <summary>The 11 angle parameters a continuous emitter must have, all 0 unless overridden.</summary>
    private static readonly string[] Angles =
    [
        "RotationY", "HueMin", "HueMax", "RotationMin", "RotationMax", "RotationSpeedMin", "RotationSpeedMax",
        "SpreadConeAngle", "SpreadArcHorizontalAngle", "SpreadArcVerticalMin", "SpreadArcVerticalMax",
    ];

    internal static string Continuous(string body) =>
        "<ContinuousEmitter name=\"c\">" + string.Concat(Angles.Where(a => !body.Contains($"<{a} ") && !body.Contains($"<{a}>"))
            .Select(a => $"<{a} value=\"0\"/>")) + body + "</ContinuousEmitter>";

    internal static string Effect(string children, string attributes = "type=\"Looping\" duration=\"2\" keyFramesPerSecond=\"10\" version=\"2\"") =>
        $"<!--Copyright-->\n<Effect {attributes}>\n{children}\n</Effect>";

    internal static VisualEffectContent Read(string xml) =>
        VisualEffectReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static EmitterContent Single(string emitter, string version = "2") =>
        Assert.Single(Read(Effect(emitter, $"keyFramesPerSecond=\"10\" version=\"{version}\"")).Emitters);

    [Fact]
    public void RootAttributes_AreReadIgnoringCase_WithDefaults()
    {
        var effect = Read(Effect("", "TYPE=\"infinite\" Duration=\"1.5\""));
        var empty = Read("<!--only a comment--><Other/>");

        Assert.Equal((VisualEffectType.Infinite, 1.5f), (effect.Type, effect.Duration));
        Assert.Equal((VisualEffectType.Single, 0f, 0, null), (empty.Type, empty.Duration, empty.Emitters.Count, empty.Light));
    }

    [Fact]
    public void EmitterNames_AreCaseSensitive_LightIsNot_AndOtherNodesAreIgnored()
    {
        var effect = Read(Effect(
            Continuous("") + "<continuousemitter/><PulseEmitter/><!--c--><Sound/><LIGHT><Radius Value=\"3\"/></LIGHT>"));

        Assert.Equal([EmitterKind.Continuous, EmitterKind.Pulse], effect.Emitters.Select(e => e.Kind));
        Assert.Equal(3f, effect.Light!.Radius);
    }

    [Fact]
    public void ContinuousParameters_AreConstantsOrKeysInFrames_AndAbsentOnesEvaluateToOne()
    {
        var emitter = Single(Continuous("""
            <Gravity value="-9.8"/>
            <PositionX><Key time="0" value="1"/><Key time="20" value="3"/><Anything time="5"/></PositionX>
            <Drag/>
            <positionz value="7"/>
            """));

        Assert.Equal(-9.8f, emitter.Evaluate(P.Gravity, 5));
        Assert.Equal([new(0f, 1f), new(2f, 3f), new(0.5f, 0f)], emitter.Parameters[P.PositionX].Keys);
        Assert.Equal(0f, emitter.Evaluate(P.Drag, 0));
        Assert.False(emitter.Parameters.ContainsKey(P.PositionZ));          // names are case-sensitive
        Assert.Equal(1f, emitter.Evaluate(P.PositionZ, 0));
        Assert.Equal(1f, emitter.Evaluate(P.ParticlesPerSecond, 0));
    }

    [Fact]
    public void KeyTimes_UseTheEffectsKeyFramesPerSecond()
    {
        var effect = Read(Effect(Continuous("<Gravity><Key time=\"3\" value=\"1\"/></Gravity>"), "keyFramesPerSecond=\"4\""));

        Assert.Equal(0.75f, Assert.Single(effect.Emitters[0].Parameters[P.Gravity].Keys).Time);
    }

    [Fact]
    public void AngleParameters_AreConvertedFromDegrees()
    {
        var emitter = Single(Continuous("<HueMax value=\"180\"/><RotationY><Key time=\"0\" value=\"90\"/><Key time=\"10\" value=\"-90\"/></RotationY>"));

        Assert.Equal(MathF.PI, emitter.Evaluate(P.HueMax, 0), 5);
        Assert.Equal([MathF.PI / 2, -MathF.PI / 2], emitter.Parameters[P.RotationY].Keys.Select(k => k.Value), new ToleranceComparer(1e-6f));
        Assert.Equal(0f, emitter.Evaluate(P.SpreadConeAngle, 0));
    }

    [Fact]
    public void ContinuousEmitterWithoutAnAngleParameter_IsInvalid_LikeInTheOriginal()
    {
        var xml = Effect(Continuous("").Replace("<HueMin value=\"0\"/>", ""));

        Assert.Throws<InvalidDataException>(() => Read(xml));
    }

    [Theory]
    [InlineData("<Gravity>5</Gravity>")]
    [InlineData("<Gravity><!--k--></Gravity>")]
    public void KeyListsWithTextOrComments_AreInvalid_LikeInTheOriginal(string parameter)
    {
        Assert.Throws<InvalidDataException>(() => Read(Effect(Continuous(parameter))));
    }

    [Fact]
    public void Flags_AreReadIgnoringCase()
    {
        var emitter = Single(Continuous("""
            <blendmode VALUE="alpha"/><SpreadType value="CONE"/><RotationAligned value="True"/>
            <Colorize value="true"/><RelativeVelocity value=" TRUE "/><Particle value="12"/>
            <SpawnLights value="True"/><LightSizeRelative value="True"/><NrOfParticles value="9"/>
            """));

        Assert.Equal(EmitterFlags.AlphaBlended | EmitterFlags.ConeSpread | EmitterFlags.RotationAligned | EmitterFlags.Colorize
            | EmitterFlags.RelativeVelocity | EmitterFlags.SpawnLights | EmitterFlags.LightSizeRelative, emitter.Flags);
        Assert.Equal((12, 0), ((int)emitter.Particle, emitter.NrOfParticles));    // NrOfParticles is a pulse flag
    }

    [Fact]
    public void OtherFlagValues_MeanAdditiveAndArc_AndColorControlAlphaIsTheOppositeOfHsv()
    {
        Assert.Equal(EmitterFlags.Hsv, Single(Continuous("<BlendMode value=\"Additive\"/><SpreadType value=\"Arc\"/><ColorControlAlpha value=\"False\"/>")).Flags);
        Assert.Equal(EmitterFlags.None, Single(Continuous("<HSV value=\"True\"/><ColorControlAlpha value=\"True\"/>")).Flags);
    }

    [Fact]
    public void Version1_HasNoLightFlags_AndTakesTheColourAlphaFromAlpha()
    {
        var emitter = Single(Continuous("<SpawnLights value=\"True\"/><AlphaMin value=\"0.25\"/><ColorAMin value=\"0.9\"/><ColorR value=\"0.5\"/>"), version: "1");

        Assert.Equal(EmitterFlags.None, emitter.Flags);
        Assert.Equal(0.25f, emitter.Evaluate(P.ColorAMin, 0));
        Assert.False(emitter.Parameters.ContainsKey(P.ColorAMax));
        Assert.All([P.ColorR, P.ColorG, P.ColorB, P.ValueMin, P.ValueMax, P.ValueDistribution, P.AlphaMin, P.AlphaMax, P.AlphaDistribution],
            p => Assert.Equal([new Keyframe(0, 1)], emitter.Parameters[p].Keys));
    }

    [Fact]
    public void PulseParameters_ArePlainFloats_AbsentOnesAreZero()
    {
        var emitter = Single("""
            <PulseEmitter name="p">
              <NrOfParticles value="40"/><TriggerTime value="0.5"/><Gravity><Key time="10" value="3"/></Gravity>
              <SizeStartMin Value="2"/><ParticlesPerSecond value="99"/><RelativeVelocity value="True"/><HueMin value="90"/>
            </PulseEmitter>
            """);

        Assert.Equal((EmitterKind.Pulse, 40, EmitterFlags.RelativeVelocity), (emitter.Kind, emitter.NrOfParticles, emitter.Flags));
        Assert.Equal(0.5f, emitter.Evaluate(P.TriggerTime, 0));
        Assert.Equal(0f, emitter.Evaluate(P.Gravity, 1));                   // a pulse parameter's children are ignored
        Assert.Equal(2f, emitter.Evaluate(P.SizeStartMin, 0));
        Assert.Equal(0f, emitter.Evaluate(P.ParticlesPerSecond, 0));         // not a pulse parameter
        Assert.Equal(0f, emitter.Evaluate(P.Drag, 0));
        Assert.Equal(MathF.PI / 2, emitter.Evaluate(P.HueMin, 0), 5);
    }

    [Fact]
    public void PulseVersion1_IgnoresRelativeVelocity_AndShufflesTheColours()
    {
        var emitter = Single("<PulseEmitter><RelativeVelocity value=\"True\"/><AlphaMax value=\"0.5\"/></PulseEmitter>", version: "1");

        Assert.Equal(EmitterFlags.None, emitter.Flags);
        Assert.Equal((0f, 0.5f, 1f, 1f), (emitter.Evaluate(P.ColorAMin, 0), emitter.Evaluate(P.ColorAMax, 0),
            emitter.Evaluate(P.ColorR, 0), emitter.Evaluate(P.AlphaMax, 0)));
    }

    [Fact]
    public void Light_ReadsEveryProperty()
    {
        var light = Read(Effect("""
            <Light>
              <!--offset--><PositionX Value="1"/><PositionY Value="2"/><PositionZ value="3"/>
              <DiffuseR Value="0.1"/><DiffuseG Value="0.2"/><DiffuseB Value="0.3"/>
              <AmbientR Value="0.4"/><AmbientG Value="0.5"/><AmbientB Value="0.6"/>
              <Specular Value="0.7"/><Radius Value="8"/><VariationType Value="candle"/><VariationSpeed Value="9"/>
              <VariationAmount Value="10"/><SpawnTransitionType Value="Linear"/><SpawnTransitionTime Value="11"/>
              <DespawnTransitionType Value="FLICKER"/><DespawnTransitionTime Value="12"/>
            </Light>
            """)).Light!;

        Assert.Equal(new EffectLightContent(new(1, 2, 3), new(0.1f, 0.2f, 0.3f), new(0.4f, 0.5f, 0.6f), 0.7f, 8,
            LightVariationType.Candle, 9, 10, LightTransitionType.Linear, 11, LightTransitionType.Flicker, 12), light);
    }

    [Theory]
    [InlineData("type=\"Sometimes\"")]
    [InlineData("duration=\"long\"")]
    [InlineData("keyFramesPerSecond=\"1.5\"")]
    public void UnparsableRootAttributes_AreInvalid(string attributes)
    {
        Assert.Throws<InvalidDataException>(() => Read(Effect("", attributes)));
    }

    [Theory]
    [InlineData("<RotationAligned value=\"yes\"/>")]
    [InlineData("<Particle value=\"300\"/>")]
    [InlineData("<Gravity value=\"heavy\"/>")]
    [InlineData("<Gravity><Key time=\"0.5\" value=\"1\"/></Gravity>")]
    public void UnparsableEmitterValues_AreInvalid(string parameter)
    {
        Assert.Throws<InvalidDataException>(() => Read(Effect(Continuous(parameter))));
    }

    [Fact]
    public void ControlCharacterReferences_AreAccepted_ButDtdsAreNot()
    {
        Assert.Single(Read(Effect(Continuous("").Replace("name=\"c\"", "name=\"1. emitter&#x1F; - center\""))).Emitters);
        Assert.Throws<InvalidDataException>(() => Read("<!DOCTYPE Effect [<!ENTITY e \"x\">]><Effect/>"));
        Assert.Throws<InvalidDataException>(() => Read("<Effect>"));
    }

    [Theory]
    [InlineData("<?Effect a?><Effect/>")]                   // a processing instruction has no attributes: the original fails
    [InlineData("<?xml version=\"2.0\"?><Effect/>")]
    [InlineData("<?xml version=\"1.0NaN\"?><Effect/>")]
    [InlineData("<Effect duration=\"1e40\"/>")]             // .NET Framework's float.Parse overflows
    public void MalformedDocuments_OnlyEverThrowInvalidData(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Read(xml));
    }

    [Fact]
    public void InfinityAndNaNSymbols_AreAccepted_LikeTheOriginal()
    {
        Assert.Equal(float.PositiveInfinity, Read("<Effect duration=\"Infinity\"/>").Duration);
        Assert.Equal(float.NegativeInfinity, Read("<Effect duration=\"-Infinity\"/>").Duration);
        Assert.True(float.IsNaN(Read("<Effect duration=\"NaN\"/>").Duration));
    }

    [Theory]
    [InlineData("loose text")]
    [InlineData("<!--note-->")]
    public void TextOrCommentsDirectlyInAnEmitter_AreInvalid_LikeInTheOriginal(string node)
    {
        Assert.Throws<InvalidDataException>(() => Read(Effect(Continuous(node))));
        Assert.Throws<InvalidDataException>(() => Read(Effect("<PulseEmitter>" + node + "</PulseEmitter>")));
    }

    [Theory]
    [InlineData("<Light>text</Light>")]
    [InlineData("<Light><Radius Value=\"wide\"/></Light>")]
    [InlineData("<Light><VariationType Value=\"Pulse\"/></Light>")]
    public void BadLights_AreInvalid(string light)
    {
        Assert.Throws<InvalidDataException>(() => Read(Effect(light)));
    }

    [Fact]
    public void LaterLights_AndRepeatedParametersAndFlags_OverrideOnlyWhatTheySet()
    {
        var effect = Read(Effect(
            "<Light><Radius Value=\"2\"/><Specular Value=\"0.5\"/></Light><Light><Radius Value=\"4\"/></Light>"
            + Continuous("<HueMin value=\"90\"/><HueMin value=\"180\"/><Particle value=\"1\"/><Particle value=\"2\"/><TriggerTime value=\"3\"/>")));
        var emitter = Assert.Single(effect.Emitters);

        Assert.Equal((4f, 0.5f), (effect.Light!.Radius, effect.Light.Specular));
        Assert.Equal(MathF.PI, emitter.Evaluate(P.HueMin, 0), 5);           // converted once, after the last one
        Assert.Equal(2, emitter.Particle);
        Assert.False(emitter.Parameters.ContainsKey(P.TriggerTime));        // a pulse parameter, ignored here
    }

    [Fact]
    public void NumericEnumValues_AreKept_LikeEnumParse()
    {
        Assert.Equal((VisualEffectType)7, Read("<Effect type=\"7\"/>").Type);
        Assert.Equal(VisualEffectType.Looping, Read("<Effect type=\"Single, Looping\"/>").Type);
    }

    [Theory]
    [InlineData("0", float.PositiveInfinity, float.NaN)]     // the original divides as floats: 3/0 and 0/0
    [InlineData("-2", -1.5f, -0f)]
    public void KeyFramesPerSecond_IsAFloatDivisor_EvenWhenZeroOrNegative(string fps, float first, float second)
    {
        var effect = Read(Effect(Continuous("<Gravity><Key time=\"3\" value=\"1\"/><Key time=\"0\" value=\"2\"/></Gravity>"),
            $"keyFramesPerSecond=\"{fps}\""));

        Assert.Equal([first, second], effect.Emitters[0].Parameters[P.Gravity].Keys.Select(k => k.Time));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    public void VersionsBelowTwoReadAsVersion1_AndAboveAsVersion2(string version)
    {
        var emitter = Single(Continuous("<SpawnLights value=\"True\"/>"), version);

        Assert.Equal(version == "3" ? EmitterFlags.SpawnLights : EmitterFlags.None, emitter.Flags);
    }

    [Fact]
    public void Version1_ColourAlphaWithoutAlpha_EndsUpAbsent()
    {
        var emitter = Single(Continuous("<ColorAMin value=\"0.3\"/>"), version: "1");

        Assert.False(emitter.Parameters.ContainsKey(P.ColorAMin));
        Assert.Equal(1f, emitter.Evaluate(P.ColorAMin, 0));
    }

    [Fact]
    public void LoadedContent_CannotBeChangedThroughItsCollections()
    {
        var effect = Read(Effect(Continuous("<Gravity><Key time=\"0\" value=\"1\"/></Gravity>")));
        var keys = effect.Emitters[0].Parameters[P.Gravity].Keys;

        Assert.IsNotType<List<EmitterContent>>(effect.Emitters);
        Assert.False(effect.Emitters is EmitterContent[]);
        Assert.False(keys is Keyframe[]);
        Assert.True(effect.Emitters is System.Collections.IList { IsReadOnly: true });
        Assert.True(keys is System.Collections.IList { IsReadOnly: true });
    }

    [Fact]
    public void Records_CopyTheCollectionsTheyAreGiven()
    {
        var emitters = new List<EmitterContent>();
        var parameters = new Dictionary<P, ParameterCurve> { [P.Gravity] = ParameterCurve.Constant(1) };
        var effect = new VisualEffectContent(VisualEffectType.Single, 0, emitters, null);
        var emitter = new EmitterContent(EmitterKind.Pulse, EmitterFlags.None, 0, 0, parameters);
        emitters.Add(emitter);
        parameters.Clear();

        Assert.Empty(effect.Emitters);
        Assert.Equal(1f, emitter.Evaluate(P.Gravity, 0));
    }

    private sealed class ToleranceComparer(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) <= tolerance;

        public int GetHashCode(float obj) => 0;
    }
}
