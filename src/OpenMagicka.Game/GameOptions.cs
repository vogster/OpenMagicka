namespace OpenMagicka.Game;

/// <summary>
/// Command line: <c>--content &lt;Magicka folder or its Content folder&gt;</c> (else the
/// MAGICKA_DIR environment variable), <c>--check-effects</c> (load and apply every game effect
/// on the GPU, print the result and exit). Finding the Steam installation by itself is OM-4.
/// </summary>
public sealed record GameOptions(string? ContentDirectory, bool CheckEffects)
{
    public const string ContentVariable = "MAGICKA_DIR";

    private const string Usage = "usage: OpenMagicka [--content <Magicka folder>] [--check-effects]";

    public static GameOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        string? content = null;
        var checkEffects = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--content" when i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal): content = args[++i]; break;
                case "--content": throw new ArgumentException("--content needs a folder; " + Usage);
                case "--check-effects": checkEffects = true; break;
                default: throw new ArgumentException($"unknown argument \"{args[i]}\"; " + Usage);
            }
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            content = environment(ContentVariable);
            content = string.IsNullOrWhiteSpace(content) ? null : content;
        }
        var directory = content is null ? null : ContentFolder(content);
        if (checkEffects && directory is null)
        {
            throw new DirectoryNotFoundException($"--check-effects needs the game's content: pass --content or set {ContentVariable}");
        }
        return new GameOptions(directory, checkEffects);
    }

    private static string ContentFolder(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            throw new ArgumentException($"invalid content folder \"{path}\": {e.Message}", e);
        }
        var inner = Path.Combine(full, "Content");
        return Directory.Exists(inner) ? inner
            : Directory.Exists(full) ? full
            : throw new DirectoryNotFoundException($"no Magicka content at {full}");
    }
}
