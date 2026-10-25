using OpenMagicka.Game;
using OpenMagicka.Game.Install;
using OpenMagicka.Game.Play;
using OpenMagicka.Game.Shell;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var log = LogFile.Start(args);
        // Before FNA starts: with the stock FNA3D, the drivers it would pick first draw some
        // original effects wrongly (our own FNA3D, where shipped, fixes them).
        // FNA3D reads the choice as an SDL hint (an environment variable set from .NET would not
        // reach native code on Unix); a normal-priority hint leaves FNA's own /gldevice: option first.
        if (GraphicsDriver.Default(Environment.GetEnvironmentVariable(GraphicsDriver.Variable), GraphicsDriver.HasOwnFna3d()) is { } driver)
        {
            SDL3.SDL.SDL_SetHint(GraphicsDriver.Variable, driver);
        }
        GameOptions options;
        try
        {
            options = GameOptions.Parse(args, Environment.GetEnvironmentVariable);
        }
        catch (Exception e) when (e is ArgumentException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"OpenMagicka: {e.Message}");
            return 2;
        }
        if (options is { ContentDirectory: { } levelContent, Level: { } level })
        {
            return RunLevel(levelContent, level, options.Screenshot);
        }
        if (options.CheckEffects || options.Placeholder)
        {
            using var check = new OpenMagickaGame(options);
            check.Run();
            return check.ExitCode;
        }
        if (options is { ContentDirectory: { } content, WantsViewer: true })
        {
            return RunViewer(content, options);
        }
        return RunGame(options);
    }

    /// <summary>The game (OM-53): the install the player gave or Steam has, the settings and saves; a box saying so when there is no Magicka.</summary>
    private static int RunGame(GameOptions options)
    {
        var settings = GameSettings.Load(GameSettings.DefaultPath);
        var given = new[] { options.ContentDirectory, settings.MagickaFolder };
        var install = MagickaInstall.Find(given, MagickaInstall.SteamRoots());
        if (install is null && options.Screenshot is null)
        {
            Console.WriteLine("OpenMagicka: Magicka is not in Steam's libraries; asking for its folder");
            using var questions = new SdlQuestions();
            install = InstallChooser.Ask(questions, settings.MagickaFolder);  // a copy outside Steam: the player shows where
        }
        if (install is null)
        {
            Console.Error.WriteLine("OpenMagicka: no Magicka (pass --content <Magicka folder> or set MAGICKA_DIR)");
            return 2;
        }
        if (install.IsKnownVersion)
        {
            Console.WriteLine($"OpenMagicka: Magicka {install.Version} at {install.Folder}");
        }
        else
        {
            Console.Error.WriteLine($"OpenMagicka: Magicka {install.Version ?? "(unknown version)"} at {install.Folder}; OpenMagicka is made for {MagickaInstall.KnownVersion}");
        }
        if (!string.Equals(settings.MagickaFolder, install.Folder, StringComparison.OrdinalIgnoreCase) && options.Screenshot is null)
        {
            settings = settings with { MagickaFolder = install.Folder };        // remembered for the next start
            try
            {
                settings.Save(GameSettings.DefaultPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"OpenMagicka: the settings were not saved ({e.Message})");
            }
        }
        using var content = ShellContent.Open(install.Content, settings, sound: options.Screenshot is null);
        using var game = new GameShell(content, options.Screenshot);
        game.Run();
        return game.ExitCode;
    }

    private static int RunLevel(string content, string name, string? screenshot)
    {
        var files = new OpenMagicka.Content.ContentFileSystem(content);
        if (PlayGame.FindLevel(files, name) is not { } path)
        {
            Console.Error.WriteLine($"OpenMagicka: no level named \"{name}\"");
            return 2;
        }
        var loader = new OpenMagicka.Content.ContentLoader(files, OpenMagicka.Content.Xnb.XnbReaderRegistry.Magicka);
        var definition = OpenMagicka.Content.Levels.LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);
        var play = LevelPlay.Open(definition, files, loader);
        using var audio = screenshot is null ? GameFiles.OpenAudio(files) : null; // a screenshot is taken in silence
        if (audio is not null)
        {
            if (GameFiles.SoundNames(loader) is { } names)
            {
                play.Hear(new OpenMagicka.Engine.Audio.SoundSystem(audio, names));
            }
            else
            {
                audio.Dispose();                                              // no names, no sound: the device closes at once
            }
        }
        if (GameFiles.Controls() is { } controls)
        {
            play.Bind(controls);
        }
        using var game = new PlayGame(loader, play, Path.GetFileNameWithoutExtension(path), screenshot);
        game.Run();
        return game.ExitCode;
    }

    private static int RunViewer(string content, GameOptions options)
    {
        var (catalog, loader) = ViewerGame.Open(content);
        if (options.Scene is { } scene && catalog.Find(scene) < 0)
        {
            Console.Error.WriteLine($"OpenMagicka: no scene named \"{scene}\"");
            return 2;
        }
        using var viewer = new ViewerGame(catalog, loader, options.Scene, options.Screenshot, options.GameCamera);
        viewer.Run();
        return viewer.ExitCode;
    }
}
