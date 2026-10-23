using OpenMagicka.Game;
using OpenMagicka.Game.Viewer;

namespace OpenMagicka.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
        if (options.ContentDirectory is { } content && !options.CheckEffects)
        {
            return RunViewer(content, options);
        }
        using var game = new OpenMagickaGame(options);
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
        using var viewer = new ViewerGame(catalog, loader, options.Scene, options.Screenshot);
        viewer.Run();
        return viewer.ExitCode;
    }
}
