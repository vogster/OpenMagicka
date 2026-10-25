using Microsoft.Xna.Framework;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Install;
using OpenMagicka.Game.Menu;

namespace OpenMagicka.Tests.Game;

/// <summary>A menu's host that keeps what the menu asks of it.</summary>
internal sealed class FakeMenuHost : IMenuHost, IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("om53");

    public FakeMenuHost() => Saves = CampaignSaves.Load(Path.Combine(folder.FullName, "campaign.json"));

    public StringTable Strings { get; } = StringTable.Empty;

    public GameSettings Settings { get; private set; } = new();

    public CampaignSaves Saves { get; }

    public IReadOnlyList<string> Languages { get; } = ["eng", "rus"];

    public IReadOnlyList<Point> Resolutions { get; } = [new(800, 600), new(1280, 720)];

    public List<string> Cues { get; } = [];

    public (int Slot, int Chapter)? Played { get; private set; }

    public bool Quitted { get; private set; }

    public string SavesPath => Path.Combine(folder.FullName, "campaign.json");

    public void Apply(GameSettings settings) => Settings = settings.Clamped();

    public void Cue(string name) => Cues.Add(name);

    public void Play(int slot, int chapter) => Played = (slot, chapter);

    public void Quit() => Quitted = true;

    public void Dispose() => folder.Delete(recursive: true);
}

/// <summary>The Tome's pages (OM-53): the main page, the save slots, the lobby, the options.</summary>
public class MenuTests
{
    internal static MenuInput Keys(int vertical = 0, int horizontal = 0, bool accept = false, bool back = false) =>
        new(null, default, false, false, vertical, horizontal, accept, back, false, 0);

    internal static MenuInput ClickOnPage(Vector2 at) => new(at, default, true, true, 0, 0, false, false, false, 0);

    private static readonly Point Screen = new(1280, 720);

    [Fact]
    public void MainPage_MovesPastTheTilesNotInTheBeta_AndAdventureOpensTheSlots()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open();
        Assert.IsType<MainPage>(menu.Page);
        Assert.Equal(0, menu.Page!.Selected);

        menu.Update(Keys(vertical: 1), Screen);
        Assert.Equal(5, menu.Page.Selected);                                  // the options button: DLC, Challenges, Versus, Online are skipped
        menu.Update(Keys(vertical: -1), Screen);
        menu.Update(Keys(accept: true), Screen);

