using OpenMagicka.Content;
using OpenMagicka.Content.Localization;
using static OpenMagicka.Tests.Localization.LocTableReaderTests;

namespace OpenMagicka.Tests.Localization;

public sealed class LanguagePackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omloc-" + Guid.NewGuid().ToString("N"));

    public LanguagePackTests()
    {
        Write("Languages/eng/b.loctable.xml", Row("#shared", "eng b"), Row("#only_b", "B"));
        Write("Languages/eng/A.loctable.xml", Row("#shared", "eng a"), Row("#only_a", "A"));
        Write("Languages/eng/Font/x.loctable.xml", Row("#font", "not loaded"));
        Write("Languages/eng/notes.xml", Row("#notes", "not loaded"));
        Write("Languages/RUS/UI.loctable.xml", Row("#shared", "rus"));
        Directory.CreateDirectory(Path.Combine(root, "Languages", "empty"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void Write(string path, params string[] rows)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, Workbook(rows));
    }

    [Fact]
    public void Available_ListsLanguageDirectoriesWithStringTables()
    {
        Assert.Equal(["eng", "RUS"], LanguagePack.Available(new ContentFileSystem(root)));
    }

    [Fact]
    public void Load_ReadsTheLanguagesTablesInNameOrder_LaterFilesWinning()
    {
        var table = LanguagePack.Load(new ContentFileSystem(root), "eng");

        Assert.Equal(3, table.Count);
        Assert.Equal("eng b", table.Get("#shared"));
        Assert.Equal("A", table.Get("#only_a"));
        Assert.False(table.TryGetRaw(StringTable.Id("#font"), out _));
        Assert.False(table.TryGetRaw(StringTable.Id("#notes"), out _));
    }

    [Fact]
    public void Load_IgnoresTheCaseOfTheLanguageName()
    {
        Assert.Equal("rus", LanguagePack.Load(new ContentFileSystem(root), "rus").Get("#shared"));
    }

    [Fact]
    public void Load_OfAnUnavailableLanguage_FallsBackToEnglish()
    {
        var fs = new ContentFileSystem(root);

        Assert.Equal("eng b", LanguagePack.Load(fs, "jpn").Get("#shared"));
        Assert.Equal("eng b", LanguagePack.Load(fs, "empty").Get("#shared"));
        Assert.Equal("eng b", LanguagePack.Load(fs, "../eng").Get("#shared"));
    }

    [Fact]
    public void Load_OfABrokenTable_NamesTheFile()
    {
        File.WriteAllText(Path.Combine(root, "Languages", "RUS", "UI.loctable.xml"), "<Workbook>");

        var error = Assert.Throws<InvalidDataException>(() => LanguagePack.Load(new ContentFileSystem(root), "rus"));

        Assert.Contains("UI.loctable.xml", error.Message);
    }
}
