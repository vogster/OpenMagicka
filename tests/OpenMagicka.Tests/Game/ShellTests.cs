using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game;
using OpenMagicka.Game.Menu;
using OpenMagicka.Game.Shell;

namespace OpenMagicka.Tests.Game;

/// <summary>The game around the levels (OM-53): the pause menu, a level's ending, the campaign's slots, the menu's input.</summary>
public class ShellTests
{
    private static readonly Point Screen = new(1280, 720);

    [Fact]
    public void ThePauseMenu_SelectsNothingFromTheKeyboard_AndBackResumes()
    {
        using var host = new FakeMenuHost();
        var (resumed, restarted, quit) = (0, 0, 0);
        var pause = new PauseMenu(host, new PauseActions(() => resumed++, () => restarted++, () => quit++));
        pause.Open(pad: false);
        Assert.Equal(-1, pause.Selected);

        pause.Update(MenuTests.Keys(vertical: 1), Screen);
        Assert.Equal(0, pause.Selected);
        pause.Update(MenuTests.Keys(back: true), Screen);
        Assert.Equal(1, resumed);

        pause.Open(pad: true);
        Assert.Equal(0, pause.Selected);
        pause.Update(new MenuInput(null, default, false, false, 0, 0, false, false, false, 0, Start: true), Screen);
        Assert.Equal(2, resumed);                                             // Start closes what it opened
    }

    [Fact]
    public void ThePauseMenu_AsksBeforeRestartingOrQuitting()
    {
        using var host = new FakeMenuHost();
        var (restarted, quit) = (0, 0);
        var pause = new PauseMenu(host, new PauseActions(() => { }, () => restarted++, () => quit++));
        pause.Open(pad: true);

        pause.Update(MenuTests.Keys(vertical: 1), Screen);
        pause.Update(MenuTests.Keys(accept: true), Screen);
        pause.Update(MenuTests.Keys(accept: true), Screen);                   // No is first
        Assert.Equal(0, restarted);
        pause.Update(MenuTests.Keys(accept: true), Screen);
        pause.Update(MenuTests.Keys(vertical: -1), Screen);
        pause.Update(MenuTests.Keys(accept: true), Screen);
        Assert.Equal(1, restarted);

        var row = PauseMenu.Row(3, Screen);
        pause.Update(new MenuInput(null, row.Center, true, true, 0, 0, false, false, false, 0), Screen);
        pause.Update(MenuTests.Keys(vertical: -1), Screen);
        pause.Update(MenuTests.Keys(accept: true), Screen);
        Assert.Equal(1, quit);
    }

    [Fact]
    public void ThePauseMenusOptions_AreTheTomesPages_BackClosesThem()
    {
        using var host = new FakeMenuHost();
        var pause = new PauseMenu(host, new PauseActions(() => { }, () => { }, () => { }));
        pause.Open(pad: true);
        pause.Update(MenuTests.Keys(vertical: 1), Screen);
        pause.Update(MenuTests.Keys(vertical: 1), Screen);
        pause.Update(MenuTests.Keys(accept: true), Screen);
        Assert.True(pause.InOptions);

        var picture = new MenuPicture();
        pause.Draw(picture, Screen);
        Assert.True(picture.ShowsPage);                                       // over the level

        pause.Update(MenuTests.Keys(back: true), Screen);
        Assert.False(pause.InOptions);
        picture.Clear();
        pause.Draw(picture, Screen);
        Assert.False(picture.ShowsPage);
        Assert.Equal(4, picture.ScreenTexts.Count);
    }

    [Fact]
    public void AChaptersEnd_ShowsItsWordWithItsMusic_ThenGoesOn()
    {
        var ending = new LevelEnding(EndGameCondition.LevelComplete);
        Assert.Equal("music_fanfare", ending.Step(0.1f));
        Assert.Null(ending.Step(0.1f));                                       // once
        for (var t = 0.2f; t < 1.5f; t += 0.1f)
        {
            ending.Step(0.1f);
        }
        Assert.Equal(1, ending.WordAlpha, 3);
        Assert.Equal(0, ending.Cover);
        for (var t = 1.5f; t < 3.05f; t += 0.1f)
        {
            ending.Step(0.1f);
        }
        Assert.True(ending.IsOver);
        Assert.Equal(1, ending.Cover);
        Assert.Equal(EndingNext.NextChapter, ending.Next);
        Assert.Equal("#add_game_level_complete", ending.Word);
    }

    [Fact]
    public void ADefeatByDeath_WaitsItsDelay_ThenRestarts()
    {
        var ending = new LevelEnding(EndGameCondition.Defeat, LevelEnding.DeathDelay);
        Assert.Null(ending.Step(2.9f));
        Assert.True(ending.Waiting);
        Assert.Equal("music_defeat", ending.Step(0.2f));
        Assert.False(ending.Waiting);
        Assert.Equal(EndingNext.Restart, ending.Next);
    }

