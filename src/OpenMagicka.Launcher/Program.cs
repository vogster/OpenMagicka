using OpenMagicka.Game;

namespace OpenMagicka.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new OpenMagickaGame();
        game.Run();
    }
}
