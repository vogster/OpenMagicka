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
        var loaded = LoadAll(XnbReaderRegistry.Standard, allowedMissing: name => !name.StartsWith("Microsoft.Xna", StringComparison.Ordinal));

        Assert.Equal(2296, loaded.GetValueOrDefault("Texture2DReader"));
        Assert.Equal(52, loaded.GetValueOrDefault("EffectReader"));
        Assert.Equal(13, loaded.GetValueOrDefault("TextureCubeReader"));
        Assert.Equal(7, loaded.GetValueOrDefault("Texture3DReader"));
        Assert.Equal(1, loaded.GetValueOrDefault("ListReader<String>"));
    }

    [GameContentFact]
    public void MagickaContent_LoadsCompletely()
    {
        // Every reader the game uses is implemented: nothing may be missing.
        var loaded = LoadAll(XnbReaderRegistry.Magicka, allowedMissing: _ => false);

        Assert.Equal(553, loaded.GetValueOrDefault("ModelReader"));
        Assert.Equal(327, loaded.GetValueOrDefault("SkinnedModelReader"));
        Assert.Equal(66, loaded.GetValueOrDefault("BitmapFontReader"));
        Assert.Equal(2296, loaded.GetValueOrDefault("Texture2DReader"));
        Assert.Equal(132, loaded.GetValueOrDefault("LevelModelReader"));
        Assert.Equal(454, loaded.GetValueOrDefault("ItemReader"));
        Assert.Equal(338, loaded.GetValueOrDefault("CharacterTemplateReader"));
        Assert.Equal(69, loaded.GetValueOrDefault("PhysicsEntityTemplateReader"));
        Assert.Equal(4308, loaded.Values.Sum());
    }

    /// <summary>Loads every file; a missing reader is only allowed when its name passes the filter.</summary>
    private Dictionary<string, int> LoadAll(XnbReaderRegistry registry, Func<string, bool> allowedMissing)
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
                XnbContent.Load(xnb, registry);
                loaded[rootKey] = loaded.GetValueOrDefault(rootKey) + 1;
            }
            catch (XnbUnsupportedReaderException e)
            {
                if (xnb.TypeReaders.Any(t => t.Key == e.ReaderKey && !allowedMissing(t.Name)))
                {
                    failures.Add($"{path}: missing reader {e.ReaderKey}");
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
        return loaded;
    }
}
