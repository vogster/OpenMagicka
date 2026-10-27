using OpenMagicka.Content;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Input;
using OpenMagicka.Game.Install;
using OpenMagicka.Game.Menu;

namespace OpenMagicka.Game.Shell;

/// <summary>
/// What the game runs on (OM-53): the install's content, its sound (none when there is no device), the player's
/// controls, settings and campaign saves, and the languages the content has.
/// </summary>
public sealed class ShellContent : IDisposable
{
    private ShellContent(ContentFileSystem files, GameSettings settings, string settingsPath, CampaignSaves saves)
    {
        Files = files;
        Loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        Settings = settings;
        SettingsPath = settingsPath;
        Saves = saves;
        Languages = LanguagePack.Available(files);
        Cutscenes = ReadCutscenes(files);
    }

    /// <summary>The world map's cutscenes (OM-84); none when the file is missing or does not read.</summary>
    private static IReadOnlyDictionary<int, Content.Levels.MapCutscene> ReadCutscenes(ContentFileSystem files)
    {
        try
        {
            return files.Exists(Content.Levels.MapCutsceneReader.Path)
                ? Content.Levels.MapCutsceneReader.Read(new MemoryStream(files.ReadAllBytes(Content.Levels.MapCutsceneReader.Path)))
                : new Dictionary<int, Content.Levels.MapCutscene>();
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"OpenMagicka: no map cutscenes ({e.Message})");
            return new Dictionary<int, Content.Levels.MapCutscene>();
        }
    }

    public ContentFileSystem Files { get; }

    public ContentLoader Loader { get; }

    public SoundSystem? Sounds { get; private set; }

    public ControlBindings? Controls { get; private set; }

    public GameSettings Settings { get; }

    public string SettingsPath { get; }

    public CampaignSaves Saves { get; }

    public IReadOnlyList<string> Languages { get; }

    public IReadOnlyDictionary<int, Content.Levels.MapCutscene> Cutscenes { get; }

    /// <summary>The game's content at a Content folder, with the player's files (by default where OpenMagicka keeps them).</summary>
    public static ShellContent Open(string content, GameSettings settings, string? settingsPath = null, string? savesPath = null, bool sound = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settings);
        var shell = new ShellContent(new ContentFileSystem(content), settings, settingsPath ?? GameSettings.DefaultPath,
            CampaignSaves.Load(savesPath ?? CampaignSaves.DefaultPath));
        shell.Controls = GameFiles.Controls();
        var language = shell.Languages.Contains(settings.Language, StringComparer.OrdinalIgnoreCase) ? settings.Language : LanguagePack.Fallback;
        if (sound && GameFiles.OpenAudio(shell.Files, language) is { } audio)
        {
            if (GameFiles.SoundNames(shell.Loader) is { } names)
            {
                shell.Sounds = new SoundSystem(audio, names);
                shell.Sounds.SetVolumes(settings.MusicVolume, settings.SoundVolume);
            }
            else
            {
                audio.Dispose();
            }
        }
        return shell;
    }

    public void Dispose() => Sounds?.Dispose();
}

/// <summary>The player's and the game's files the launcher and the shell both open (OM-51, OM-31).</summary>
public static class GameFiles
{
    /// <summary>The player's own controls (<c>controls.json</c> in the roaming data folder); none: the defaults.</summary>
    public static ControlBindings? Controls()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenMagicka", "controls.json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return ControlBindings.Load(File.ReadAllText(path));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: {path}: {e.Message}; the default controls are used");
            return null;
        }
    }

    /// <summary>The game's sound (OM-51) with the language's voices; none, with a word why, when there is no device or the content lacks it.</summary>
    public static XactAudio? OpenAudio(ContentFileSystem files, string language = LanguagePack.Fallback)
    {
        ArgumentNullException.ThrowIfNull(files);
        var transcoded = new TranscodedBanks(TranscodedBanks.DefaultDirectory, WmaDecoders.Find); // the WMA banks, made playable once (OM-61)
        var audio = XactAudio.Open(files, language, out var failure, transcoded);
        if (audio is null)
        {
            Console.Error.WriteLine($"OpenMagicka: no sound ({failure})");
        }
        return audio;
    }

    /// <summary>The cues' names; none, with a word why, when the content's list does not read (the game is then silent).</summary>
    public static SoundNames? SoundNames(ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        try
        {
            return Engine.Audio.SoundNames.Load(loader);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or FormatException)
        {
            Console.Error.WriteLine($"OpenMagicka: no sound ({Engine.Audio.SoundNames.Asset}: {e.Message})");
            return null;
        }
    }
}
