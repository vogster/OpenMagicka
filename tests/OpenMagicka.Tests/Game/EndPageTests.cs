using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Menu;
using OpenMagicka.Game.Shell;

namespace OpenMagicka.Tests.Game;

/// <summary>The book's end page between chapters (OM-83 phase 3): the level's last frame on the page, then on.</summary>
public class EndPageTests
{
    [Fact]
    public void TheShot_IsCutToItsMiddle_AtOneAndAQuarterToOne()
    {
        Assert.Equal(new Rectangle(285, 0, 1350, 1080), MenuPass.ShotCut(1920, 1080));
        Assert.Equal(new Rectangle(0, 80, 800, 640), MenuPass.ShotCut(800, 800));    // narrower: its full width (ours)
    }

    [Fact]
    public void ThePage_ShowsTheShot_TakesNoInput_AndGoesOnOnceTheCameraIsStill()
    {
        using var host = new FakeMenuHost();
        var book = TomeRiffleTests.Book();
        var menu = new TomeMenu(host) { Book = book };
        menu.Open();
        book.EndLevel();
        menu.PushInstantly(new EndPage(menu, 0, 3));
        Assert.Empty(host.Cues);                                              // put on at once, without the riffle's sound

        var picture = new MenuPicture();
        picture.Book.Shown = true;
        menu.Draw(picture, MenuTests.Screen);
        Assert.Equal(MenuAtlas.Shot, Assert.Single(picture.Page.Sprites).Atlas);

        menu.Update(MenuInput.None with { Back = true }, MenuTests.Screen, 0.01f);
        Assert.IsType<EndPage>(menu.Page);
        Assert.True(book.IsCameraMoving);
        for (var t = 0f; t < 1 && host.Played is null; t += 0.01f)
        {
            book.Update(0.01f);
            menu.Update(MenuInput.None, MenuTests.Screen, 0.01f);
        }

        Assert.Equal((0, 3), host.Played);                                    // no map for the chapter: it loads, the page gone
        Assert.IsNotType<EndPage>(menu.Page);
    }

    [Fact]
    public void WithoutANextChapter_ItGoesBack()
    {
        using var host = new FakeMenuHost();
        var book = TomeRiffleTests.Book();
        var menu = new TomeMenu(host) { Book = book };
        menu.Open();
        menu.PushInstantly(new EndPage(menu, 0, null));
        menu.Update(MenuInput.None, MenuTests.Screen, 0.01f);

        Assert.IsType<MainPage>(menu.Page);
        Assert.True(menu.IsTurning);                                          // riffling back by its nine pages
    }

    [Fact]
    public void AChaptersEnd_ToTheEndPage_IsNotCovered_NorADefeat()
    {
        var complete = new LevelEnding(EndGameCondition.LevelComplete) { ToEndPage = true };
        var defeat = new LevelEnding(EndGameCondition.Defeat) { ToEndPage = true };
        for (var i = 0; i < 280; i++)
        {
            complete.Step(0.01f);
            defeat.Step(0.01f);
        }

        Assert.Equal(0, complete.Cover);
        Assert.Equal(0, defeat.Cover);                                        // its restart fades the screen out, after the word
    }
}
