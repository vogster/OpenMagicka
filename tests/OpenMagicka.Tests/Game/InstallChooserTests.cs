using OpenMagicka.Game.Install;

namespace OpenMagicka.Tests.Game;

/// <summary>The player shows where Magicka is when OpenMagicka does not find it (OM-53).</summary>
public sealed class InstallChooserTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("om53");

    private sealed class Player(params object[] answers) : IInstallQuestions
    {
        private readonly Queue<object> answers = new(answers);

        public List<string> Asked { get; } = [];

        public List<string?> Starts { get; } = [];

        public List<string> Told { get; } = [];

        public int Ask(string message, IReadOnlyList<string> buttons)
        {
            Asked.Add(message);
            return (int)answers.Dequeue();
        }

        public FolderAnswer PickFolder(string? start)
        {
            Starts.Add(start);
            return (FolderAnswer)answers.Dequeue();
        }

        public void Tell(string message) => Told.Add(message);
    }

    private string Magicka()
    {
        var folder = Path.Combine(root.FullName, "Games", "Magicka");
        Directory.CreateDirectory(Path.Combine(folder, "Content", "Levels"));
        Directory.CreateDirectory(Path.Combine(folder, "Content", "Audio"));
        return folder;
    }

    [Fact]
    public void AFolderWithTheGame_IsTaken_AfterAWrongOneAndACancel()
    {
        var magicka = Magicka();
        var player = new Player(0, FolderAnswer.Cancelled, 0, new FolderAnswer(root.FullName), 0, new FolderAnswer(magicka));

        var install = InstallChooser.Ask(player);

        Assert.Equal(Path.GetFullPath(magicka), install?.Folder);
        Assert.Equal(3, player.Asked.Count);
        Assert.Contains(root.FullName, player.Asked[2]);                     // the wrong folder is named
        Assert.Equal(root.FullName, player.Starts[2]);                        // and the dialog starts there
    }

    [Fact]
    public void Quitting_GivesNone()
    {
        Assert.Null(InstallChooser.Ask(new Player(1)));
        Assert.Null(InstallChooser.Ask(new Player(-1)));                      // the box closed
    }

    [Fact]
    public void NoFolderDialog_SaysHowToStartWithTheFolder()
    {
        var player = new Player(0, FolderAnswer.Failed("no portal"));

        Assert.Null(InstallChooser.Ask(player));
        Assert.Contains("--content", Assert.Single(player.Told));
        Assert.Contains("no portal", player.Told[0]);
    }

    public void Dispose() => root.Delete(recursive: true);
}
