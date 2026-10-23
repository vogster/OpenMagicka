using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>Building the renderer: the effects, and the optional particle resources.</summary>
public sealed partial class DeferredRenderer
{
    /// <summary>
    /// Loads the effects (created on the render thread through the library's queue) and builds the
    /// renderer there, with the filter and bloom when they load (<see cref="PostUnavailable"/>). With a
    /// content loader it also loads <c>ParticleEffect</c> and the particles' sprite sheets; when those
    /// fail the renderer is built without particles (<see cref="ParticlesUnavailable"/> says why).
    /// </summary>
    public static async Task<DeferredRenderer> Create(ShaderLibrary shaders, Threading.RenderQueue<GraphicsDevice> gpu, Content.ContentLoader? loader = null)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        var effects = await Task.WhenAll(EffectNames.Select(shaders.Load)).ConfigureAwait(false);
        var (particleResources, unavailable) = loader is null ? (null, null) : await LoadParticles(shaders, gpu, loader).ConfigureAwait(false);
        var (postEffects, postUnavailable) = await LoadPost(shaders).ConfigureAwait(false);
        try
        {
            var renderer = await gpu.Enqueue(device => new DeferredRenderer(device, effects, particleResources, postEffects)).ConfigureAwait(false);
            renderer.ParticlesUnavailable ??= unavailable;
            renderer.PostUnavailable ??= postUnavailable;
            return renderer;
        }
        catch
        {
            // The renderer never took the sheets.
            if (particleResources is not null)
            {
                _ = gpu.Enqueue(_ => Array.ForEach(particleResources.Sheets, s => s.Dispose()));
            }
            throw;
        }
    }

    /// <summary>The filter and bloom effects, optional like the particles.</summary>
    private static async Task<(GameEffect[]?, string?)> LoadPost(ShaderLibrary shaders)
    {
        try
        {
            return (await Task.WhenAll(shaders.Load("FilterEffect"), shaders.Load("BloomEffect")).ConfigureAwait(false), null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (null, e.GetBaseException().Message);
        }
    }

    private static async Task<(ParticleResources?, string?)> LoadParticles(ShaderLibrary shaders, Threading.RenderQueue<GraphicsDevice> gpu, Content.ContentLoader loader)
    {
        try
        {
            var effect = await shaders.Load("ParticleEffect").ConfigureAwait(false);
            var sheets = await ParticleSheets.Load(loader, gpu).ConfigureAwait(false);
            return (new ParticleResources(effect, sheets), null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (null, e.GetBaseException().Message);
        }
    }
}
