using System.Globalization;
using System.Text.Json;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The campaign's chapters as the menu shows them: each a level of the campaign (<see cref="Engine.Scripting.Campaign"/>),
/// named "#chapter_NN" and titled "#chaptertitle_NN" (docs/engine/ARCHITECTURE.md §20).
/// </summary>
public static class Chapters
{
    public static IReadOnlyList<string> Levels => Engine.Scripting.Campaign.Chapters;

    public static int Count => Levels.Count;

    public static string NameKey(int chapter) => "#chapter_" + (chapter + 1).ToString("D2", CultureInfo.InvariantCulture);

    public static string TitleKey(int chapter) => "#chaptertitle_" + (chapter + 1).ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>The time a slot has been played, as the original writes it (hh:mm:ss, at most 99:59:59).</summary>
    public static string Time(double seconds)
    {
        var time = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 100 * 3600 - 1) : 0);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}");
    }
}

/// <summary>A campaign in a save slot: the chapter it is at, the furthest reached, whether it was finished once, and the time played.</summary>
public sealed record CampaignSlot(int Chapter, int Reached, bool Looped = false, double Seconds = 0)
{
    /// <summary>A slot at a chapter: the furthest reached rises with it.</summary>
    public CampaignSlot At(int chapter) => this with { Chapter = chapter, Reached = Math.Max(Reached, chapter) };

    /// <summary>The chapter played through: the next one, or the first again (looped) after the last.</summary>
    public CampaignSlot Completed() => Chapter + 1 >= Chapters.Count ? this with { Chapter = 0, Looped = true } : At(Chapter + 1);
}

/// <summary>
/// The three campaign save slots (OM-53), kept as JSON in OpenMagicka's data folder (the original's
/// <c>campaign.sav</c> is left alone). A missing or unreadable file gives three empty slots.
/// </summary>
public sealed class CampaignSaves
{
    public const int Slots = 3;

    private readonly string path;
    private readonly CampaignSlot?[] slots;

    private CampaignSaves(string path, CampaignSlot?[] slots) => (this.path, this.slots) = (path, slots);

    public static string DefaultPath => Engine.DataFolder.Of("campaign.json");

    public CampaignSlot? this[int slot] => slots[slot];

    public static CampaignSaves Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var slots = new CampaignSlot?[Slots];
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<CampaignSlot?[]>(File.ReadAllText(path)) is { } read)
            {
                for (var i = 0; i < Math.Min(Slots, read.Length); i++)
                {
                    slots[i] = read[i] is { } slot ? Valid(slot) : null;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // unreadable: empty slots (the file is written anew on the next change)
        }
        return new CampaignSaves(path, slots);
    }

    private static CampaignSlot Valid(CampaignSlot slot)
    {
        var chapter = Math.Clamp(slot.Chapter, 0, Chapters.Count - 1);
        return slot with { Chapter = chapter, Reached = Math.Clamp(Math.Max(slot.Reached, chapter), 0, Chapters.Count - 1),
            Seconds = double.IsFinite(slot.Seconds) && slot.Seconds > 0 ? slot.Seconds : 0 };
    }

    /// <summary>A slot set (or emptied) and the slots written; a write that fails is said, and the slots stay as set.</summary>
    public void Set(int slot, CampaignSlot? value)
    {
        slots[slot] = value is null ? null : Valid(value);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var partial = path + ".part";
            File.WriteAllText(partial, JsonSerializer.Serialize(slots));
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: the campaign was not saved ({e.Message})");
        }
    }
}
