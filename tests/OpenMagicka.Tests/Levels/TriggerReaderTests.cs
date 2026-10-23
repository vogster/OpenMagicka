using OpenMagicka.Content.Levels;
using static OpenMagicka.Tests.Levels.SceneReaderTests;

namespace OpenMagicka.Tests.Levels;

public class TriggerReaderTests
{
    private static TriggerDefinition Trigger(string xml) => Assert.Single(Read(Scene(xml)).Triggers);

    [Fact]
    public void Attributes_HaveDefaults_AndTheIdIsLowered()
    {
        var plain = Trigger("<Trigger><then/></Trigger>");
        var full = Trigger("<trigger ID=\"Door_Open\" autorun=\"FALSE\" repeat=\"2.5\"><Then/></trigger>");

        Assert.Equal((null, 0, true, -1f), (plain.Id, plain.Key, plain.AutoRun, plain.Repeat));
        Assert.Equal(("door_open", false, 2.5f), (full.Id, full.AutoRun, full.Repeat));
        Assert.Equal(OpenMagicka.Content.LegacyStringHash.Compute("door_open"), full.Key);
    }

    [Theory]
    [InlineData("true", 0f)]
    [InlineData("False", -1f)]
    [InlineData("12", 12f)]
    [InlineData("1,000", 1000f)]      // NumberStyles.Number: thousands allowed...
    [InlineData("1e2", -1f)]          // ...but no exponent: not a number, not a bool, so "once"
    [InlineData("often", -1f)]
    [InlineData(" -3 ", -3f)]
    [InlineData("99999999999999999999999999999999999999999999999999", -1f)]   // overflows: not a number in .NET Framework
    [InlineData("Infinity", float.PositiveInfinity)]
    public void Repeat_IsANumberOrABool(string value, float expected)
    {
        Assert.Equal(expected, Trigger($"<Trigger id=\"t\" repeat=\"{value}\"><then/></Trigger>").Repeat);
    }