    [Fact]
    public void APhonyEnding_OnlyShows_AndToBeContinuedFadesForEightSeconds()
    {
        var phony = new LevelEnding(EndGameCondition.LevelComplete, phony: true);
        phony.Step(2.5f);
        Assert.Equal((EndingNext.None, 0f), (phony.Next, phony.Cover));

        var continued = new LevelEnding(EndGameCondition.ToBeContinued);
        Assert.Null(continued.Step(4));
        Assert.Equal(0.5f, continued.Cover, 3);
        Assert.Null(continued.Word);
        continued.Step(4);
        Assert.True(continued.IsOver);
        Assert.Equal(EndingNext.NextChapter, continued.Next);
    }

    [Fact]
    public void AChapterCompleted_GoesToTheNext_TheLastLoops()
    {
        Assert.Equal(new CampaignSlot(3, 3), new CampaignSlot(2, 2).Completed());
        Assert.Equal(new CampaignSlot(1, 5), new CampaignSlot(0, 5).Completed());  // the furthest reached stays
        Assert.Equal(new CampaignSlot(0, 11, Looped: true), new CampaignSlot(11, 11).Completed());
        Assert.Equal(12, Chapters.Count);
        Assert.Equal(("#chapter_01", "#chaptertitle_12"), (Chapters.NameKey(0), Chapters.TitleKey(11)));
    }

    [Fact]
    public void PlayTime_IsWrittenAsTheOriginals_UpTo99Hours()
    {
        Assert.Equal("01:02:03", Chapters.Time(3723));
        Assert.Equal("99:59:59", Chapters.Time(1e9));
        Assert.Equal("00:00:00", Chapters.Time(double.NaN));
    }

    [Fact]
    public void TheSaves_RoundTrip_AndABadFileGivesEmptySlots()
    {
        var folder = Directory.CreateTempSubdirectory("om53");
        try
        {
            var path = Path.Combine(folder.FullName, "campaign.json");
            CampaignSaves.Load(path).Set(1, new CampaignSlot(40, -3, Seconds: -5)); // clamped to the chapters there are
            var read = CampaignSaves.Load(path);
            Assert.Equal((null, new CampaignSlot(11, 11), null), (read[0], read[1], read[2]));

            File.WriteAllText(path, "{ not json");
            Assert.Null(CampaignSaves.Load(path)[1]);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void MenuInput_ChoosesOnAPress_AndRepeatsAHeldMove()
    {
        var reader = new MenuInputReader();
        InputState State(params Keys[] keys) => new(new KeyboardState(keys), default, true, Screen);

        reader.Read(State(), 1 / 60f);
        Assert.True(reader.Read(State(Keys.Enter), 1 / 60f).Accept);
        Assert.False(reader.Read(State(Keys.Enter), 1 / 60f).Accept);         // held: once
        Assert.Equal(1, reader.Read(State(Keys.Down), 1 / 60f).Vertical);
        Assert.Equal(0, reader.Read(State(Keys.Down), 0.1f).Vertical);
        Assert.Equal(1, reader.Read(State(Keys.Down), 0.2f).Vertical);        // after a quarter of a second
        Assert.Equal(default, reader.Read(new InputState(new KeyboardState(Keys.Enter), default, false, Screen), 1 / 60f));
    }

    [Fact]
    public void TheResolutions_AreEachOnce_SmallestFirst_From800x600()
    {
        Assert.Equal([new Point(800, 600), new Point(1280, 720), new Point(1920, 1080)],
            GameShell.Resolutions([new(1920, 1080), new(640, 480), new(1280, 720), new(800, 600), new(1920, 1080)]));
    }

    [Fact]
    public void TheGame_IsWhatRuns_UnlessTheViewerIsAskedFor()
    {
        var folder = Directory.CreateTempSubdirectory("om53");
        try
        {
            Assert.False(GameOptions.Parse([], _ => null).WantsViewer);
            Assert.True(GameOptions.Parse(["--content", folder.FullName, "--viewer"], _ => null).WantsViewer);
            Assert.True(GameOptions.Parse(["--content", folder.FullName, "--scene", "x"], _ => null).WantsViewer);
            Assert.Throws<ArgumentException>(() => GameOptions.Parse(["--viewer"], _ => null));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheLoadingScreen_ShowsTheChapterAndATip()
    {
        var picture = new MenuPicture();
        LoadingScreen.Write(picture, OpenMagicka.Content.Localization.StringTable.Empty, 2, 5, 1.2, Screen);

        Assert.Equal(MenuAtlas.Loading, Assert.Single(picture.ScreenSprites).Atlas);
        Assert.Equal(new Vector2(280, 0), picture.ScreenSprites[0].At);       // a square of the screen's height in its middle
        Assert.Equal(4, picture.ScreenTexts.Count);
        Assert.Equal("#tip05", LoadingScreen.TipKey(5));
    }
}
