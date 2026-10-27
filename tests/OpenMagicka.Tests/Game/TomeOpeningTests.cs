using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Menu;

namespace OpenMagicka.Tests.Game;

/// <summary>The Tome's book shut, flying in and opening (OM-83 phase 3), on a book of bones alone (its clips a second long).</summary>
public class TomeOpeningTests
{
    private static TomeBookEvents Run(TomeBook book, float seconds)
    {
        var events = TomeBookEvents.None;
        for (var t = 0f; t < seconds; t += 0.01f)
        {
            events |= book.Update(0.01f);
        }
        return events;
    }

    [Fact]
    public void TheShutBook_FliesInOnItsFirstStep_TheCandleLitAfterASecondAndAHalf()
    {
        var book = TomeRiffleTests.Book(open: false);
        Assert.Equal(TomeBookState.Closed, book.State);
        Assert.False(book.IsCameraMoving);                                    // still till the menu's first step

        Run(book, 1.2f);
        Assert.Equal(0, book.Candle.Strength, 3);                             // out (no wobble from this candle)
        Run(book, 0.5f);
        Assert.True(book.Candle.Strength > 0);
        Assert.Equal(TomeBookState.Closed, book.State);                       // never opened unasked
    }

    [Fact]
    public void AskedToOpen_AfterItsFirstSecond_ItHurries_ThenOpens_LockedTillItLies()
    {
        var book = TomeRiffleTests.Book(open: false);
        Run(book, 0.5f);
        Assert.False(book.AskToOpen());                                       // too soon
        Run(book, 0.55f);
        Assert.True(book.AskToOpen());
        Assert.False(book.AskToOpen());                                       // once

        var events = Run(book, 0.2f);                                         // what is left of the fly-in, six times fast
        Assert.Equal(TomeBookEvents.Opening, events);
        Assert.Equal(TomeBookState.Opening, book.State);
        Assert.True(book.IsLocked);
        Assert.True(book.IsShut);
        Assert.Null(book.ToPage(new Microsoft.Xna.Framework.Point(640, 360), new Microsoft.Xna.Framework.Point(1280, 720)));

        Assert.Equal(TomeBookEvents.None, Run(book, 1.1f));
        Assert.Equal(TomeBookState.Open, book.State);
        Assert.False(book.IsLocked);
        Assert.False(book.IsCameraMoving);                                    // the settling ran six times fast too, as the original's
    }

    [Fact]
    public void TheMenu_OverTheShutBook_ShowsBarePaper_AndAChoiceOpensIt()
    {
        using var host = new FakeMenuHost();
        var book = TomeRiffleTests.Book(open: false);
        var menu = new TomeMenu(host) { Book = book };
        menu.Open();
        Run(book, 1.1f);

        var picture = new MenuPicture();
        picture.Book.Shown = true;
        menu.Draw(picture, MenuTests.Screen);
        Assert.True(picture.ShowsPage);
        Assert.True(picture.Page.IsEmpty);

        menu.Update(MenuInput.None with { Accept = true }, MenuTests.Screen);
        Assert.IsType<MainPage>(menu.Page);                                   // the choice opened the book, not the page's item
        Run(book, 1.5f);
        Assert.True(book.IsOpen);
    }
}
