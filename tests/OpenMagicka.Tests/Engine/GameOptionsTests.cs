using OpenMagicka.Game;

namespace OpenMagicka.Tests.Engine;

public sealed class GameOptionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omopt-" + Guid.NewGuid().ToString("N"));

    public GameOptionsTests() => Directory.CreateDirectory(Path.Combine(root, "Content"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string? NoEnvironment(string name) => null;

    [Fact]
    public void Content_ComesFromTheArgument_OrMagickaDir_AsTheContentFolder()
    {
        Assert.Equal(Path.Combine(root, "Content"), GameOptions.Parse(["--content", root], NoEnvironment).ContentDirectory);
        Assert.Equal(Path.Combine(root, "Content"), GameOptions.Parse(["--content", Path.Combine(root, "Content")], NoEnvironment).ContentDirectory);
        Assert.Equal(Path.Combine(root, "Content"), GameOptions.Parse([], n => n == "MAGICKA_DIR" ? root : null).ContentDirectory);
        Assert.Null(GameOptions.Parse([], NoEnvironment).ContentDirectory);
    }

    [Fact]
    public void CheckEffects_IsAFlag()
    {
        Assert.True(GameOptions.Parse(["--check-effects", "--content", root], NoEnvironment).CheckEffects);
        Assert.False(GameOptions.Parse([], NoEnvironment).CheckEffects);
    }

    [Theory]
    [InlineData("--fullscreen")]
    [InlineData("--content")]
    [InlineData("--content", "--check-effects")]
    public void BadArguments_AreRejected(params string[] arguments)
    {
        var error = Assert.Throws<ArgumentException>(() => GameOptions.Parse(arguments, NoEnvironment));

        Assert.Contains(arguments[0] == "--content" ? "needs a folder" : "unknown argument", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneAndScreenshot_AreRead()
    {
        var options = GameOptions.Parse(["--content", root, "--scene", "sw_s1", "--screenshot", "shot.png"], NoEnvironment);

        Assert.Equal(("sw_s1", Path.GetFullPath("shot.png")), (options.Scene, options.Screenshot));
        Assert.False(options.GameCamera);
        Assert.True(GameOptions.Parse(["--content", root, "--game-camera"], NoEnvironment).GameCamera);
        Assert.Throws<ArgumentException>(() => GameOptions.Parse(["--scene"], NoEnvironment));
        Assert.Throws<DirectoryNotFoundException>(() => GameOptions.Parse(["--screenshot", "x.png"], NoEnvironment));
        Assert.Throws<ArgumentException>(() => GameOptions.Parse(["--content", root, "--check-effects", "--screenshot", "x.png"], NoEnvironment));
    }

    [Fact]
    public void AnEmptyMagickaDir_IsUnset()
    {
        Assert.Null(GameOptions.Parse([], n => n == "MAGICKA_DIR" ? " " : null).ContentDirectory);
    }

    [Theory]
    [InlineData(5, 0, 0, 0)]
    [InlineData(5, 1, 0, 0)]      // a known-broken effect is skipped, not a failure
    [InlineData(5, 0, 1, 1)]
    [InlineData(0, 0, 0, 1)]      // nothing was checked
    public void TheEffectCheck_PassesOnlyWhenEffectsRanAndNoneFailed(int ready, int skipped, int failed, int exitCode)
    {
        var report = new EffectWarmupReport(Enumerable.Repeat("e", ready).ToArray(), Enumerable.Repeat("s", skipped).ToArray(),
            Enumerable.Repeat("f", failed).ToArray());

        Assert.Equal(exitCode, report.ExitCode);
    }

    [Fact]
    public void AContentFolderThatDoesNotExist_IsRejected()
    {
        Assert.Throws<DirectoryNotFoundException>(() => GameOptions.Parse(["--content", Path.Combine(root, "nope")], NoEnvironment));
        Assert.Throws<DirectoryNotFoundException>(() => GameOptions.Parse(["--check-effects"], NoEnvironment));   // checking needs content
    }
}
