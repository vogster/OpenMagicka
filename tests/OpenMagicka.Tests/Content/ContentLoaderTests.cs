using OpenMagicka.Content;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;
using static OpenMagicka.Tests.Content.XnbTestWriter;

namespace OpenMagicka.Tests.Content;

public sealed class ContentLoaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omcl-" + Guid.NewGuid().ToString("N"));

    public ContentLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Models", "Items"));
        Directory.CreateDirectory(Path.Combine(root, "Data", "Items"));
        Write(Path.Combine(root, "Models", "Items", "Staff_0.xnb"), new XnbTestWriter().StringObject("staff model"));
        var item = new XnbTestWriter().TypeId(XnaContent + "ExternalReferenceReader" + Xna);
        item.Writer.Write(@"..\..\Models\Items\staff_0");
        Write(Path.Combine(root, "Data", "Items", "staff.xnb"), item);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static void Write(string path, XnbTestWriter writer) => File.WriteAllBytes(path, writer.BuildBytes());

    private ContentLoader Loader() => new(new ContentFileSystem(root), XnbReaderRegistry.Magicka);

    [Fact]
    public void Load_FindsTheAssetWithoutExtensionInAnyCase()
    {
        var content = Loader().Load("models/ITEMS/staff_0");

        Assert.Equal("staff model", content.Root);
    }

    [Fact]
    public void Load_CachesByNormalizedPath()
    {
        var loader = Loader();

        Assert.Same(loader.Load("Models/Items/Staff_0"), loader.Load(@"models\items\staff_0"));
    }

    [Fact]
    public void LoadReference_ResolvesRelativeToTheReferringAsset()
    {
        var loader = Loader();
        var reference = Assert.IsType<ExternalReference>(loader.Load("Data/Items/staff").Root);

        Assert.Equal("Models/Items/staff_0", loader.ResolveReference("Data/Items/staff", reference));
        Assert.Equal("staff model", loader.LoadReference("Data/Items/staff", reference).Root);
    }

    [Fact]
    public void Load_MissingAsset_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => Loader().Load("Models/Items/missing"));
        Assert.Throws<FileNotFoundException>(() => Loader().LoadReference("Data/Items/staff", new ExternalReference(@"..\..\..\escape")));
    }

    [Fact]
    public void Load_FailedLoadIsRetried()
    {
        var path = Path.Combine(root, "Models", "Items", "late.xnb");
        File.WriteAllBytes(path, [(byte)'X', (byte)'N', (byte)'B']);
        var loader = Loader();
        Assert.Throws<InvalidDataException>(() => loader.Load("Models/Items/late"));

        Write(path, new XnbTestWriter().StringObject("fixed"));

        Assert.Equal("fixed", loader.Load("Models/Items/late").Root);
    }

    [Fact]
    public void Load_ConcurrentCallers_GetTheSameInstance()
    {
        var loader = Loader();

        var results = Enumerable.Range(0, 16).AsParallel().Select(_ => loader.Load("Models/Items/Staff_0")).ToArray();

        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public void Load_CorruptFile_NamesTheAsset()
    {
        File.WriteAllBytes(Path.Combine(root, "Models", "Items", "broken.xnb"), [(byte)'X', (byte)'N', (byte)'B']);

        var error = Assert.Throws<InvalidDataException>(() => Loader().Load("Models/Items/broken"));
        Assert.Contains("Models/Items/broken", error.Message);
    }

    [Fact]
    public void LoadUncached_ReadsAFreshCopyEachTime_AndLeavesTheCacheAlone()
    {
        var loader = Loader();

        var first = loader.LoadUncached("Models/Items/Staff_0");
        var second = loader.LoadUncached("models/items/staff_0");

        Assert.Equal("staff model", first.Root);
        Assert.NotSame(first, second);
        Assert.Same(loader.Load("Models/Items/Staff_0"), loader.Load("Models/Items/Staff_0"));
        Assert.Throws<FileNotFoundException>(() => loader.LoadUncached("Models/missing"));
    }
}
