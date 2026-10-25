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
        using var game = new PlayGame(loader, LevelPlay.Open(definition, files, loader), Path.GetFileNameWithoutExtension(path), screenshot);
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