    [Theory]
    [InlineData("<Trigger id=\"t\" delay=\"1\"><then/></Trigger>")]   // any other attribute
    [InlineData("<Trigger id=\"t\" autorun=\"no\"><then/></Trigger>")]
    [InlineData("<Trigger id=\"t\"><if/></Trigger>")]                  // no <then>
    [InlineData("<Trigger id=\"t\"><then/></Trigger><Trigger id=\"T\"><then/></Trigger>")] // duplicate key
    [InlineData("<Trigger id=\"door\"><then/></Trigger><OnInteract locator=\"door\" type=\"use\"/>")]
    [InlineData("<Trigger id=\"t\"><then>text</then></Trigger>")]
    [InlineData("<Trigger id=\"t\"><then><random><!--c--><spawn/></random></then></Trigger>")]
    [InlineData("<Trigger id=\"t\"><then><looped><!--c--></looped></then></Trigger>")]
    [InlineData("<Trigger id=\"t\"><if>text</if><then/></Trigger>")]
    public void TriggersTheOriginalRejects_AreInvalid(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Read(Scene(xml)));
    }

    [Fact]
    public void Conditions_AreBlocksOfNodes_SkippingComments()
    {
        var trigger = Trigger("""
            <Trigger id="t"><If><!--c--><counter name="a" value="1"/><Present/></If><if/><OTHER/><then/></Trigger>
            """);

        Assert.Equal(2, trigger.Conditions.Count);
        Assert.Equal(["counter", "Present"], trigger.Conditions[0].Select(c => c.Name));
        Assert.Equal("1", trigger.Conditions[0][0].Attribute("VALUE"));
        Assert.Empty(trigger.Conditions[1]);
    }

    [Fact]
    public void Actions_AreRegularRandomAndLooped_AndTheLastThenWins()
    {
        var trigger = Trigger("""
            <Trigger id="t">
              <then><ignored/></then>
              <then>
                <!--c--><spawn type="a"/>
                <Random><x/><y/></Random>
                <looped><z/><random><w/></random></looped>
                <playMusic/>
              </then>
            </Trigger>
            """);

        Assert.Equal([("spawn", false), ("z", true), ("playMusic", false)], trigger.Actions.Regular.Select(a => (a.Node.Name, a.Looped)));
        Assert.Equal([(new[] { "x", "y" }, false), (["w"], true)],
            trigger.Actions.Random.Select(g => (g.Actions.Select(a => a.Name).ToArray(), g.Looped)));
    }

    [Fact]
    public void Variants_PutTheChosenRandomGroupBeforeTheRegularActions()
    {
        var actions = Trigger("""
            <Trigger id="t"><then><a/><random><r1/></random><b/><random><r2/><r3/></random><looped><l/><random><lr/></random></looped></then></Trigger>
            """).Actions;

        Assert.Equal([["r1", "a", "b"], ["r2", "r3", "a", "b"]], actions.Variants(looped: false).Select(v => v.Select(a => a.Name)));
        Assert.Equal([["r1", "a", "b", "l"], ["r2", "r3", "a", "b", "l"], ["lr", "a", "b", "l"]],
            actions.Variants(looped: true).Select(v => v.Select(a => a.Name)));
        Assert.Equal([["a"]], Trigger("<Trigger id=\"u\"><then><a/></then></Trigger>").Actions.Variants(false).Select(v => v.Select(a => a.Name)));
    }

    [Fact]
    public void OnInteract_IsStrictAboutAttributes_AndHoldsActions()
    {
        var interact = Assert.Single(Read(Scene("""
            <OnInteract locator="Door_1" type="pick_up" enabled="false" animHighlight="Door/Frame, Lock" physHighlight="Box"><open/></OnInteract>
            """)).Interactables);

        Assert.Equal(("door_1", InteractType.Pick_Up, false), (interact.Locator, interact.Type, interact.Enabled));
        Assert.Equal([["door", "frame"], ["lock"]], interact.AnimHighlight);
        Assert.Equal(["box"], interact.PhysHighlight);
        Assert.Equal(["open"], interact.Actions.Regular.Select(a => a.Node.Name));
        Assert.Throws<InvalidDataException>(() => Read(Scene("<OnInteract locator=\"d\" type=\"use\" repeatitions=\"2\"/>")));
        Assert.Throws<InvalidDataException>(() => Read(Scene("<OnInteract locator=\"d\" type=\"wave\"/>")));
    }

    [Fact]
    public void OnInteract_SkipsDirectComments_AndRejectsText()
    {
        var interact = Assert.Single(Read(Scene("<OnInteract type=\"use\"><!--c--><random><a/></random><b/></OnInteract>")).Interactables);

        Assert.Equal((null, 0), (interact.Locator, interact.Key));
        Assert.Equal([["a", "b"]], interact.Actions.Variants(false).Select(v => v.Select(a => a.Name)));
        Assert.Throws<InvalidDataException>(() => Read(Scene("<OnInteract locator=\"d\">text</OnInteract>")));
    }

    [Fact]
    public void ScriptNodeText_IsTheInnerTextWithoutComments()
    {
        var node = Trigger("<Trigger id=\"t\"><then><say>Hello <!--x--><b>big</b> world</say></then></Trigger>").Actions.Regular[0].Node;

        Assert.Equal("Hello big world", node.Text);
    }

    [Fact]
    public void ScriptNodes_KeepAttributesInOrderAndTheirText()
    {
        var spawn = Trigger("""<Trigger id="t"><then><spawn type="Goblin" Area="a"><move position="1,2,3"/>hint</spawn></then></Trigger>""")
            .Actions.Regular[0].Node;

        Assert.Equal([("type", "Goblin"), ("Area", "a")], spawn.Attributes.Select(a => (a.Name, a.Value)));
        Assert.Equal("Goblin", spawn.Attribute("TYPE"));
        Assert.Null(spawn.Attribute("delay"));
        Assert.Equal(["move", "#text"], spawn.Children.Select(c => c.Name));
        Assert.Equal("hint", spawn.Children[1].Text);
        Assert.Equal("hint", spawn.Text);
    }
}
