using OpenMagicka.Game;

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
        using var game = new OpenMagickaGame(options);
        game.Run();
        return game.ExitCode;
    }
}
