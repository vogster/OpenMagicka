using OpenMagicka.Game;
using OpenMagicka.Game.Play;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
        // With the game's content the level viewer runs (the game itself comes with M3);
        // --check-effects and running without content use the placeholder game.
        if (options is { ContentDirectory: { } levelContent, Level: { } level })
        {
            return RunLevel(levelContent, level, options.Screenshot);
        }
        if (options.ContentDirectory is { } content && !options.CheckEffects)
        {
            return RunViewer(content, options);
        }
        using var game = new OpenMagickaGame(options);
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
        using var audio = screenshot is null ? OpenAudio(files) : null;      // a screenshot is taken in silence
        if (audio is not null)
        {
            if (SoundNames(loader) is { } names)
            {
                play.Hear(new OpenMagicka.Engine.Audio.SoundSystem(audio, names));
            }
            else
            {
                audio.Dispose();                                              // no names, no sound: the device closes at once
            }
        }
        var controls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenMagicka", "controls.json");
        if (File.Exists(controls))
        {
            try
            {
                play.Bind(OpenMagicka.Engine.Input.ControlBindings.Load(File.ReadAllText(controls)));
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"OpenMagicka: {controls}: {e.Message}; the default controls are used");
            }
        }
        using var game = new PlayGame(loader, play, Path.GetFileNameWithoutExtension(path), screenshot);
        game.Run();
        return game.ExitCode;
    }

    /// <summary>The game's sound (OM-51); none, with a word why, when there is no device or the content lacks it.</summary>
    private static OpenMagicka.Engine.Audio.XactAudio? OpenAudio(OpenMagicka.Content.ContentFileSystem files)
    {
        var audio = OpenMagicka.Engine.Audio.XactAudio.Open(files, OpenMagicka.Content.Localization.LanguagePack.Fallback, out var failure);
        if (audio is null)
        {
            Console.Error.WriteLine($"OpenMagicka: no sound ({failure})");
        }
        return audio;
    }

    /// <summary>The cues' names; none, with a word why, when the content's list does not read (the game is then silent).</summary>
    private static OpenMagicka.Engine.Audio.SoundNames? SoundNames(OpenMagicka.Content.ContentLoader loader)
    {
        try
        {
            return OpenMagicka.Engine.Audio.SoundNames.Load(loader);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or FormatException)
        {
            Console.Error.WriteLine($"OpenMagicka: no sound ({OpenMagicka.Engine.Audio.SoundNames.Asset}: {e.Message})");
            return null;
        }
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
