using System.Diagnostics;

namespace OpenMagicka.Game.Install;

/// <summary>
/// The player's Magicka (OM-4): its folder, whether it has the game's content, and the version of its
/// <c>Magicka.exe</c>, which OpenMagicka is made against (1.10.4.2). Found where the player said (the command line,
/// <c>MAGICKA_DIR</c>, the settings), else in Steam's libraries.
/// </summary>
public sealed record MagickaInstall(string Folder, string? Version)
{
    public const int SteamAppId = 42910;
    public const string KnownVersion = "1.10.4.2";

    public string Content => Path.Combine(Folder, "Content");

    /// <summary>Whether this is the version OpenMagicka is made against (another may still play, with a warning).</summary>
    public bool IsKnownVersion => Version == KnownVersion;

    /// <summary>The install at a folder (Magicka's, or its Content folder); none without the game's content there.</summary>
    public static MagickaInstall? At(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }
        string full;
        try
        {
            full = Path.GetFullPath(folder.Trim().Trim('"'));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Equals("Content", StringComparison.OrdinalIgnoreCase))
        {
            full = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? full;
        }
        var content = Path.Combine(full, "Content");
        if (!Directory.Exists(Path.Combine(content, "Levels")) || !Directory.Exists(Path.Combine(content, "Audio")))
        {
            return null;
        }
        var exe = Path.Combine(full, "Magicka.exe");
        return new MagickaInstall(full, File.Exists(exe) ? VersionOf(exe) : null);
    }

    /// <summary>The exe's file version (Windows reads it from its resources), else its assembly's (elsewhere: it is a .NET assembly); none.</summary>
    private static string? VersionOf(string exe)
    {
        if (FileVersionInfo.GetVersionInfo(exe).FileVersion is { Length: > 0 } version)
        {
            return version;
        }
        try
        {
            return System.Reflection.AssemblyName.GetAssemblyName(exe).Version?.ToString();
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The first install among the folders given (in order), then Steam's libraries; none.</summary>
    public static MagickaInstall? Find(IEnumerable<string?> given, IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(steamRoots);
        return given.Select(At).FirstOrDefault(i => i is not null)
            ?? steamRoots.SelectMany(SteamLibraries).Select(library => At(Path.Combine(library, "steamapps", "common", "Magicka"))).FirstOrDefault(i => i is not null);
    }

    /// <summary>A Steam root's libraries (itself first), from its <c>steamapps/libraryfolders.vdf</c>.</summary>
    public static IEnumerable<string> SteamLibraries(string root)
    {
        var listed = new List<string> { root };
        var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file))
        {
            try
            {
                listed.AddRange(Vdf.Values(File.ReadAllText(file), "path"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // an unreadable list: the root alone
            }
        }
        return listed.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Where Steam usually lives on this system.</summary>
    public static IEnumerable<string> SteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string registered)
            {
                yield return registered.Replace('/', Path.DirectorySeparatorChar);
            }
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
        else
        {
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }
}

/// <summary>Valve's KeyValues text, as far as finding values by key: quoted strings (with \\ and \" escapes) and braces.</summary>
public static class Vdf
{
    /// <summary>Every value of a key, anywhere in the text, in order.</summary>
    public static IEnumerable<string> Values(string text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = Tokens(text).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] is { Quoted: true } name && name.Text.Equals(key, StringComparison.OrdinalIgnoreCase) && tokens[i + 1].Quoted)
            {
                yield return tokens[i + 1].Text;
            }
        }
    }

    private static IEnumerable<(string Text, bool Quoted)> Tokens(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '{' or '}')
            {
                yield return (c.ToString(), false);
            }
            else if (c == '"')
            {
                var value = new System.Text.StringBuilder();
                for (i++; i < text.Length && text[i] != '"'; i++)
                {
                    value.Append(text[i] == '\\' && i + 1 < text.Length ? text[++i] : text[i]);
                }
                yield return (value.ToString(), true);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
        }
    }
}
