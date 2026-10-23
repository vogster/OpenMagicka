using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Levels;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The scene's colour filter and bloom (docs/engine/ARCHITECTURE.md §3.2, "Post-processing").</summary>
public sealed record PostProcessing(float Brightness, float Contrast, float Saturation, float BloomThreshold, float BloomMultiplier, float BlurSigma)
{
    /// <summary>The values the original resets to before a scene sets its own.</summary>
    public static PostProcessing Default { get; } = new(1, 1, 1, 0.8f, 1, 2.5f);

    public static PostProcessing For(SceneSettings? settings) => settings is null
        ? Default
        : new(settings.Filter.Brightness, settings.Filter.Contrast, settings.Filter.Saturation,
            settings.Bloom.Threshold, settings.Bloom.Multiplier, settings.Bloom.Blur);

    /// <summary>The bloom buffers: a sixteenth of the screen on each side (at least one pixel).</summary>
    public static (int Width, int Height) BloomSize(int width, int height) => (Math.Max(width / 16, 1), Math.Max(height / 16, 1));
}

/// <summary>
/// The original's post-processing after the scene: <c>FilterEffect</c> (brightness, contrast,
/// saturation) into a screen-sized target, then bloom with <c>BloomEffect</c>: the bright parts
/// (above the threshold) down-sampled into a sixteenth-size buffer, blurred horizontally then
/// vertically (σ), and added onto the filtered picture (× multiplier) on the back buffer. The game
/// draws its GUI between the filter and the bloom, so the GUI glows too; the viewer has none.
/// Render thread only.
/// </summary>
internal sealed class PostPass : IDisposable
{
    private const int HorizontalBlur = 0;
    private const int VerticalBlur = 1;
    private const int Combine = 2;
    private const int DownSample = 3;

    private readonly GameEffect filter;
    private readonly GameEffect bloom;
    private RenderTarget2D? filtered;
    private RenderTarget2D? bloomA;
    private RenderTarget2D? bloomB;

    public PostPass(GameEffect filter, GameEffect bloom)
    {
        if (bloom.Effect.Techniques.Count <= DownSample)
        {
            throw new InvalidDataException("the bloom effect does not have the expected techniques");
        }
        EffectChecks.Require(filter, ["SourceTexture0", "Brightness", "Contrast", "Saturation", "DestinationDimensions"]);
        EffectChecks.Require(bloom, ["SourceTexture0", "SourceTexture1", "SourceDimensions", "Threshold", "Sigma", "Multiplier", "DestinationDimensions"]);
        (this.filter, this.bloom) = (filter, bloom);
    }

    public IEnumerable<GameEffect> Effects => [filter, bloom];

    /// <summary>Filters and blooms <paramref name="scene"/> onto the back buffer.</summary>
    public void Draw(GraphicsDevice device, FullscreenQuad quad, Texture2D scene, PostProcessing settings)
    {
        EnsureTargets(device, scene.Width, scene.Height);

        device.SetRenderTarget(filtered);
        var filterParameters = new FnaEffectParameters(filter.Effect);
        filterParameters.SetValue("SourceTexture0", scene);
        filterParameters.SetValue("Brightness", settings.Brightness);
        filterParameters.SetValue("Contrast", settings.Contrast);
        filterParameters.SetValue("Saturation", settings.Saturation);
        quad.Draw(device, filter, BlendState.Opaque);

        var parameters = new FnaEffectParameters(bloom.Effect);
        var small = new Vector2(bloomA!.Width, bloomA.Height);
        var large = new Vector2(filtered!.Width, filtered.Height);
        // Down-sample the bright parts; the effect steps through the source by its size and shifts
        // by half a source pixel, which is cancelled.
        Unbind(device);
        device.SetRenderTarget(bloomA);
        parameters.SetValue("SourceTexture0", filtered);
        parameters.SetValue("SourceDimensions", large);
        parameters.SetValue("Threshold", settings.BloomThreshold);
        Pass(device, quad, DownSample, null, new Vector2(-0.5f) / large);
        // The blurs step by the target's size, so they get the real one, and its half-pixel shift is cancelled.
        Unbind(device);
        device.SetRenderTarget(bloomB);
        parameters.SetValue("SourceTexture0", bloomA);
        parameters.SetValue("Sigma", settings.BlurSigma);
        Pass(device, quad, HorizontalBlur, small, new Vector2(-0.5f) / small);
        Unbind(device);
        device.SetRenderTarget(bloomA);
        parameters.SetValue("SourceTexture0", bloomB);
        parameters.SetValue("Threshold", 0f);
        Pass(device, quad, VerticalBlur, small, new Vector2(-0.5f) / small);
        Unbind(device);
        device.SetRenderTarget(null);
        parameters.SetValue("SourceTexture0", filtered);
        parameters.SetValue("SourceTexture1", bloomA);
        parameters.SetValue("Multiplier", settings.BloomMultiplier);
        Pass(device, quad, Combine, null, null);
        Unbind(device);
    }

    private void Pass(GraphicsDevice device, FullscreenQuad quad, int technique, Vector2? destination, Vector2? uvShift)
    {
        bloom.Effect.CurrentTechnique = bloom.Effect.Techniques[technique];
        quad.Draw(device, bloom, BlendState.Opaque, destination: destination, uvShift: uvShift);
    }

    private static void Unbind(GraphicsDevice device)
    {
        for (var slot = 0; slot < 4; slot++)
        {
            device.Textures[slot] = null;
        }
    }

    private void EnsureTargets(GraphicsDevice device, int width, int height)
    {
        if (filtered is not null && filtered.Width == width && filtered.Height == height)
        {
            return;
        }
        DisposeTargets();
        var (w, h) = PostProcessing.BloomSize(width, height);
        filtered = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        bloomA = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        bloomB = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
    }

    private void DisposeTargets()
    {
        filtered?.Dispose();
        bloomA?.Dispose();
        bloomB?.Dispose();
        (filtered, bloomA, bloomB) = (null, null, null);
    }

    public void Dispose() => DisposeTargets();
}
