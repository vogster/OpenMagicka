using OpenMagicka.Content.Xnb;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Content;

/// <summary>The viewer's catalog and batches on the game's files (skipped without MAGICKA_DIR).</summary>
public class ViewerContentTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryModel_TurnsIntoDrawableBatches()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new OpenMagicka.Content.ContentLoader(files, XnbReaderRegistry.Magicka);
        var catalog = OpenMagicka.Game.Viewer.SceneCatalog.Load(files);
        var batches = 0;
        foreach (var model in catalog.Models)
        {
            var found = OpenMagicka.Engine.Rendering.ModelMeshes.ForModel(loader.LoadUncached(model.Model));
            Assert.All(found, b => Assert.True(float.IsFinite(b.Bounds.Min.X + b.Bounds.Max.X), $"{model.Model}: bounds {b.Bounds}"));
            batches += found.Count;
        }
        output.WriteLine($"{catalog.Scenes.Count} scenes, {catalog.Models.Count} models with {batches} batches, {catalog.Problems.Count} problems");
        Assert.Equal((141, 874), (catalog.Scenes.Count, catalog.Models.Count));
        Assert.Empty(catalog.Problems);
    }
}
