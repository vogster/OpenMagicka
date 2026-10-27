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
/// <c>campaign.sav</c> is left alone). A missing or unreadable file gives three empty slots. Each slot's last
/// checkpoint (OM-85) is a file of its own beside it, gone with the slot.
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

    /// <summary>A slot set (or emptied, its checkpoint too) and the slots written; a write that fails is said, and the slots stay as set.</summary>
    public void Set(int slot, CampaignSlot? value)
    {
        slots[slot] = value is null ? null : Valid(value);
        if (value is null)
        {
            SetCheckpoint(slot, null);
        }
        Write(path, JsonSerializer.Serialize(slots), "the campaign");
    }

    /// <summary>Where a slot's checkpoint is kept.</summary>
    public string CheckpointPath(int slot) => Path.ChangeExtension(path, null) + ".slot" + (slot + 1).ToString(CultureInfo.InvariantCulture) + ".checkpoint.json";

    /// <summary>The slot's last checkpoint; none when there is none, or it does not read.</summary>
    public Engine.Scripting.SavedCheckpoint? Checkpoint(int slot)
    {
        try
        {
            var file = CheckpointPath(slot);
            return File.Exists(file) ? Engine.Scripting.SavedCheckpoint.FromJson(File.ReadAllText(file)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The slot has a checkpoint file (one that may not read, or be of other files: it is still the one a chapter change asks about, as the original's).</summary>
    public bool HasCheckpoint(int slot) => File.Exists(CheckpointPath(slot));

    /// <summary>The slot's checkpoint written (or deleted, with none); a write that fails is said.</summary>
    public void SetCheckpoint(int slot, Engine.Scripting.SavedCheckpoint? checkpoint)
    {
        var file = CheckpointPath(slot);
        if (checkpoint is not null)
        {
            Write(file, checkpoint.ToJson(), "the checkpoint");
            return;
        }
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: the checkpoint was not deleted ({e.Message})");
        }
    }

    private static void Write(string file, string text, string what)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var partial = file + ".part";
            File.WriteAllText(partial, text);
            File.Move(partial, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: {what} was not saved ({e.Message})");
        }
    }
}
