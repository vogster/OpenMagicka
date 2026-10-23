using OpenMagicka.Content;

namespace OpenMagicka.Tests.Content;

public sealed class ContentFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omfs-" + Guid.NewGuid().ToString("N"));

    public ContentFileSystemTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Models", "Items_NPC"));
        Directory.CreateDirectory(Path.Combine(root, "Audio"));
        File.WriteAllText(Path.Combine(root, "Models", "Items_NPC", "acc_AmmoBelt_0.xnb"), "belt");
        File.WriteAllText(Path.Combine(root, "Audio", "Loc.xwb"), "loc");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData(@"Models\Items_NPC\acc_AmmoBelt_0.xnb")]
    [InlineData("models/items_npc/ACC_AMMOBELT_0.XNB")]
    [InlineData(@"Models\Items_NPC\..\Items_NPC\.\acc_ammobelt_0.xnb")]
    [InlineData(@"\Models\\Items_NPC\acc_ammobelt_0.xnb")]
    public void Resolve_IgnoresCaseSeparatorsAndDotSegments(string path)
    {
        var fs = new ContentFileSystem(root);

        Assert.Equal(Path.Combine(root, "Models", "Items_NPC", "acc_AmmoBelt_0.xnb"), fs.Resolve(path));
    }

    [Fact]
    public void Resolve_MissingFileOrEscapingTheRoot_IsNull()
    {
        var fs = new ContentFileSystem(root);

        Assert.Null(fs.Resolve("Audio/loc.xgs"));
        Assert.Null(fs.Resolve(@"..\outside.xnb"));
        Assert.Null(fs.Resolve(""));
        Assert.Null(fs.Resolve(".."));
        Assert.Null(fs.Resolve("Models"));                     // a directory is not a file
        Assert.Null(fs.Resolve(Path.Combine(root, "Audio", "Loc.xwb"))); // absolute paths are not content paths
    }

    [Fact]
    public void ReadAllBytes_OpensTheRealFile()
    {
        var fs = new ContentFileSystem(root);

        Assert.Equal("loc"u8.ToArray(), fs.ReadAllBytes("audio/LOC.XWB"));
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllBytes("audio/missing.xwb"));
    }

    [Fact]
    public void Combine_ResolvesAReferenceRelativeToTheReferringAsset()
    {
        Assert.Equal("Models/Items/staff_0",
            ContentFileSystem.Combine(@"Data\Items\Wizard\staff", @"..\..\..\Models\Items\staff_0"));
        Assert.Equal("Shaders/Maya/SkinnedModelEffect_0",
            ContentFileSystem.Combine("Models/AnimatedProps/Dungeons/radio", @"..\..\..\Shaders\Maya\SkinnedModelEffect_0"));
        Assert.Null(ContentFileSystem.Combine("Data/x", @"..\..\..\escape"));
    }

    [Fact]
    public void Constructor_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new ContentFileSystem(Path.Combine(root, "nope")));
    }
}
