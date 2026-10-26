using OpenMagicka.Engine.Hud;
using OpenMagicka.Game.Install;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Game;

/// <summary>Finding Magicka (OM-4) and the player's settings (OM-53).</summary>
public class InstallTests
{
    private static string FakeMagicka(string root)
    {
        var folder = Path.Combine(root, "steamapps", "common", "Magicka");
        Directory.CreateDirectory(Path.Combine(folder, "Content", "Levels"));
        Directory.CreateDirectory(Path.Combine(folder, "Content", "Audio"));
        return folder;
    }

    [Fact]
    public void SteamsLibraryList_GivesItsPaths()
    {
        const string text = """
            "libraryfolders"
            {
                "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "228980" "1" } }
                // a comment
                "1" { "path" "D:\\SteamLibrary" "apps" { "42910" "1809617517" } }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], Vdf.Values(text, "path"));
    }

    [Fact]
    public void Magicka_IsFoundWhereGiven_ElseInASteamLibrary_ByItsContent()
    {
        var root = Directory.CreateTempSubdirectory("om4");
        try
        {
            var steam = Path.Combine(root.FullName, "Steam");
            var library = Path.Combine(root.FullName, "Library");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam.Replace(@"\", @"\\")}\" }} \"1\" {{ \"path\" \"{library.Replace(@"\", @"\\")}\" }} }}");
            var folder = FakeMagicka(library);

            var found = MagickaInstall.Find([null, Path.Combine(root.FullName, "nowhere")], [steam]);
            Assert.Equal(Path.GetFullPath(folder), found?.Folder);
            Assert.Null(found!.Version);                                                            // no Magicka.exe in the fake
            Assert.Equal(Path.GetFullPath(folder), MagickaInstall.At(Path.Combine(folder, "Content"))?.Folder);   // its Content folder works too
            Assert.Null(MagickaInstall.At(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [GameContentFact]
    public void TheGame_IsTheKnownVersion()
    {
        var install = MagickaInstall.At(GameContent.ContentDirectory);

        Assert.NotNull(install);
        Assert.True(install.IsKnownVersion, install.Version);
    }

    [Fact]
    public void Settings_RoundTrip_AndClampWhatTheyDoNotAllow()
    {
        var dir = Directory.CreateTempSubdirectory("om53");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            Assert.Equal(new GameSettings(), GameSettings.Load(path));                               // none yet: the defaults

            new GameSettings { MusicVolume = 3, Language = "rus", Fullscreen = true, HealthBars = HealthBarOption.PlayersOnly }.Save(path);
            var read = GameSettings.Load(path);
            Assert.Equal((3, "rus", true, HealthBarOption.PlayersOnly, 7), (read.MusicVolume, read.Language, read.Fullscreen, read.HealthBars, read.SoundVolume));

            File.WriteAllText(path, """{ "MusicVolume": 99, "Language": "../x", "Width": -5 }""");
            var clamped = GameSettings.Load(path);
            Assert.Equal((10, "eng", (int?)null), (clamped.MusicVolume, clamped.Language, clamped.Width));

            File.WriteAllText(path, "not json");
            Assert.Equal(new GameSettings(), GameSettings.Load(path));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheInterfaceSize_IsTheOneChosen_ElseTheScreensHeightAgainst1080_NeverBelowTheOriginals()
    {
        Assert.Equal(1f, new GameSettings().InterfaceScale(720));
        Assert.Equal(1f, new GameSettings().InterfaceScale(1080));
        Assert.Equal(2f, new GameSettings().InterfaceScale(2160));                    // a 4K screen
        Assert.Equal(1.5f, new GameSettings { InterfaceSize = 150 }.InterfaceScale(2160));
        Assert.Equal(1.25f, new GameSettings().InterfaceScale(1440));                   // whole quarters
        Assert.Equal(1.5f, new GameSettings { InterfaceSize = 300 }.InterfaceScale(720));  // the pieces' screen stays 480 lines high
        var path = Path.Combine(Path.GetTempPath(), $"om87-{Guid.NewGuid():N}.json");
        try
        {
            new GameSettings { InterfaceSize = 150 }.Save(path);
            Assert.Equal(150, GameSettings.Load(path).InterfaceSize);
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Null(new GameSettings { InterfaceSize = 133 }.Clamped().InterfaceSize);     // not one offered: by the screen
        Assert.Equal(125, new GameSettings { InterfaceSize = 125 }.Clamped().InterfaceSize);
    }
}
