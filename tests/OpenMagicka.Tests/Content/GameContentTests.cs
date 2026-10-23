using OpenMagicka.Content.Xnb;
using Xunit.Abstractions;

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

public class GameContentTests(ITestOutputHelper output)
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

    [GameContentFact]
    public void StandardXnaContent_LoadsCompletely()
    {
        var files = Directory.GetFiles(GameContent.ContentDirectory!, "*.xnb", SearchOption.AllDirectories);
        var loaded = new Dictionary<string, int>();
        var unsupported = new Dictionary<string, int>();
        var failures = new List<string>();

        foreach (var path in files)
        {
            var xnb = XnbFile.Read(File.ReadAllBytes(path));
            var rootKey = xnb.TypeReaders[0].Key;
            try
            {
                XnbContent.Load(xnb, XnbReaderRegistry.Standard);
                loaded[rootKey] = loaded.GetValueOrDefault(rootKey) + 1;
            }
            catch (XnbUnsupportedReaderException e)
            {
                // Game-specific readers come later; an XNA reader must never be missing.
                if (xnb.TypeReaders.Any(t => t.Key == e.ReaderKey && t.Name.StartsWith("Microsoft.Xna", StringComparison.Ordinal)))
                {
                    failures.Add($"{path}: missing XNA reader {e.ReaderKey}");
                }
                unsupported[e.ReaderKey] = unsupported.GetValueOrDefault(e.ReaderKey) + 1;
            }
            catch (Exception e)
            {
                failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
            }
        }

        foreach (var (key, count) in loaded.OrderByDescending(p => p.Value))
        {
            output.WriteLine($"loaded {count,5} {key}");
        }
        foreach (var (key, count) in unsupported.OrderByDescending(p => p.Value))
        {
            output.WriteLine($"later  {count,5} {key}");
        }
        Assert.Empty(failures);
        Assert.Equal(2296, loaded.GetValueOrDefault("Texture2DReader"));
        Assert.Equal(52, loaded.GetValueOrDefault("EffectReader"));
        Assert.Equal(13, loaded.GetValueOrDefault("TextureCubeReader"));
        Assert.Equal(7, loaded.GetValueOrDefault("Texture3DReader"));
        Assert.Equal(1, loaded.GetValueOrDefault("ListReader<String>"));
    }
}
