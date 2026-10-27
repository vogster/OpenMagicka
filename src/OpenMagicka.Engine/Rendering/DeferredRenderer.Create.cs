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
        DeferredRenderer renderer;
        try
        {
            renderer = await gpu.Enqueue(device => new DeferredRenderer(device, effects, particleResources, postEffects)).ConfigureAwait(false);
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
        renderer.ParticlesUnavailable ??= unavailable;
        renderer.PostUnavailable ??= postUnavailable;
        if (loader is not null)
        {
            try
            {
                await renderer.LoadSpells(shaders, gpu, loader).ConfigureAwait(false);
                await renderer.LoadGui(shaders, gpu, loader).ConfigureAwait(false);
            }
            catch
            {
                _ = gpu.Enqueue(_ => renderer.Dispose());                   // cancelled: the renderer, with what it took, goes
                throw;
            }
        }
        return renderer;
    }

    /// <summary>The spells' own pictures, optional like the particles (<see cref="SpellsUnavailable"/>).</summary>
    private async Task LoadSpells(ShaderLibrary shaders, Threading.RenderQueue<GraphicsDevice> gpu, Content.ContentLoader loader)
    {
        try
        {
            spells = await SpellPass.Load(shaders, gpu, loader, additive).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SpellsUnavailable = e.GetBaseException().Message;
        }
    }

    /// <summary>What the screen shows over the world, optional like the particles (<see cref="GuiUnavailable"/>); in English till OM-52.</summary>
    private async Task LoadGui(ShaderLibrary shaders, Threading.RenderQueue<GraphicsDevice> gpu, Content.ContentLoader loader)
    {
        try
        {
            gui = await GuiPass.Load(shaders, gpu, loader, Content.Localization.LanguagePack.Fallback).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            GuiUnavailable = e.GetBaseException().Message;
        }
    }

    /// <summary>The filter and bloom effects, optional like the particles.</summary>
    private static async Task<(GameEffect[]?, string?)> LoadPost(ShaderLibrary shaders)
    {
        try
        {
            var effects = await Task.WhenAll(shaders.Load("FilterEffect"), shaders.Load("BloomEffect")).ConfigureAwait(false);
            GameEffect? fade;
            try
            {
                fade = await shaders.Load("FadeTransitionEffect").ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                fade = null;                                                  // without it the screen's cover is drawn plainly
            }
            return ([.. effects, fade!], null);
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
