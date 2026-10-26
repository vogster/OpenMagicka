using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMagicka.Engine.Hud;

namespace OpenMagicka.Game.Install;

/// <summary>
/// The player's settings (OM-53), kept as JSON in OpenMagicka's data folder (the game's own <c>Settings.sav</c> is left
/// alone): where Magicka is, the music's and the sounds' volumes (0…10, 7 by default, as the original's), the
/// language, the screen, and the HUD's options. A missing or unreadable file gives the defaults.
/// </summary>
public sealed record GameSettings
{
    public const int MaxVolume = 10, DefaultVolume = 7;

    public string? MagickaFolder { get; init; }

    public int MusicVolume { get; init; } = DefaultVolume;

    public int SoundVolume { get; init; } = DefaultVolume;

    /// <summary>The language's folder name under <c>Content/Languages</c> (eng, rus, deu…).</summary>
    public string Language { get; init; } = "eng";

    /// <summary>The window's or the screen's size; none: the desktop's.</summary>
    public int? Width { get; init; }

    public int? Height { get; init; }

    public bool Fullscreen { get; init; }

    public bool VSync { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<HealthBarOption>))]
    public HealthBarOption HealthBars { get; init; }

    public bool DamageNumbers { get; init; } = true;

    public bool SpellWheel { get; init; } = true;

    /// <summary>The interface's sizes offered, in per cent of the original's (OM-87).</summary>
    public static readonly int[] InterfaceSizes = [75, 100, 125, 150, 175, 200, 250, 300];

    /// <summary>The interface's size in per cent (one of <see cref="InterfaceSizes"/>); none: by the screen (see <see cref="InterfaceScale"/>).</summary>
    public int? InterfaceSize { get; init; }

    /// <summary>
    /// How much bigger the pieces drawn in screen pixels are (the HUD, the speech boxes, the hints, the message boxes):
    /// the size chosen, else the screen's height against 1080 in quarter steps (the original's size up to a 1080-line
    /// screen, twice it on a 4K one; whole quarters keep the bitmap text sharp). Never so big that the pieces' screen
    /// is under 480 lines, where the original's 640-wide cutscene box and the hints would not fit.
    /// </summary>
    public float InterfaceScale(int screenHeight)
    {
        var scale = InterfaceSize is { } size ? size / 100f : Math.Max(1, MathF.Floor(screenHeight / 1080f * 4) / 4);
        return Math.Min(scale, Math.Max(1, screenHeight / 480f));
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>OpenMagicka's settings file (the user's local application data).</summary>
    public static string DefaultPath => Engine.DataFolder.Of("settings.json");

    /// <summary>The settings at a path, clamped to what they allow; the defaults when there are none or they do not read.</summary>
    public static GameSettings Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return File.Exists(path) ? (JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), Json) ?? new GameSettings()).Clamped() : new GameSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new GameSettings();
        }
    }

    /// <summary>Written whole (a temporary file moved over the old one).</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var partial = path + ".part";
        File.WriteAllText(partial, JsonSerializer.Serialize(this, Json));
        File.Move(partial, path, overwrite: true);
    }

    public GameSettings Clamped() => this with
    {
        MusicVolume = Math.Clamp(MusicVolume, 0, MaxVolume),
        SoundVolume = Math.Clamp(SoundVolume, 0, MaxVolume),
        Language = string.IsNullOrWhiteSpace(Language) || Language.IndexOfAny([.. Path.GetInvalidFileNameChars(), '.', '/', '\\']) >= 0 ? "eng" : Language,
        Width = Width is > 0 and <= 16384 ? Width : null,
        Height = Height is > 0 and <= 16384 ? Height : null,
        HealthBars = Enum.IsDefined(HealthBars) ? HealthBars : HealthBarOption.On,
        InterfaceSize = InterfaceSize is { } size && InterfaceSizes.Contains(size) ? size : null,
    };
}
