using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Menu;
using static OpenMagicka.Tests.Game.MenuTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Game;

/// <summary>The pages before a chapter (OM-84): the intro before the first, the world map's cutscene before the others.</summary>
public class CutscenePagesTests
{
    private static readonly Point Screen = new(1280, 720);

    private const float Tick = 1 / 60f;

    /// <summary>The cutscene before Highlands (chapter 3): the map moving down for 8 s, the narration over it.</summary>
    private static readonly MapCutscene Highlands = new([new(0, 0.78f)], [new(0, new Numerics.Vector2(1090, 1400)), new(8, new Numerics.Vector2(1090, 1130))],
        "#cutscene_03", SoundBanks.WaveBank, "cuts_cut03");

    private static (FakeMenuHost Host, TomeMenu Menu) Lobby(int chapter)
    {
        var host = new FakeMenuHost();
        host.Saves.Set(0, new CampaignSlot(chapter, chapter));
        host.Cutscenes[2] = Highlands;
        var menu = new TomeMenu(host);
        menu.Open(m => new LobbyPage(m, 0));
        return (host, menu);
    }

    private static void Run(TomeMenu menu, float seconds, MenuInput? input = null)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            menu.Update(input ?? Keys(), Screen, Tick);
        }
    }

    [Fact]
    public void AMapCutscene_ShowsTheChapter_ThenTheMapAndItsNarration_AndLoadsTheChapterAfterTheFade()
    {
        var (host, menu) = Lobby(2);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);                                // Start
        var page = Assert.IsType<MapCutscenePage>(menu.Page);
        var voice = Assert.Single(host.Voices);
        Assert.Equal("cuts_cut03", voice.Name);
        Assert.False(voice.IsPlaying);                                          // prepared: the chapter's page first

        Run(menu, MapCutscenePage.TitleTime + 0.1f);
        Assert.True(page.OnMap);
        Assert.True(voice.IsPlaying);
        Run(menu, 10);
        Assert.False(page.IsEnding);                                            // the keys are over, the narration is not
        voice.Finish();
        Run(menu, 0.1f);
        Assert.True(page.IsEnding);
        Assert.Null(host.Played);
        Run(menu, MapCutscenePage.FadeTime);

        Assert.Equal(((0, 2), true), (host.Played, host.Resumed));
        Assert.IsType<LobbyPage>(menu.Page);                                    // it went, silently
    }

    [Fact]
    public void TheMap_IsPaintedThroughItsMask_TheCamerasTexelAtThePagesCentre()
    {
        var (host, menu) = Lobby(2);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        Run(menu, MapCutscenePage.TitleTime);
        var picture = new MenuPicture();
        menu.Draw(picture, Screen);

        var painting = Assert.Single(picture.PagePaintings);
        Assert.Equal(MenuAtlas.MapMask, painting.Mask);
        Assert.Equal(new Rectangle(0, 0, 1024, 1024), painting.Area);
        Assert.Equal(-338.2f, painting.Picture.At.X, 0);
        Assert.Equal(-580f, painting.Picture.At.Y, 0);
        Assert.Equal(1597.44f, painting.Picture.Size.X, 2);
    }

    [Fact]
    public void ACutscene_IsSkipped_ByAClick_ItsNarrationLetEnd()
    {
        var (host, menu) = Lobby(2);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        Run(menu, 4);

        menu.Update(ClickOnPage(new Vector2(500, 500)), Screen, Tick);
        Assert.True(host.Voices[0].IsStopping);                                 // as authored
        Run(menu, MapCutscenePage.FadeTime + 0.1f);
        Assert.Equal((0, 2), host.Played);
    }

    [Fact]
    public void NoSkip_WhileTheBookTurnsToIt_ThenEscSkips_AndAVoiceNotYetPlayedStopsAtOnce()
    {
        var (host, menu) = Lobby(2);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        var page = Assert.IsType<MapCutscenePage>(menu.Page);

        menu.Update(ClickOnPage(new Vector2(500, 500)), Screen, Tick);         // the walking button let go as the level ended
        Assert.False(page.IsEnding);
        Run(menu, MapCutscenePage.Turning);
        menu.Update(Keys(back: true), Screen, Tick);
        Assert.True(page.IsEnding);
        Assert.True(host.Voices[0].StoppedAtOnce);                              // still on the chapter's title: never played
        menu.Update(Keys(accept: true), Screen, Tick);                         // a skip in the fade does nothing more
        Run(menu, MapCutscenePage.FadeTime + 0.1f);
        Assert.Equal((0, 2), host.Played);
    }

    [Fact]
    public void AChapterWithoutACutscene_StartsAtOnce()
    {
        var (host, menu) = Lobby(5);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        Assert.Equal((0, 5), host.Played);
    }

    [Fact]
    public void TheIntro_LeadsIn_ShowsItsSixPictures_AndTheLoadingScreenNarratesItsEnd()
    {
        var (host, menu) = Lobby(0);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        var page = Assert.IsType<IntroPage>(menu.Page);
        Assert.Equal("music_intro", host.Music);

        Run(menu, IntroPage.NarrationAt + 0.1f);
        Assert.Equal("cuts_int0", Assert.Single(host.Voices).Name);
        Run(menu, IntroPage.LeadIn - IntroPage.NarrationAt);
        Assert.Equal(0, page.Slide);                                            // the title page
        Run(menu, IntroPage.TitleTime);
        Assert.Equal(1, page.Slide);
        Run(menu, 1);
        Assert.Equal("cuts_int1", host.Voices[^1].Name);
        Run(menu, IntroPage.SlideTimes.Sum() - 1);
        Assert.True(page.IsEnding);
        Run(menu, IntroPage.FadeTime + 0.1f);

        Assert.Equal((0, 0), host.Played);
        Assert.Equal("#cutscene_intro7", host.Narrated!.TextKey);
        Assert.True(host.Narrated.Cue is { Name: "cuts_int7", IsPlaying: true });
    }

    [Fact]
    public void TheIntroSkipped_LoadsPlainly()
    {
        var (host, menu) = Lobby(0);
        using var _ = host;
        menu.Update(Keys(accept: true), Screen);
        Run(menu, 10);
        menu.Update(Keys(accept: true), Screen, Tick);
        Run(menu, IntroPage.FadeTime + 0.1f);

        Assert.Equal((0, 0), host.Played);
        Assert.Null(host.Narrated);
    }

    [Fact]
    public void TheLobby_ShowsTheChaptersPlaceOnTheMap_AroundItsCutscenesLastKey()
    {
        var (host, menu) = Lobby(0);
        using var _ = host;
        host.Cutscenes[0] = new MapCutscene([new(0, 1.333f)], [new(0, new Numerics.Vector2(1000, 1700))], null, SoundBanks.WaveBank, null);
        var picture = new MenuPicture();
        menu.Draw(picture, Screen);

        var preview = Assert.Single(picture.PagePaintings);
        Assert.Equal(LobbyPage.PreviewArea, preview.Area);
        Assert.Equal(new Rectangle(616, 1280, 768, 768), preview.Picture.Source);  // 1024 / zoom texels around the castle, kept on the map
        Assert.Null(LobbyPage.Preview(null));
    }

    [Theory]
    [InlineData(10, 10, 0)]                                                     // the first tenth: still
    [InlineData(1, 10, 1)]                                                      // the last tenth: at rest
    [InlineData(5, 10, 0.5f)]
    public void APicturesProgress_IsStillAtBothEnds(float left, float time, float progress) =>
        Assert.Equal(progress, IntroPage.Progress(left, time), 3);

    [Fact]
    public void ThePicturesWindows_PanZoomAndDrift()
    {
        Assert.Equal(new Vector4(426, 64, 852, 652), IntroPage.Window(4, 0));
        Assert.Equal(new Vector4(0, 0, 1704, 1304), IntroPage.Window(4, 1));
        Assert.Equal(new Vector4(700, 0, 852, 652), IntroPage.Window(3, 1));
        Assert.Equal(937.2f, IntroPage.Window(5, 1).Z, 3);
        Assert.Equal(new Vector4(0, 0, 852, 652), IntroPage.Window(2, 0.5f));
        Assert.Equal(400, IntroPage.Window(3, 0.5f).X);                       // between texels as it pans
    }
}
