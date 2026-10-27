namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// The campaigns' chapters, in order (docs/formats/LEVELS_TRIGGERS.md §3): written into the original's
/// code, not into any file. The paths are the levels' under <c>Levels/</c>. Each chapter after the first
/// is reached from the world map, with the cutscene <c>Levels/Cutscenes.xml</c> gives it (OM-84).
/// </summary>
public static class Campaign
{
    public static IReadOnlyList<string> Chapters { get; } =
    [
        "Levels/Wizard_Castle.lvl",
        "Levels/Mountaindale.lvl",
        "Levels/Highlands.lvl",
        "Levels/Havindr.lvl",
        "Levels/Battlefield.lvl",
        "Levels/EndOfWorld.lvl",
        "Levels/Wizard_Castle2.lvl",
        "Levels/Mines.lvl",
        "Levels/Swamp.lvl",
        "Levels/Niflheim.lvl",
        "Levels/Ruins.lvl",
        "Levels/EndOfWorld2.lvl",
    ];

    /// <summary>The Mythos campaign (the DLC's).</summary>
    public static IReadOnlyList<string> Mythos { get; } =
    [
        "Levels/Tsar/Tsar_WizardCastle.lvl",
        "Levels/Tsar/Tsar_Mountaindale.lvl",
        "Levels/Tsar/Tsar_Rlyeh.lvl",
    ];

    /// <summary>The Dungeons and Daemons story challenges, in order.</summary>
    public static IReadOnlyList<string> Dungeons { get; } =
    [
        "Levels/Challenges/ch_dungeons_ch1.lvl",
        "Levels/Challenges/ch_dungeons_ch2.lvl",
    ];

    /// <summary>The chapter after one of a campaign (any case); null after the last, or for a level of none.</summary>
    public static string? Next(string chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        foreach (var list in new[] { Chapters, Mythos, Dungeons })
        {
            var index = IndexOf(list, chapter);
            if (index >= 0)
            {
                return index + 1 < list.Count ? list[index + 1] : null;
            }
        }
        return null;
    }

    private static int IndexOf(IReadOnlyList<string> list, string chapter)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], chapter, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
