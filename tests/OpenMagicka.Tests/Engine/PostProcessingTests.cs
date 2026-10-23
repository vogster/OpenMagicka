using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class PostProcessingTests
{
    [Fact]
    public void TheSettings_AreTheScenesFilterAndBloom()
    {
        var settings = SceneSettings.Default with { Filter = new FilterSettings(1.2f, 0.9f, 0.5f), Bloom = new BloomSettings(0.6f, 1.5f, 3) };

        var post = PostProcessing.For(settings);

        Assert.Equal(new PostProcessing(1.2f, 0.9f, 0.5f, 0.6f, 1.5f, 3), post);
    }

    [Fact]
    public void WithoutAScene_TheOriginalsDefaultsAreUsed()
    {
        Assert.Equal(new PostProcessing(1, 1, 1, 0.8f, 1, 2.5f), PostProcessing.For(null));
        Assert.Equal(PostProcessing.Default, PostProcessing.For(SceneSettings.Default));
    }

    [Theory]
    [InlineData(1280, 720, 80, 45)]
    [InlineData(1920, 1080, 120, 67)]
    [InlineData(8, 8, 1, 1)]
    public void TheBloomBuffers_AreASixteenthOfTheScreen_AtLeastOnePixel(int width, int height, int bloomWidth, int bloomHeight)
    {
        Assert.Equal((bloomWidth, bloomHeight), PostProcessing.BloomSize(width, height));
    }
}
