namespace OpenMagicka.Content.Localization;

/// <summary>
/// The languages under <c>Languages/</c> and their string tables (docs/engine/ARCHITECTURE.md §10).
/// A language is a directory holding <c>*.loctable.xml</c> files; all of them are merged, in name
/// order ignoring case, a later file replacing an earlier one's strings (in the shipped content
/// no key has different texts in two files, so the order does not change any string).
/// </summary>
public static class LanguagePack
{
    public const string Fallback = "eng";

    private const string LanguagesDirectory = "Languages";
    private const string Suffix = ".loctable.xml";

    /// <summary>Names of the language directories that hold string tables, in name order.</summary>
    public static IReadOnlyList<string> Available(ContentFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.ListFiles(LanguagesDirectory, recursive: true)
            .Select(path => path.Split('/'))
            .Where(segments => segments.Length == 3 && IsTable(segments[2]))
            .Select(segments => segments[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The strings of a language, or of English when that language is not available.</summary>
    public static StringTable Load(ContentFileSystem files, string language)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(language);
        var available = Available(files);
        var directory = available.FirstOrDefault(l => l.Equals(language, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(l => l.Equals(Fallback, StringComparison.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException($"no string tables for {language} or {Fallback} under {LanguagesDirectory}/");

        var entries = new List<LocEntry>();
        foreach (var path in files.ListFiles(LanguagesDirectory + "/" + directory).Where(IsTable))
        {
            try
            {
                using var stream = new MemoryStream(files.ReadAllBytes(path));
                entries.AddRange(LocTableReader.Read(stream));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{path}: {e.Message}", e);
            }
        }
        return StringTable.Create(entries);
    }

    private static bool IsTable(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
}
