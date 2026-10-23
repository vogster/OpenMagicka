using System.Text.Json;
using OpenMagicka.Tests.Content;
using OpenMagicka.Tests.Levels;
using OpenMagicka.Tools;

namespace OpenMagicka.Tests.Tools;

public sealed class OmxTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omx-" + Guid.NewGuid().ToString("N"));

    public OmxTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Content", "Levels", "Swamp"));
        File.WriteAllBytes(Path.Combine(root, "Content", "hello.xnb"), new XnbTestWriter().StringObject("hi").BuildBytes());
        File.WriteAllText(Path.Combine(root, "Content", "Levels", "Swamp", "swamp_s1.xml"), SceneReaderTests.Scene("<Indoor>true</Indoor>"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Content(params string[] parts) => Path.Combine([root, "Content", .. parts]);

    private static (int Code, string Out, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Omx.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void NoOrUnknownCommand_PrintsUsage()
    {
        Assert.Equal(2, Run().Code);
        var (code, _, error) = Run("explode");
        Assert.Equal(2, code);
        Assert.Contains("usage", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dump_PrintsAnXnbFilesObjectsAsJson()
    {
        var (code, output, _) = Run("dump", Content("hello.xnb"));

        Assert.Equal(0, code);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal("hi", json.GetProperty("Root").GetString());
        Assert.Equal(0, json.GetProperty("SharedResources").GetArrayLength());
    }

    [Fact]
    public void Dump_ReadsSceneXml_WithItsContentPathFromTheContentDirectory()
    {
        var (code, output, _) = Run("dump", Content("Levels", "Swamp", "swamp_s1.xml"));

        Assert.Equal(0, code);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal("SceneDefinition", json.GetProperty("$type").GetString());
        Assert.Equal("Levels/Swamp/swamp_s1", json.GetProperty("Model").GetString());
        Assert.True(json.GetProperty("Settings").GetProperty("Indoor").GetBoolean());
    }

    [Fact]
    public void Dump_OfABrokenOrMissingFile_FailsWithAMessage()
    {
        File.WriteAllText(Content("broken.xnb"), "not an xnb");

        var broken = Run("dump", Content("broken.xnb"));
        var missing = Run("dump", Content("missing.xnb"));

        Assert.Equal(1, broken.Code);
        Assert.Contains("broken.xnb", broken.Error, StringComparison.Ordinal);
        Assert.Equal(1, missing.Code);
        Assert.Contains("missing.xnb", missing.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dump", "")]
    [InlineData("info", " ")]
    [InlineData("check", "")]
    public void EmptyPaths_AreBadUsage(string command, string path)
    {
        Assert.Equal(2, Run(command, path).Code);
    }

    [Fact]
    public void Dump_ExplainsFilesItCannotRead()
    {
        File.WriteAllText(Content("empty.xml"), "");
        File.WriteAllText(Content("other.xml"), "<Cutscenes/>");
        File.WriteAllText(Content("image.png"), "PNG");

        Assert.All(new[] { "empty.xml", "other.xml", "image.png", "Levels" }, name =>
        {
            var (code, _, error) = Run("dump", Content(name));
            Assert.Equal(1, code);
            Assert.Contains(name, error, StringComparison.Ordinal);
        });
        Assert.Contains("is a directory", Run("dump", Content("Levels")).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_ReadsEveryKindOfContentXml_AndAcceptsFullFirst()
    {
        File.WriteAllText(Content("Levels", "Dialog_x.xml"), DialogReaderTests.Dialogs("<Dialog id=\"a\"/>"));
        File.WriteAllText(Content("fx.xml"), "<Effect duration=\"1\"/>");
        File.WriteAllText(Content("t.loctable.xml"), OpenMagicka.Tests.Localization.LocTableReaderTests.Workbook(
            OpenMagicka.Tests.Localization.LocTableReaderTests.Row("#k", "v")));
        File.WriteAllText(Content("Levels", "x.lvl"), LevelReaderTests.Level(""));

        Assert.Equal("a", JsonDocument.Parse(Run("dump", Content("Levels", "Dialog_x.xml")).Out).RootElement[0].GetProperty("Id").GetString());
        Assert.Equal(1, JsonDocument.Parse(Run("dump", "--full", Content("fx.xml")).Out).RootElement.GetProperty("Duration").GetSingle());
        Assert.Equal("v", JsonDocument.Parse(Run("dump", Content("t.loctable.xml")).Out).RootElement[0].GetProperty("Text").GetString());
        Assert.Equal("Levels/ch_test.xml", JsonDocument.Parse(Run("dump", Content("Levels", "x.lvl")).Out).RootElement.GetProperty("Scenes")[0].GetString());
    }

    [Fact]
    public void Dump_OfABadLevel_NamesTheFile()
    {
        File.WriteAllText(Content("Levels", "bad.lvl"), "<Level/>");

        Assert.Contains("bad.lvl", Run("dump", Content("Levels", "bad.lvl")).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_OfAMissingFile_NamesIt()
    {
        var (code, _, error) = Run("info", Content("nope.xnb"));

        Assert.Equal(1, code);
        Assert.Contains("nope.xnb: file not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_PrintsTheXnbHeader()
    {
        var (code, output, _) = Run("info", Content("hello.xnb"));

        Assert.Equal(0, code);
        Assert.Contains("StringReader", output, StringComparison.Ordinal);
        Assert.Contains("compressed: no", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ReadsEveryFile_AndReportsFailures()
    {
        Assert.Equal(0, Run("check", Content()).Code);

        File.WriteAllText(Content("broken.xnb"), "not an xnb");
        var (code, output, _) = Run("check", Content());

        Assert.Equal(1, code);
        Assert.Contains("broken.xnb", output, StringComparison.Ordinal);
        Assert.Contains("1 failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_OfAFolderWithoutContent_OrAMissingOne_IsBadUsage()
    {
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);

        var (code, output, _) = Run("check", empty);
        Assert.Equal(2, code);
        Assert.Contains("no Magicka content", output, StringComparison.Ordinal);
        Assert.Equal(2, Run("check", Path.Combine(root, "missing")).Code);
    }

    [Fact]
    public void Check_ListsEveryBrokenEffectAndLanguageFile()
    {
        Directory.CreateDirectory(Content("Effects"));
        File.WriteAllText(Content("Effects", "a.xml"), "<Effect type=\"never\"/>");
        File.WriteAllText(Content("Effects", "b.xml"), "<Effect duration=\"x\"/>");
        Directory.CreateDirectory(Content("Languages", "eng"));
        File.WriteAllText(Content("Languages", "eng", "ui.loctable.xml"), "<Workbook>");

        var (code, output, _) = Run("check", Content());

        Assert.Equal(1, code);
        Assert.Contains("Effects/a.xml", output, StringComparison.Ordinal);
        Assert.Contains("Effects/b.xml", output, StringComparison.Ordinal);
        Assert.Contains("ui.loctable.xml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_AcceptsTheGameDirectoryToo()
    {
        Assert.Equal(0, Run("check", root).Code);   // the folder holding Content
    }
}
