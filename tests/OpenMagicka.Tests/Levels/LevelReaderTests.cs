using System.Text;
using OpenMagicka.Content.Levels;

namespace OpenMagicka.Tests.Levels;

public class LevelReaderTests
{
    private const string Path = "Levels/Challenges/ch_test.lvl";

    internal static string Level(string children) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Level>
          <Loading>UI/LoadingScreens/test</Loading>
          <Name>#Chapter_04</Name>
          <Description>#Chapter_04_d</Description>
          {children}
          <Scenes><!--first--><Scene>ch_test.xml  </Scene><scene>../Shared/arena.xml</scene></Scenes>
        </Level>
        """;

    internal static LevelDefinition Read(string xml, string path = Path) =>
        LevelReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), path);

    [Fact]
    public void RequiredElements_AndScenePathsRelativeToTheLevel()
    {
        var level = Read(Level(""));

        Assert.Equal(("#Chapter_04", "#Chapter_04_d", "#Chapter_04_d", "UI/LoadingScreens/test"),
            (level.Name, level.Description, level.ShortDescription, level.Loading));
        Assert.Equal(["Levels/Challenges/ch_test.xml", "Levels/Shared/arena.xml"], level.Scenes);
        Assert.Null(level.Dialogs);
    }

    [Fact]
    public void Start_HasDefaults_AndReadsItsAttributesIgnoringCase()
    {
        var defaults = Read(Level("")).Start;
        var start = Read(Level("<Start scene=\"Ch_Test\" AREA=\"start\" spawnplayers=\"false\" noItems=\"true\" ForceCamera=\"false\" spawnFairy=\"false\" other=\"x\"/>")).Start;

        Assert.Equal(new LevelStart(null, null, true, false, null, true), defaults);
        Assert.Equal(new LevelStart("ch_test", "start", false, true, false, false), start);
    }

    [Fact]
    public void AreaPoints_AreTheAreaNameWithThePlayerIndex_NotLowered()
    {
        var start = Read(Level("<Start area=\"Start\"/>")).Start;

        Assert.Equal(["Start0", "Start1", "Start2", "Start3"], start.PlayerAreas);
        Assert.Empty(Read(Level("")).Start.PlayerAreas);
    }

    [Fact]
    public void OptionalElements_AreRead()
    {
        var level = Read(Level("""
            <ShortDescription>#short</ShortDescription>
            <Dialogs>../Dialog_Tutorial.xml</Dialogs>
            <PreferredAvatar>Cultist</PreferredAvatar>
            <AllowedAvatars><Avatar>Wizard</Avatar><!--c--><Avatar>Vlad</Avatar></AllowedAvatars>
            <AdditionalItems>
              <Item item="Items/Wizard/staff" bone="joint_hand"/><Item item="Items/x"/><Other item="y" bone="z"/>
            </AdditionalItems>
            <Unknown/>
            """));

        Assert.Equal("#short", level.ShortDescription);
        Assert.Equal("Levels/Dialog_Tutorial.xml", level.Dialogs);
        Assert.Equal("cultist", level.PreferredAvatar);
        Assert.Equal(["wizard", "vlad"], level.AllowedAvatars);
        Assert.Equal([new AvatarItem("Items/Wizard/staff", "joint_hand")], level.AdditionalItems);  // without a bone it is dropped
    }

    [Fact]
    public void RepeatedElements_AccumulateListsAndMergeStartAttributes()
    {
        var level = Read(Level("""
            <Start scene="a" area="x"/><Start scene="b"/>
            <AllowedAvatars><Avatar>one</Avatar></AllowedAvatars><AllowedAvatars><Avatar>two</Avatar></AllowedAvatars>
            <AdditionalItems><Item item="i1" bone="b"/></AdditionalItems><AdditionalItems><Item item="i2" bone="b"/></AdditionalItems>
            """));

        Assert.Equal(("b", "x"), (level.Start.Scene, level.Start.Area));
        Assert.Equal(["one", "two"], level.AllowedAvatars);
        Assert.Equal(["i1", "i2"], level.AdditionalItems.Select(i => i.Item));
    }

    [Fact]
    public void TheMenuNeedsNameDescriptionAndLoadingBeforeTheScenes()
    {
        var xml = "<Level><Name>a</Name><Description>b</Description><Scenes><Scene>s.xml</Scene></Scenes><Loading>c</Loading></Level>";

        Assert.Throws<InvalidDataException>(() => Read(xml));
    }

    [Theory]
    [InlineData("<PreferredAvatar></PreferredAvatar>")]
    [InlineData("<AllowedAvatars><Avatar/></AllowedAvatars>")]
    [InlineData("<Dialogs/>")]
    [InlineData("<Scenes><Scene> </Scene></Scenes>")]
    public void EmptyValues_AreInvalid(string children)
    {
        Assert.Throws<InvalidDataException>(() => Read(Level(children)));
    }

    [Fact]
    public void PreferredAvatar_DefaultsToTheWizard()
    {
        Assert.Equal("wizard", Read(Level("")).PreferredAvatar);
        Assert.Empty(Read(Level("")).AllowedAvatars);
    }

    [Theory]
    [InlineData("<Book/>")]                                                         // no Level element
    [InlineData("<Level><Name>a</Name><Description>b</Description><Loading>c</Loading></Level>")]   // no scenes
    [InlineData("<Level><Description>b</Description><Loading>c</Loading><Scenes><Scene>s.xml</Scene></Scenes></Level>")]
    [InlineData("<Level><Name>a</Name><Description></Description><Loading>c</Loading><Scenes><Scene>s.xml</Scene></Scenes></Level>")]
    [InlineData("<Level><Name>a</Name><Description>b</Description><Scenes><Scene>s.xml</Scene></Scenes></Level>")]
    public void LevelsWithoutTheirRequiredParts_AreInvalid(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Read(xml));
    }

    [Theory]
    [InlineData("<Scenes><Map>s.xml</Map></Scenes>")]
    [InlineData("<Scenes>s.xml</Scenes>")]
    [InlineData("<Scenes><Scene>../../../../outside.xml</Scene></Scenes>")]
    [InlineData("<Start spawnPlayers=\"yes\"/>")]
    public void BadScenesAndValues_AreInvalid(string children)
    {
        Assert.Throws<InvalidDataException>(() => Read(Level(children)));
    }
}
