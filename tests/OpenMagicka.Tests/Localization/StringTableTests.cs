using OpenMagicka.Content;
using OpenMagicka.Content.Localization;

namespace OpenMagicka.Tests.Localization;

public class StringTableTests
{
    private static StringTable Table(params (string Key, string Text)[] entries) =>
        StringTable.Create(entries.Select(e => new LocEntry(e.Key, e.Text)));

    [Fact]
    public void Ids_AreTheLegacyHashOfTheLowerCaseKeyWithItsHash()
    {
        Assert.Equal(LegacyStringHash.Compute("#item_staff"), StringTable.Id("#Item_Staff"));
    }

    [Fact]
    public void LaterEntries_OverrideEarlierOnes_IgnoringKeyCase()
    {
        var table = Table(("#Name", "old"), ("#other", "x"), ("#NAME", "new"));

        Assert.Equal(2, table.Count);
        Assert.Equal("new", table.Get("#name"));
        Assert.Equal("new", table.Get(StringTable.Id("#Name")));
    }

    [Fact]
    public void Get_TurnsEscapedNewlinesIntoLineBreaks_ButDoesNotExpandReferences()
    {
        var table = Table(("#a", @"one\ntwo #b;"), ("#b", "B"));

        Assert.Equal("one\ntwo #b;", table.Get("#a"));
        Assert.True(table.TryGetRaw(StringTable.Id("#a"), out var raw));
        Assert.Equal(@"one\ntwo #b;", raw);
    }

    [Fact]
    public void MissingStrings_AreReportedWithTheirId()
    {
        var id = StringTable.Id("#missing");

        Assert.Equal($"NOT FOUND: {id}", StringTable.Empty.Get(id));
        Assert.Equal($"NOT FOUND: {id}", StringTable.Empty.GetWithReferences(id));
        Assert.False(StringTable.Empty.TryGetRaw(id, out _));
    }

    [Fact]
    public void GetWithReferences_ExpandsNestedReferences()
    {
        var table = Table(("#desc", "#PA;None #AA;Heals."), ("#PA", "Passive: "), ("#AA", "Active (#key;): "), ("#key", "K"));

        Assert.Equal("Passive: None Active (K): Heals.", table.GetWithReferences(StringTable.Id("#desc")));
        Assert.Equal(@"keeps \n", Table(("#n", @"keeps \n")).GetWithReferences(StringTable.Id("#n")));
    }

    [Fact]
    public void GetWithReferences_WithAColour_WrapsEveryExpandedReference()
    {
        var table = Table(("#a", "x #b; y"), ("#b", "B"));
        var colour = StringTable.ColorCode(1f, 0.5f, 1f / 3);

        Assert.Equal("[c=1,0.5,0.3333333]", colour);
        Assert.Equal("x [c=1,0.5,0.3333333]B[/c] y", table.GetWithReferences(StringTable.Id("#a"), colour));
    }

    [Fact]
    public void ReferencesWithoutASemicolon_AndUnknownReferences_AreHandledLikeTheOriginal()
    {
        var table = Table(("#open", "50# off"), ("#unknown", "a #nope; b"));

        Assert.Equal("50# off", table.GetWithReferences(StringTable.Id("#open")));
        Assert.Equal($"a NOT FOUND: {StringTable.Id("#nope")} b", table.GetWithReferences(StringTable.Id("#unknown")));
    }

    [Fact]
    public void CyclicReferences_StopAtTheDepthLimitInsteadOfOverflowingTheStack()
    {
        var table = Table(("#a", "<#b;>"), ("#b", "[#a;]"));

        var text = table.GetWithReferences(StringTable.Id("#a"));

        Assert.StartsWith("<[<[", text);
        Assert.DoesNotContain("#", text);
    }

    [Fact]
    public void ParseReferences_ReplacesReferencesInFreeTextWithWhiteText()
    {
        var table = Table(("#fire", @"Fire\n"));

        Assert.Equal("Cast [c=1,1,1]Fire\n[/c]!", table.ParseReferences("Cast #FIRE;!"));
    }

    [Fact]
    public void ParseReferences_OfASelfReference_Terminates()
    {
        var table = Table(("#loop", "#loop;"));

        var text = table.ParseReferences("#loop;");

        Assert.Contains("[c=1,1,1]", text);
    }

    [Fact]
    public void TheFirstHashWins_EvenWhenAStrayHashComesBeforeARealReference()
    {
        var table = Table(("#t", "50# off #a;"), ("#a", "A"));

        Assert.Equal($"50NOT FOUND: {StringTable.Id("# off #a")}", table.GetWithReferences(StringTable.Id("#t")));
    }

    [Fact]
    public void InsertedText_IsScannedAgainTogetherWithTheRest()
    {
        var table = Table(("#t", "#a;;"), ("#a", "#b"), ("#b", "B"));

        Assert.Equal("B", table.GetWithReferences(StringTable.Id("#t")));
        // In free text the inserted "#b[/c]" plus the remaining ';' form the next key.
        Assert.Equal($"[c=1,1,1][c=1,1,1]NOT FOUND: {StringTable.Id("#b[/c]")}[/c]", table.ParseReferences("#a;;"));
    }

    [Fact]
    public void HugeExpansions_AreCutAtTheLengthLimit_Quickly()
    {
        var table = Table(("#x", string.Concat(Enumerable.Repeat("#y;", 4096))), ("#y", new string('y', 100_000)),
            ("#loop", "#loop;#loop;"));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var expanded = table.GetWithReferences(StringTable.Id("#x"));
        var parsed = table.ParseReferences("#loop;");
        var nested = table.GetWithReferences(StringTable.Id("#loop"), StringTable.ColorCode(1, 1, 1));

        Assert.All([expanded, parsed, nested], text => Assert.True(text.Length <= StringTable.MaxExpandedLength + 100_000));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void ColorCode_NeedsThreeOrFourComponents()
    {
        Assert.Equal("[c=0,1,0,0.25]", StringTable.ColorCode(0, 1, 0, 0.25f));
        Assert.Throws<ArgumentException>(() => StringTable.ColorCode());
        Assert.Throws<ArgumentException>(() => StringTable.ColorCode(1, 1));
    }

    [Fact]
    public void ThePlayersKey_ReturnsThePlayerList()
    {
        var table = Table(("#players", "from file")).WithPlayers("A, B & C");

        Assert.Equal("A, B & C", table.Get("#players"));
        Assert.Equal("Hi A, B & C", Table(("#hi", "Hi #players;")).WithPlayers("A, B & C").GetWithReferences(StringTable.Id("#hi")));
        Assert.Equal("", StringTable.Empty.Get("#Players"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A", "B")]
    [InlineData("A", "B", "C")]
    public void PlayerList_JoinsNamesLikeTheOriginal(params string[] names)
    {
        var expected = names.Length switch { 1 => "A", 2 => "A & B", _ => "A, B & C" };

        Assert.Equal(expected, StringTable.PlayerList(names));
    }
}
