using System.Text;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Tests.Levels;

public class DialogReaderTests
{
    internal static string Dialogs(string body) => $"<?xml version=\"1.0\"?>\n<!--c-->\n<Dialogs>\n{body}\n</Dialogs>";

    internal static IReadOnlyList<DialogDefinition> Read(string xml) =>
        DialogReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static DialogDefinition One(string dialog) => Assert.Single(Read(Dialogs(dialog)));

    [Fact]
    public void ADialog_HasInteractsOfMessagesOfLines()
    {
        var dialog = One("""
            <Dialog ID="Horse" iconText="examine" repeatFrom="1">
              <!--first-->
              <interact lock="true" focusOwner="false" count="2">
                <message character="Owner" type="say" facing="false" forceOnScreen="false" width="400">
                  <text sound="WaveBank/POI_Generic" anim="Talk_Casual0" TTL="2.5">#PoI_Horse1</text>
                  <line>#PoI_Horse2,#Name_A,#Name_B</line>
                </message>
              </interact>
              <Interact><message/></Interact>
            </Dialog>
            """);

        Assert.Equal(("horse", LegacyStringHash.Compute("horse"), InteractType.Examine, 1), (dialog.Id, dialog.Key, dialog.IconText, dialog.RepeatFrom));
        var interact = dialog.Interacts[0];
        Assert.Equal((true, false, 2, InteractType.Examine), (interact.Lock, interact.FocusOwner, interact.Count, interact.IconText));
        var message = Assert.Single(interact.Messages);
        Assert.Equal(("owner", MessageType.Say, false, false, 400), (message.Character, message.Type, message.Facing, message.ForceOnScreen, message.Width));
        var line = message.Lines[0];
        Assert.Equal(("#poi_horse1", SoundBanks.WaveBank, "poi_generic", Animation.talk_casual0, 2.5f), (line.Key, line.Bank, line.Sound, line.Animation, line.Ttl));
        Assert.Empty(line.Arguments);
        Assert.Equal("#poi_horse2", message.Lines[1].Key);
        Assert.Equal(["#name_a", "#name_b"], message.Lines[1].Arguments);
    }

    [Fact]
    public void Defaults_MatchTheOriginal()
    {
        var dialog = One("<Dialog><Interact><message><text>#a</text></message></Interact></Dialog>");
        var interact = dialog.Interacts[0];
        var message = interact.Messages[0];
        var line = message.Lines[0];

        Assert.Equal((null, 0, InteractType.Talk, 0), (dialog.Id, dialog.Key, dialog.IconText, dialog.RepeatFrom));
        Assert.Equal((false, true, 1), (interact.Lock, interact.FocusOwner, interact.Count));
        Assert.Equal((null, true, true, 600), (message.Character, message.Facing, message.ForceOnScreen, message.Width));
        Assert.Equal((SoundBanks.Characters, (string?)null, Animation.talk_casual0, -1f), (line.Bank, line.Sound, line.Animation, line.Ttl));
    }

    [Fact]
    public void SoundWithoutBank_UsesCharacters_AndBadFlagsAreLenient()
    {
        var message = One("""<Dialog><Interact><message facing="maybe" width="wide"><text sound="Grunt">#a</text></message></Interact></Dialog>""")
            .Interacts[0].Messages[0];

        Assert.Equal((SoundBanks.Characters, "grunt"), (message.Lines[0].Bank, message.Lines[0].Sound));
        Assert.False(message.Facing);                // bool.TryParse failing leaves false
        Assert.Equal(600, message.Width);            // int.TryParse failing keeps the default
    }

