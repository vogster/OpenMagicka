using System.Text;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;

namespace OpenMagicka.Tests.Levels;

public class SceneReaderTests
{
    private const string Path = "Levels/Swamp/Swamp_S1.xml";

    internal static string Scene(string children, string attributes = "") =>
        $"<?xml version=\"1.0\"?>\n<!--c--><Scene {attributes}>\n<Model>swamp_s1</Model>\n{children}\n</Scene>";

    internal static SceneDefinition Read(string xml, string path = Path) =>
        SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), path);

    [Fact]
    public void NameAndModel_ComeFromTheFileAndItsDirectory()
    {
        var scene = Read(Scene("", "ForceNavMesh=\"true\" forcecamera=\"True\" other=\"ignored\""));

        Assert.Equal(("swamp_s1", "Levels/Swamp/swamp_s1", true, true), (scene.Name, scene.Model, scene.ForceNavMesh, scene.ForceCamera));
    }

    [Fact]
    public void Settings_HaveTheOriginalsDefaults()
    {
        var scene = Read(Scene(""));

        Assert.Equal(SceneSettings.Default, scene.Settings);
        Assert.Equal((-50f, 1f, 0.8f, 2.5f, 1.5f), (scene.Settings.KillPlanePosition, scene.Settings.Filter.Brightness,
            scene.Settings.Bloom.Threshold, scene.Settings.Bloom.Blur, scene.Settings.RimLight.Power));
        Assert.Null(scene.Settings.Fog);
        Assert.Null(scene.Settings.SkyMap);
        Assert.Null(scene.Ruleset);
    }

    [Fact]
    public void Settings_AreReadFromTheirElements()
    {
        var settings = Read(Scene("""
            <Indoor>true</Indoor><Magnify>1.25</Magnify><Reverb roomType="cave" mix="0.4"/>
            <Fog start="10" end="80" color="0.1,0.2,0.3"/><Filter brightness="1.1" contrast="0.9" saturation="0.5"/>
            <Bloom threshold="0.7" multiplyer="2" blur="3"/><RimLight glow="0.5" power="2" bias="0.1"/>
            <SkyMap color="0.5,0.6,0.7">sky/stars</SkyMap>
            <Clouds intensity="0.3" scale="2" movement="0.1,-0.2" texture="clouds" worldRelative="true"/>
            <KillPlanePosition>-20</KillPlanePosition>
            """)).Settings;

        Assert.Equal((true, 1.25f, RoomType.Cave, 0.4f), (settings.Indoor, settings.Magnify, settings.RoomType, settings.ReverbMix));
        Assert.Equal(new FogSettings(10, 80, new Vector4(0.1f, 0.2f, 0.3f, 1)), settings.Fog);
        Assert.Equal(new FilterSettings(1.1f, 0.9f, 0.5f), settings.Filter);
        Assert.Equal(new BloomSettings(0.7f, 2, 3), settings.Bloom);
        Assert.Equal(new RimLightSettings(0.5f, 2, 0.1f), settings.RimLight);
        Assert.Equal(new SkyMapSettings("Levels/Swamp/sky/stars", new Vector3(0.5f, 0.6f, 0.7f)), settings.SkyMap);
        Assert.Equal(new CloudSettings(0.3f, new Vector2(2), new Vector2(0.1f, -0.2f), "Levels/Swamp/clouds", true), settings.Clouds);
        Assert.Equal(-20f, settings.KillPlanePosition);
    }

    [Fact]
    public void SettingQuirks_MatchTheOriginal()
    {
        var settings = Read(Scene("""
            <Fog/><Bloom MULTIPLIER="4"/><SkyMap COLOR="0,0,0">sky</SkyMap><Reverb roomType="20"/>
            <Clouds scale="1,3"/>
            """)).Settings;

        Assert.Equal(new FogSettings(0, 0, Vector4.Zero), settings.Fog);      // present without attributes
        Assert.Equal(4f, settings.Bloom.Multiplier);
        Assert.Equal(Vector3.One, settings.SkyMap!.Color);                     // "color" is case-sensitive
        Assert.Equal(RoomType.SmallRoom, settings.RoomType);
        Assert.Equal(new Vector2(1, 3), settings.Clouds.Scale);
        Assert.Equal(new Vector4(1, 0, 0, 0.5f), Read(Scene("<Fog color=\"1,0,0,0.5\"/>")).Settings.Fog!.Color);
        Assert.Equal(new Vector4(1, 0, 0, 0.5f), Read(Scene("<Fog color=\"1,0,0,0.5,junk\"/>")).Settings.Fog!.Color);
        Assert.Equal(new Vector3(1, 2, 3), Read(Scene("<SkyMap color=\"1,2,3,junk\">s</SkyMap>")).Settings.SkyMap!.Color);
        Assert.Equal(5f, Read(Scene("<Bloom multiplier=\"4\" multiplyer=\"5\"/>")).Settings.Bloom.Multiplier);   // the last one wins
    }

    [Theory]
    [InlineData("<Sound/>")]                                          // unknown element
    [InlineData("loose text")]
    [InlineData("<Wave/>")]                                           // a wave needs a survival ruleset before it
    [InlineData("<Ruleset type=\"versus\"/><Wave/>")]
    [InlineData("<Fog color=\"1,0\"/>")]
    [InlineData("<SkyMap color=\"1\">s</SkyMap>")]
    [InlineData("<Clouds scale=\"1,2,3\"/>")]
    [InlineData("<Clouds movement=\"1\"/>")]
    [InlineData("<Indoor>yes</Indoor>")]
    [InlineData("<Reverb roomType=\"hall\"/>")]
    [InlineData("<Reverb roomType=\"300\"/>")]
    public void ElementsAndValuesTheOriginalRejects_AreInvalid(string children)
    {
        Assert.Throws<InvalidDataException>(() => Read(Scene(children)));
    }

    [Theory]
    [InlineData("<Level/>")]
    [InlineData("<Scene><Indoor>true</Indoor></Scene>")]               // no model
    public void FilesThatAreNotScenes_AreInvalid(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Read(xml));
    }

    [Fact]
    public void Rulesets_KeepTheirTypeAndContent_AndSurvivalWavesFollowThem()
    {
        var scene = Read(Scene("""
            <Ruleset type="Survival" waves="2"><spawnAreas><area>a1</area></spawnAreas></Ruleset>
            <Wave value="1"><waveActions area="a1" delay="2"><spawn type="goblin"/></waveActions></Wave>
            <Wave/>
            """));

        Assert.Equal(RulesetType.Survival, scene.Ruleset!.Type);
        Assert.Equal("2", scene.Ruleset.Node.Attribute("waves"));
        Assert.Equal("a1", scene.Ruleset.Node.Children[0].Children[0].Text);
        Assert.Equal(2, scene.Waves.Count);
        var block = Assert.Single(scene.Waves[0].Blocks);
        Assert.Equal(("a1", 2f), (block.Area, block.Delay));
        Assert.Equal("spawn", block.Actions.Regular[0].Node.Name);
        Assert.Empty(scene.Waves[1].Blocks);
    }

    [Fact]
    public void WaveBlocks_AreAnyChild_WithAnyAreaMeaningNone_AndOneRandomGroupAllowed()
    {
        var wave = Read(Scene("""
            <Ruleset type="survival"/>
            <Wave><!--c--><GiveOrder area="ANY" type="x"><a/></GiveOrder><waveActions><random><b/></random><c/></waveActions></Wave>
            """)).Waves[0];

        Assert.Equal([(null, 0f), (null, 0f)], wave.Blocks.Select(b => (b.Area, b.Delay)));
        Assert.Equal(["b", "c"], wave.Blocks[1].Actions.Variants(false).Single().Select(a => a.Name));
    }

    [Theory]
    [InlineData("<Wave>text</Wave>")]
    [InlineData("<Wave><waveActions><random><a/></random><random><b/></random></waveActions></Wave>")]   // two variants
    [InlineData("<Wave><waveActions delay=\"soon\"/></Wave>")]
    public void WavesTheOriginalRejects_AreInvalid(string wave)
    {
        Assert.Throws<InvalidDataException>(() => Read(Scene("<Ruleset type=\"survival\"/>" + wave)));
    }

    [Fact]
    public void ANewRuleset_DropsTheWavesOfThePreviousOne()
    {
        var scene = Read(Scene("<Ruleset type=\"survival\"/><Wave/><Ruleset type=\"survival\"/><Wave/><Wave/><Ruleset type=\"none\"/>"));

        Assert.Equal((RulesetType.Survival, 2), (scene.Ruleset!.Type, scene.Waves.Count));
        Assert.Empty(Read(Scene("<Ruleset type=\"survival\"/><Wave/><Ruleset type=\"versus\"/>")).Waves);
    }

    [Fact]
    public void RepeatedSettings_KeepUnsetAttributes_ButEachSkyMapResetsItsColour()
    {
        var settings = Read(Scene("""
            <Fog start="1" color="1,1,1"/><Fog end="9"/><SkyMap color="0,0,0">a</SkyMap><SkyMap>b</SkyMap>
            """)).Settings;

        Assert.Equal(new FogSettings(1, 9, Vector4.One), settings.Fog);
        Assert.Equal(new SkyMapSettings("Levels/Swamp/b", Vector3.One), settings.SkyMap);
    }

    [Theory]
    [InlineData("<Model/>")]
    [InlineData("<Model>  </Model>")]
    [InlineData("<SkyMap/>")]
    [InlineData("<Clouds texture=\"\"/>")]
    public void EmptyPaths_AreInvalid(string children)
    {
        Assert.Throws<InvalidDataException>(() => Read(Scene(children).Replace("<Model>swamp_s1</Model>", "")));
    }

    [Fact]
    public void TheSceneName_IsTheLastPathSegment_WhateverTheSeparator()
    {
        Assert.Equal("swamp_s1", Read(Scene(""), @"Levels\Swamp\Swamp_S1.xml").Name);
    }

    [Fact]
    public void TrailingSpacesAndDots_AreDroppedFromPaths_LikeWindowsDoes()
    {
        Assert.Equal("Levels/Swamp/sky", Read(Scene("<SkyMap>sky. </SkyMap>")).Settings.SkyMap!.Texture);
        Assert.Equal("Levels/sky", Read(Scene("<SkyMap>../sky</SkyMap>")).Settings.SkyMap!.Texture);
    }

    [Fact]
    public void DeepNesting_IsInvalidData_NotAStackOverflow()
    {
        var deep = string.Concat(Enumerable.Repeat("<a>", 100_000)) + string.Concat(Enumerable.Repeat("</a>", 100_000));

        Assert.Throws<InvalidDataException>(() => Read(Scene($"<Indoor>{deep}</Indoor>")));
    }

    [Fact]
    public void ARulesetOfAnUnknownType_IsNone()
    {
        Assert.Null(Read(Scene("<Ruleset type=\"tag\"/>")).Ruleset);
        Assert.Equal(RulesetType.TimedObjective, Read(Scene("<Ruleset TYPE=\"TimedObjective\"/>")).Ruleset!.Type);
    }
}
