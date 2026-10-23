namespace OpenMagicka.Game;

/// <summary>
/// Command line: <c>--content &lt;Magicka folder or its Content folder&gt;</c> (else the
/// MAGICKA_DIR environment variable), <c>--check-effects</c> (load and apply every game effect
/// on the GPU, print the result and exit), <c>--scene &lt;name&gt;</c> (the viewer's first
/// scene), <c>--screenshot &lt;file.png&gt;</c> (save the viewer's first full frame and exit).
/// Finding the Steam installation by itself is OM-4.
/// </summary>
public sealed record GameOptions(string? ContentDirectory, bool CheckEffects, string? Scene = null, string? Screenshot = null)
{
    public const string ContentVariable = "MAGICKA_DIR";

    private const string Usage = "usage: OpenMagicka [--content <Magicka folder>] [--check-effects] [--scene <name>] [--screenshot <file.png>]";

    public static GameOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        string? content = null;
        string? scene = null;
        string? screenshot = null;
        var checkEffects = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--content" when i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal): content = args[++i]; break;
                case "--content": throw new ArgumentException("--content needs a folder; " + Usage);
                case "--scene" when HasValue(args, i): scene = args[++i]; break;
                case "--screenshot" when HasValue(args, i): screenshot = Path.GetFullPath(args[++i]); break;
                case "--scene" or "--screenshot": throw new ArgumentException($"{args[i]} needs a value; " + Usage);
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
        if (screenshot is not null && checkEffects)
        {
            throw new ArgumentException("--screenshot is for the viewer, not with --check-effects; " + Usage);
        }
        if (screenshot is not null && directory is null)
        {
            throw new DirectoryNotFoundException($"--screenshot needs the game's content: pass --content or set {ContentVariable}");
        }
        return new GameOptions(directory, checkEffects, scene, screenshot);
    }

    private static bool HasValue(IReadOnlyList<string> args, int i) =>
        i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

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
