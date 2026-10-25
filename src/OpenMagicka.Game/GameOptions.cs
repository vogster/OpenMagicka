namespace OpenMagicka.Game;

/// <summary>
/// Command line: <c>--content &lt;Magicka folder or its Content folder&gt;</c> (else the
/// MAGICKA_DIR environment variable), <c>--check-effects</c> (load and apply every game effect
/// on the GPU, print the result and exit), <c>--scene &lt;name&gt;</c> (the viewer's first
/// scene), <c>--screenshot &lt;file.png&gt;</c> (save the viewer's first full frame and exit),
/// <c>--game-camera</c> (the viewer starts with the game's camera), <c>--level &lt;name&gt;</c> (play that level
/// alone; its file name or path; a screenshot is then the game's), <c>--viewer</c> (the level viewer, which a scene
/// or the game camera also asks for), <c>--placeholder</c> (a window that needs none of the game's files: a check of
/// the graphics). Otherwise the game runs, its menu first (a screenshot is then the menu's); without a content folder
/// it looks for Magicka itself (OM-4, OM-53).
/// </summary>
public sealed record GameOptions(string? ContentDirectory, bool CheckEffects, string? Scene = null, string? Screenshot = null, bool GameCamera = false,
    string? Level = null, bool Viewer = false, bool Placeholder = false)
{
    /// <summary>The level viewer is asked for (by itself, a scene or the game camera), not the game.</summary>
    public bool WantsViewer => Viewer || Scene is not null || GameCamera;

    public const string ContentVariable = "MAGICKA_DIR";

    private const string Usage = "usage: OpenMagicka [--content <Magicka folder>] [--check-effects] [--scene <name>] [--screenshot <file.png>] [--game-camera] [--level <name>] [--viewer] [--placeholder]";

    public static GameOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        string? content = null;
        string? scene = null;
        string? screenshot = null;
        string? level = null;
        var checkEffects = false;
        var gameCamera = false;
        var viewer = false;
        var placeholder = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--content" when i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal): content = args[++i]; break;
                case "--content": throw new ArgumentException("--content needs a folder; " + Usage);
                case "--scene" when HasValue(args, i): scene = args[++i]; break;
                case "--screenshot" when HasValue(args, i): screenshot = Path.GetFullPath(args[++i]); break;
                case "--level" when HasValue(args, i): level = args[++i]; break;
                case "--scene" or "--screenshot" or "--level": throw new ArgumentException($"{args[i]} needs a value; " + Usage);
                case "--check-effects": checkEffects = true; break;
                case "--game-camera": gameCamera = true; break;
                case "--viewer": viewer = true; break;
                case "--placeholder": placeholder = true; break;
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
        if (level is not null && (checkEffects || directory is null))
        {
            throw new ArgumentException($"--level needs the game's content (pass --content or set {ContentVariable}) and is not for --check-effects; " + Usage);
        }
        if (screenshot is not null && checkEffects)
        {
            throw new ArgumentException("--screenshot is for the viewer, not with --check-effects; " + Usage);
        }
        if (screenshot is not null && directory is null && (viewer || scene is not null || gameCamera))
        {
            throw new DirectoryNotFoundException($"the viewer's --screenshot needs the game's content: pass --content or set {ContentVariable}");
        }
        if (viewer && (directory is null || level is not null))
        {
            throw new ArgumentException($"--viewer needs the game's content (pass --content or set {ContentVariable}) and is not for --level; " + Usage);
        }
        return new GameOptions(directory, checkEffects, scene, screenshot, gameCamera, level, viewer, placeholder);
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
