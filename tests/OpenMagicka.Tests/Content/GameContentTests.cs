using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Tests.Content;

/// <summary>Location of a Magicka installation for tests that need the real game files.</summary>
internal static class GameContent
{
    public const string Variable = "MAGICKA_DIR";

    public static string? ContentDirectory
    {
        get
        {
            var root = Environment.GetEnvironmentVariable(Variable);
            var content = root is null ? null : Path.Combine(root, "Content");
            return content is not null && Directory.Exists(content) ? content : null;
        }
    }
}

/// <summary>Runs only when MAGICKA_DIR points at a Magicka installation; skipped otherwise.</summary>
public sealed class GameContentFactAttribute : FactAttribute
{
    public GameContentFactAttribute()
    {
        if (GameContent.ContentDirectory is null)
        {
            Skip = $"Set {GameContent.Variable} to a Magicka installation to run tests on the game files.";
        }
    }
}

public class GameContentTests
{
    [GameContentFact]
    public void EveryXnbFile_DecompressesAndHasAReaderTable()
    {
        var files = Directory.GetFiles(GameContent.ContentDirectory!, "*.xnb", SearchOption.AllDirectories);
        var failures = new List<string>();

        foreach (var path in files)
        {
            try
            {
                var xnb = XnbFile.Read(File.ReadAllBytes(path));
                if (xnb.TypeReaders.Count == 0 || xnb.Objects.Length == 0)
                {
                    failures.Add($"{path}: empty");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(files.Length > 4000, $"only {files.Length} .xnb files found");
        Assert.Empty(failures);
    }
}
