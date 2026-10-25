namespace OpenMagicka.Game.Install;

/// <summary>What the player answered a folder dialog: a folder, nothing (cancelled), or why there is no dialog.</summary>
public readonly record struct FolderAnswer(string? Folder, string? Error = null)
{
    public static FolderAnswer Cancelled => default;

    public static FolderAnswer Failed(string error) => new(null, error);
}

/// <summary>The system's boxes the launcher asks the player with (SDL's, in the launcher).</summary>
public interface IInstallQuestions
{
    /// <summary>A question with buttons; the button chosen (its index), or −1 when the box was closed.</summary>
    int Ask(string message, IReadOnlyList<string> buttons);

    FolderAnswer PickFolder(string? start);

    void Tell(string message);
}

/// <summary>
/// The player picks Magicka's folder (OM-53) when it is not where OpenMagicka looks (a copy outside Steam): a box says
/// so and offers a folder dialog, until a folder with the game's content is chosen or the player gives up. A system
/// without a folder dialog gets the word on starting with <c>--content</c> instead.
/// </summary>
public static class InstallChooser
{
    public const string NotFound = "OpenMagicka plays your own copy of Magicka (version 1.10.4.2), but did not find it in Steam's libraries.\n\n"
        + "Choose the folder Magicka is installed in (the one with Magicka.exe and the Content folder).";

    public const string Choose = "Choose the folder", Quit = "Quit";

    public const string NoDialog = "No folder dialog is available here ({0}).\n\nStart OpenMagicka with --content \"<Magicka folder>\", "
        + "or set MAGICKA_DIR to that folder.";

    public const string NotMagicka = "There is no Magicka in {0}: its folder has a Content folder with Levels and Audio in it.";

    public static MagickaInstall? Ask(IInstallQuestions questions, string? start = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var message = NotFound;
        while (questions.Ask(message, [Choose, Quit]) == 0)
        {
            var answer = questions.PickFolder(start);
            if (answer.Error is { } error)
            {
                questions.Tell(string.Format(System.Globalization.CultureInfo.InvariantCulture, NoDialog, error));
                return null;
            }
            if (answer.Folder is not { } folder)
            {
                continue;                                                     // cancelled: the question again
            }
            if (MagickaInstall.At(folder) is { } install)
            {
                return install;
            }
            start = folder;
            message = string.Format(System.Globalization.CultureInfo.InvariantCulture, NotMagicka, folder) + "\n\n" + NotFound;
        }
        return null;
    }
}