        Assert.IsType<SlotsPage>(menu.Page);
        Assert.Contains("ui_menu_tomepage", host.Cues);
    }

    [Fact]
    public void BackOnTheMainPage_AsksToQuit_YesQuits()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open();

        menu.Update(Keys(back: true), Screen);
        Assert.True(menu.HasBox);
        menu.Update(Keys(accept: true), Screen);                              // Yes is chosen first, as the original's

        Assert.True(host.Quitted);
        Assert.False(menu.HasBox);
    }

    [Fact]
    public void AnEmptySlot_StartsACampaign_ItsLobbyStartsItsChapter()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open(m => new SlotsPage(m));

        menu.Update(ClickOnPage(new Vector2(512, 448)), Screen);              // the second slot

        Assert.IsType<LobbyPage>(menu.Page);
        Assert.Equal(new CampaignSlot(0, 0), host.Saves[1]);
        Assert.Equal(new CampaignSlot(0, 0), CampaignSaves.Load(host.SavesPath)[1]);
        menu.Update(Keys(accept: true), Screen);                              // Start is selected first
        Assert.Equal((1, 0), host.Played);
    }

    [Fact]
    public void AFilledSlot_IsDeletedAfterAsking_NoIsTheFirstAnswer()
    {
        using var host = new FakeMenuHost();
        host.Saves.Set(0, new CampaignSlot(4, 6, Seconds: 100));
        var menu = new TomeMenu(host);
        menu.Open(m => new SlotsPage(m));
        var page = menu.Page!;
        Assert.True(page.Items[0].Enabled);
        Assert.False(page.Items[1].Enabled);                                  // an empty slot has no delete icon

        menu.Update(ClickOnPage(new Vector2(870, 260)), Screen);              // the icon, on its slot's plate
        Assert.True(menu.HasBox);
        menu.Update(Keys(accept: true), Screen);                              // No
        Assert.NotNull(host.Saves[0]);

        menu.Update(Keys(), Screen);
        page.Selected = SlotsPage.FirstSlot;
        menu.Update(new MenuInput(null, default, false, false, 0, 0, false, false, true, 0), Screen); // the Delete key on the slot
        menu.Update(Keys(vertical: -1), Screen);
        menu.Update(Keys(accept: true), Screen);                              // Yes
        Assert.Null(host.Saves[0]);
        Assert.False(page.Items[0].Enabled);
    }

    [Fact]
    public void TheLobby_OffersTheChaptersReached()
    {
        using var host = new FakeMenuHost();
        host.Saves.Set(2, new CampaignSlot(1, 3));
        var menu = new TomeMenu(host);
        menu.Open(m => new LobbyPage(m, 2));

        menu.Update(ClickOnPage(new Vector2(512, 120)), Screen);              // the chapter block
        Assert.True(menu.HasBox);
        menu.Update(Keys(vertical: 1), Screen);
        menu.Update(Keys(vertical: 1), Screen);
        menu.Update(Keys(vertical: 1), Screen);                               // no further than chapter 4 (index 3)
        menu.Update(Keys(accept: true), Screen);

        Assert.Equal(new CampaignSlot(3, 3), host.Saves[2]);
    }

    [Fact]
    public void Settings_TurnAndAreApplied()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open(SettingsPage.Sound);

        for (var i = 0; i < 5; i++)
        {
            menu.Update(Keys(horizontal: 1), Screen);
        }
        Assert.Equal(GameSettings.MaxVolume, host.Settings.MusicVolume);     // 7 up to 10, no further
        menu.Update(Keys(vertical: 1), Screen);
        menu.Update(Keys(horizontal: -1), Screen);
        Assert.Equal(GameSettings.DefaultVolume - 1, host.Settings.SoundVolume);
        Assert.Contains("misc_thud01", host.Cues);                            // the sounds' sample

        menu.Open(SettingsPage.Game);
        menu.Update(Keys(vertical: 1), Screen);
        menu.Update(Keys(horizontal: 1), Screen);
        Assert.Equal(HealthBarOption.PlayersOnly, host.Settings.HealthBars);
        menu.Update(Keys(horizontal: 1), Screen);
        menu.Update(Keys(horizontal: 1), Screen);
        Assert.Equal(HealthBarOption.On, host.Settings.HealthBars);           // round
        menu.Update(Keys(vertical: -1), Screen);
        menu.Update(Keys(accept: true), Screen);
        Assert.False(host.Settings.DamageNumbers);
    }

    [Fact]
    public void TheLanguageAndResolutionBoxes_ApplyTheChoice()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open(SettingsPage.Game);
        menu.Page!.Selected = 3;
        menu.Update(Keys(accept: true), Screen);
        Assert.True(menu.HasBox);
        menu.Update(Keys(vertical: 1), Screen);
        menu.Update(Keys(accept: true), Screen);
        Assert.Equal("rus", host.Settings.Language);

        menu.Open(SettingsPage.Graphics);
        menu.Page!.Selected = 1;
        menu.Update(Keys(accept: true), Screen);
        menu.Update(Keys(accept: true), Screen);
        Assert.Equal((800, 600), (host.Settings.Width, host.Settings.Height));
    }

    [Fact]
    public void LanguageNames_AreTheirOwn()
    {
        Assert.Equal("Deutsch", SettingsPage.LanguageName("deu"));
        Assert.Equal(7, SettingsPage.LanguageName("rus").Length);
        Assert.Equal("xyz", SettingsPage.LanguageName("xyz"));
    }

    [Fact]
    public void APage_DrawsOnThePaper_ItsBoxOnTheScreen()
    {
        using var host = new FakeMenuHost();
        var menu = new TomeMenu(host);
        menu.Open();
        var picture = new MenuPicture();

        menu.Draw(picture, Screen);
        Assert.True(picture.ShowsPage);
        Assert.Contains(picture.PageSprites, s => s.Atlas == MenuAtlas.PagesNew);
        Assert.Empty(picture.ScreenSprites);

        menu.AskToQuit();
        picture.Clear();
        menu.Draw(picture, Screen);
        Assert.Contains(picture.ScreenSprites, s => s.Source == MessageBox.Parchment);
        Assert.Equal(3, picture.ScreenTexts.Count);                           // the question, Yes, No
    }

    [Fact]
    public void AMessageBox_OpensWithItsChoiceInSight()
    {
        var box = new MessageBox("", [.. Enumerable.Range(0, 20).Select(i => $"{i}")], 15, _ => { });

        Assert.Equal(15, box.Rows(1280, 720, TomeMenu.BoxRow).Last().Option);
    }

    [Fact]
    public void AMessageBox_ScrollsALongList()
    {
        int? answer = -1;
        var box = new MessageBox("", [.. Enumerable.Range(0, 20).Select(i => $"{i}")], 0, a => answer = a);
        for (var i = 0; i < 12; i++)
        {
            box.Update(Keys(vertical: 1), 1280, 720, TomeMenu.BoxRow, _ => { });
        }

        Assert.Equal(12, box.Selected);
        Assert.Equal(3, box.Rows(1280, 720, TomeMenu.BoxRow).First().Option); // the list scrolled to keep it in sight
        box.Update(Keys(back: true), 1280, 720, TomeMenu.BoxRow, _ => { });
        Assert.Null(answer);                                                  // back closes it unchosen
    }
}
