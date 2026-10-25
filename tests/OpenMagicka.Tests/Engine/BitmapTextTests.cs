using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Engine.Dialogs;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Tests.Engine;

/// <summary>The bitmap text engine (OM-49): measuring, wrapping and typing as the original's.</summary>
public class BitmapTextTests
{
    /// <summary>Letters 10 wide, a space 5, line height 20, spacing 1; "av" kerned by −2; '?' the default.</summary>
    internal static BitmapFont Font(int spacing = 1)
    {
        var glyphs = "abcdefghijklmnopqrstuvwxyz?[.,!".Select((c, i) => new GlyphContent(new Rune(c), i * 10, 0, 10, 20, 10, 0, false))
            .Append(new GlyphContent(new Rune(' '), 400, 0, 1, 20, 5, 0, false)).ToList();
        return new BitmapFont(new BitmapFontContent(20, 15, new Rune('?'), default, glyphs, [new KerningPair(new Rune('a'), new Rune('v'), -2)]), spacing);
    }

    [Fact]
    public void Measuring_AddsEachGlyphsAdvanceAndSpacing_KernsPairs_SkipsTags_AndTakesOffOneSpacing()
    {
        var font = Font();

        Assert.Equal((32, 20), font.Measure("abc"));                                                   // 3 × (10 + 1) − 1
        Assert.Equal((30, 20), font.Measure("av c"[..2] + "c"));                                       // the pair kerned by 2
        Assert.Equal((32, 20), font.Measure("a[c=1,0,0]b[/c]c"));
        Assert.Equal((21, 20), font.Measure("[[a"));                                                   // [[ is one [
        Assert.Equal((43, 40), font.Measure("abc\nabc"));                                              // the break measured as a '?', as the original's
    }

    [Fact]
    public void Wrapping_BreaksAtTheLastSpaceThatFits_KeepsBreaks_AndWidensForALongWord()
    {
        var font = Font();

        Assert.Equal("aaa bbb\nccc", font.Wrap("aaa bbb ccc", 80));
        Assert.Equal("aaa\nbbb", font.Wrap("aaa\nbbb", 200));
        Assert.Equal("aaaaaaaaa\nb", font.Wrap("aaaaaaaaa b", 30));                                     // the first word widens every line
        Assert.Equal("aaa", font.Wrap("aaa", 5));
        Assert.Equal("[c=1,1,1]aaa[/c]\nbbb", font.Wrap("[c=1,1,1]aaa[/c] bbb", 40));                 // tags take no room
    }

    [Fact]
    public void Typing_Shows40GlyphsASecond_PausesAfterPunctuationAndAtPTags_AndCanBeFinished()
    {
        var typing = new TypingText();
        typing.Set("ab. c[p=1]d\ne");

        Assert.Equal(7, typing.Glyphs);                                                                // a b . space c d e
        typing.Update(0.001f);
        Assert.Equal(1, typing.Visible);
        typing.Update(1 / 40f);
        Assert.Equal(2, typing.Visible);
        typing.Update(1 / 40f);
        Assert.Equal(3, typing.Visible);                                                               // the full stop
        typing.Update(0.2f);
        Assert.Equal(3, typing.Visible);                                                               // a quarter second after it
        typing.Update(0.1f);
        Assert.Equal(5, typing.Visible);                                                               // the space and c, then a second's pause
        typing.Update(0.5f);
        Assert.Equal(5, typing.Visible);
        typing.Finish();
        Assert.True(typing.IsFinished);
    }

    [Fact]
    public void ALine_IsItsLocString_WithItsArgumentsAndReferencesInWhite()
    {
        var strings = StringTable.Create([new LocEntry("#greet", "Hail #1;, see #place;!"), new LocEntry("#hero", "Vlad"), new LocEntry("#place", "the inn")]);
        var line = new DialogLine("#greet", ["#hero"], default, null, Animation.None, -1);

        Assert.Equal("Hail [c=1,1,1]Vlad[/c], see [c=1,1,1]the inn[/c]!", DialogText.Resolve(line, strings));
    }
}
