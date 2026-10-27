using System.Numerics;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Menu;
using OpenMagicka.Gameplay.Animation;

namespace OpenMagicka.Tests.Game;

/// <summary>The Tome's riffle (OM-83 phase 2): its sheets' timing, and the menu's pages through it.</summary>
public class TomeRiffleTests
{
    private static readonly MenuInput Accept = MenuInput.None with { Accept = true };

    /// <summary>How long a riffle of a count runs at a frame of 1/100 s.</summary>
    private static float Length(int pages)
    {
        var riffle = new TomeRiffle();
        riffle.Start(new AnimationClip("flip_left", 1), pages);
        var time = 0f;
        while (!riffle.IsOver && time < 5)
        {
            riffle.Update(0.01f);
            time += 0.01f;
        }
        return time;
    }

    [Theory]
    [InlineData(1, 0.5f)]
    [InlineData(3, 0.625f)]
    [InlineData(8, 0.9375f)]
    [InlineData(10, 1.0625f)]
    [InlineData(20, 1.0625f)]
    public void ARiffle_LastsHalfASecond_AndASixteenthMoreForEachSheetAfterTheFirst(int pages, float about) =>
        Assert.InRange(Length(pages), about - 0.03f, about + 0.03f);

    [Fact]
    public void ARiffle_TurnsAtMostTenSheets_AndNothingForNoPages()
    {
        var riffle = new TomeRiffle();
        riffle.Start(new AnimationClip("flip_left", 1), 20);
        Assert.Equal(10, riffle.Count);

        riffle.Start(new AnimationClip("flip_left", 1), 0);
        Assert.True(riffle.IsOver);
        Assert.Equal(0, riffle.Count);
    }

    [Fact]
    public void ARiffle_StartedOverAnother_LastsItsOwnTime()
    {
        var riffle = new TomeRiffle();
        riffle.Start(new AnimationClip("flip_left", 1), 10);
        riffle.Update(0.03f);                                                 // a sheet let go, the next one waiting
        riffle.Start(new AnimationClip("flip_left", 1), 1);
        var time = 0f;
        while (!riffle.IsOver && time < 5)
        {
            riffle.Update(0.01f);
            time += 0.01f;
        }

        Assert.InRange(time, 0.47f, 0.53f);
    }

    [Fact]
    public void TheSheets_AllStartAtOnce_AndTheLastOnesTurnAgain()
    {
        var riffle = new TomeRiffle();
        riffle.Start(new AnimationClip("flip_left", 1), 10);
        for (var i = 0; i < 40; i++)
        {
            riffle.Update(0.01f);
        }
        Assert.True(riffle[0].Time > 0.6f);                                   // sheet 0 has moved all along, not let go again yet
        Assert.True(riffle[5].Time < 0.4f);                                   // let go again at 0.25 s, from its start
        for (var i = 0; i < 30; i++)
        {
            riffle.Update(0.01f);
        }
        Assert.True(riffle[0].Time < 0.5f);                                   // let go again at 0.5625 s: it turns a second time
    }

    [Fact]
    public void APush_RifflesForward_TheOldPageOnTheSheets_AndTheMenuWaitsForIt()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host) { Book = Book() };
        menu.Open();
        menu.Update(MenuInput.None with { Vertical = 1 }, MenuTests.Screen);
        menu.Update(Accept, MenuTests.Screen);                                 // the options page
        var options = menu.Page;
        Assert.True(menu.IsTurning);

        var during = Drawn(menu);
        Assert.False(during.Sheet.IsEmpty);                                   // the main page rides the sheets away
        Assert.Equal(Alone(options!).Page.Texts.Count, during.Page.Texts.Count);  // the new page under them

        menu.Update(Accept, MenuTests.Screen);
        menu.Pop();
        Assert.Same(options, menu.Page);                                      // locked: no choice, no going back

        Run(menu, 0.6f);
        Assert.False(menu.IsTurning);
        Assert.True(Drawn(menu).Sheet.IsEmpty);
        menu.Pop();
        Assert.IsType<MainPage>(menu.Page);
        Assert.True(menu.IsTurning);
        Assert.Equal(Alone(options!).Page.Texts.Count, Drawn(menu).Page.Texts.Count);   // the page going stays on the right till the sheets land
    }

    [Fact]
    public void WithoutTheBook_PagesChangeAtOnce()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open();
        menu.Push(new SlotsPage(menu));
        menu.Push(new SlotsPage(menu));

        Assert.False(menu.IsTurning);
        Assert.Equal(2, host.CueVariables.Count);
    }

    private static void Run(TomeMenu menu, float seconds)
    {
        for (var t = 0f; t < seconds; t += 0.01f)
        {
            menu.Update(MenuInput.None, MenuTests.Screen, 0.01f);
            menu.Book!.Update(0.01f);
        }
    }

    private static MenuPicture Drawn(TomeMenu menu)
    {
        var picture = new MenuPicture();
        picture.Book.Shown = true;
        menu.Draw(picture, MenuTests.Screen);
        return picture;
    }

    private static MenuPicture Alone(MenuPage page)
    {
        var picture = new MenuPicture();
        picture.Book.Shown = true;
        page.Draw(picture);
        return picture;
    }

    /// <summary>A book of bones and clips alone (no game files): a rig with its camera and light, the book, a page.</summary>
    internal static TomeBook Book()
    {
        static SkeletonBone Bone(string name, int parent, Vector3 at = default) =>
            new(name, parent, BonePose.Identity with { Translation = at }, Matrix4x4.Identity);
        static AnimatedModel Model(SkeletonBone[] bones, params string[] clips) =>
            new(new Skeleton(bones), clips.ToDictionary(c => c, c => new AnimationClip(c, 1)));
        var rig = Model([Bone("root", -1), Bone("camera", 0, new Vector3(0, 9, 0)), Bone("Light", 0, new Vector3(-1, 8, 3))], "idle");
        var page = Model([Bone("root", -1)], "open_left", "open_right", "flip_left", "flip_right");
        return new TomeBook(rig, Model([Bone("root", -1)], "open"), page, page, new Candle(() => 0));
    }
}
