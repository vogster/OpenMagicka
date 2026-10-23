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

    [GameContentFact]
    public void EverySceneSky_IsAPictureTheViewerCanLoad()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new OpenMagicka.Content.ContentLoader(files, XnbReaderRegistry.Magicka);
        var skies = OpenMagicka.Game.Viewer.SceneCatalog.Load(files).Scenes.Select(s => s.Settings?.SkyMap).OfType<OpenMagicka.Content.Levels.SkyMapSettings>().ToList();

        Assert.All(skies, sky => OpenMagicka.Engine.Rendering.TextureFormats.ToFna(
            Assert.IsType<OpenMagicka.Content.Xna.Texture2DContent>(loader.LoadUncached(sky.Texture).Root).Format));
        Assert.Equal(19, skies.Count);     // of the 21 scene files with a sky, 19 are in the levels
    }

    [GameContentFact]
    public void TheParticleSheets_AreVolumeTexturesTheRendererCanCreate()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);

        var depths = OpenMagicka.Engine.Rendering.ParticleSheets.Assets
            .Select(asset => Assert.IsType<OpenMagicka.Content.Xna.Texture3DContent>(loader.LoadUncached(asset).Root))
            .Select(sheet => (sheet.Format, sheet.Width, sheet.Depth)).ToList();

        Assert.All(depths, d => Assert.Equal((OpenMagicka.Content.Xna.SurfaceFormatXna31.Color, 256), (d.Format, d.Width)));
        Assert.Equal([1, 4, 4, 8], depths.Select(d => d.Depth));
    }
}