    [Theory]
    [InlineData("<Dialog other=\"1\"/>")]
    [InlineData("<Dialog><Interact count=\"x\"/></Dialog>")]
    [InlineData("<Dialog><Interact fast=\"true\"/></Dialog>")]
    [InlineData("<Dialog><Interact><message colour=\"red\"/></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message type=\"shout\"/></Interact></Dialog>")]
    [InlineData("<Dialog><Talk/></Dialog>")]
    [InlineData("<Dialog><Interact><text/></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message><!--c--><text>#a</text></message></Interact></Dialog>")]   // a comment in a message fails
    [InlineData("<Dialog><Interact><message>#a</message></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message><text sound=\"a/b/c\">#a</text></message></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message><text sound=\"Choir/x\">#a</text></message></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message><text anim=\"moonwalk\">#a</text></message></Interact></Dialog>")]
    [InlineData("<Dialog><Interact><message><text TTL=\"soon\">#a</text></message></Interact></Dialog>")]
    [InlineData("<Dialog id=\"a\"/><Dialog id=\"A\"/>")]
    [InlineData("<Dialog/><Dialog/>")]                                                      // both key 0
    [InlineData("<Dialog><Interact lock=\"no\"/></Dialog>")]
    [InlineData("<Dialog iconText=\"wave\"/>")]
    [InlineData("<Dialog><Interact><message><text anim=\"\">#a</text></message></Interact></Dialog>")]
    [InlineData("<?Dialog x?>")]
    [InlineData("<Speech/>")]
    [InlineData("text")]
    public void DialogsTheOriginalRejects_AreInvalid(string body)
    {
        Assert.Throws<InvalidDataException>(() => Read(Dialogs(body)));
    }

    [Fact]
    public void Lines_IgnoreOtherAttributes_AndKeepArgumentSpaces()
    {
        var message = One("""
            <Dialog id=""><Interact><message forceOnScreen="x"><text foo="1">#a, #B</text><text/></message></Interact></Dialog>
            """).Interacts[0].Messages[0];

        Assert.False(message.ForceOnScreen);
        Assert.Equal(("#a", new[] { " #b" }), (message.Lines[0].Key, message.Lines[0].Arguments.ToArray()), new LineComparer());
        Assert.Equal("", message.Lines[1].Key);
        Assert.Equal(LegacyStringHash.Compute(""), One("<Dialog id=\"\"/>").Key);
    }

    [Fact]
    public void AFileWithoutDialogs_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Read("<Level/>"));
    }

    [Fact]
    public void Collections_MergeTheLevelsDialogsWithTheFairys_AndRejectDuplicateIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "omdlg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Levels"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Levels", "Dialog_Fairy.xml"), Dialogs("<Dialog id=\"fairy_hint\"/>"));
            File.WriteAllText(Path.Combine(root, "Levels", "Dialog_Swamp.xml"), Dialogs("<Dialog id=\"frog\"/>"));
            File.WriteAllText(Path.Combine(root, "Levels", "Dialog_Bad.xml"), Dialogs("<Dialog id=\"Fairy_Hint\"/>"));
            var files = new ContentFileSystem(root);

            var level = DialogCollection.Load(files, "Levels/Dialog_Swamp.xml");
            Assert.Equal(2, level.Count);
            Assert.True(level.TryGet("FROG", out _));
            Assert.Equal("fairy_hint", level.Get(LegacyStringHash.Compute("fairy_hint")).Id);
            Assert.Equal(1, DialogCollection.Load(files, null).Count);
            var error = Assert.Throws<InvalidDataException>(() => DialogCollection.Load(files, "Levels/Dialog_Bad.xml"));
            Assert.Contains("Dialog_Fairy.xml", error.Message);
            Assert.Throws<FileNotFoundException>(() => DialogCollection.Load(files, "Levels/Dialog_Missing.xml"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class LineComparer : IEqualityComparer<(string, string[])>
    {
        public bool Equals((string, string[]) x, (string, string[]) y) => x.Item1 == y.Item1 && x.Item2.SequenceEqual(y.Item2);

        public int GetHashCode((string, string[]) obj) => obj.Item1.GetHashCode(StringComparison.Ordinal);
    }
}
