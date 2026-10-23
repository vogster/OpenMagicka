using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The renderer's geometry passes: shadow casters, the opaque objects, the sky and the additive objects.</summary>
public sealed partial class DeferredRenderer
{
    /// <summary>
    /// The sun's shadow map, when it casts shadows: the opaque geometry in the light's box, drawn with
    /// the shadow techniques and the light's view-projection (which the casters take from the shared
    /// globals, so these are set to the light's view for this pass).
    /// </summary>
    private SunShadow? DrawSunShadow(GraphicsDevice device, GpuLevel level, DeferredFrame frame)
    {
        var size = frame.Sun.ShadowMapSize;
        if (size <= 0)
        {
            shadowMap?.Dispose();
            shadowMap = null;
            return null;
        }
        if (shadowMap is null || shadowMap.Width != size)
        {
            shadowMap?.Dispose();
            shadowMap = null;
            shadowMap = new RenderTarget2D(device, size, size, false, SurfaceFormat.Single, DepthFormat.Depth24);
        }
        var view = SunShadowView.Around(frame.Sun.Direction, frame.ShadowFocus, size);
        device.SetRenderTarget(shadowMap);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, NoCaster, 1, 0);
        DrawCasters(device, level, frame, view.LightView, view.LightProjection, RasterizerState.CullCounterClockwise);
        return new SunShadow(shadowMap, view);
    }

    /// <summary>
    /// The opaque geometry seen by a light, with the shadow techniques, into the bound shadow map.
    /// The casters take the view-projection from the shared globals, so these are set to the
    /// light's view here; the camera's are set again before the camera passes.
    /// </summary>
    private void DrawCasters(GraphicsDevice device, GpuLevel level, DeferredFrame frame, Matrix view, Matrix projection, RasterizerState cull)
    {
        var lightGlobals = frame.Globals with { View = view, Projection = projection };
        foreach (var effect in new[] { deferred, skinned, water, lava })
        {
            effect.Globals.Apply(lightGlobals);
        }
        shadowFrustum.Matrix = lightGlobals.ViewProjection;
        device.RasterizerState = cull;
        DrawOpaque(device, level, frame, OpaqueKind.Shadow, shadowFrustum);
        device.RasterizerState = RasterizerState.CullNone;
    }

    private int DrawOpaque(GraphicsDevice device, GpuLevel level, DeferredFrame frame, OpaqueKind pass, BoundingFrustum view)
    {
        var liquids = (water: new FnaEffectParameters(water.Effect), lava: new FnaEffectParameters(lava.Effect));
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        var parameters = new FnaEffectParameters(deferred.Effect);
        var characters = new FnaEffectParameters(skinned.Effect);
        skinned.Effect.Parameters["Bones"].SetValue(bindPose);
        skinned.Effect.CurrentTechnique = skinned.Effect.Techniques[pass switch
        {
            OpaqueKind.Shadow => SkinnedDeferredMaterial.Shadow,
            OpaqueKind.Depth => SkinnedDeferredMaterial.Depth,
            _ => SkinnedDeferredMaterial.Default,
        }];
        var drawn = 0;
        foreach (var batch in level.Batches)
        {
            if (!view.Intersects(batch.Bounds))
            {
                continue;
            }
            if (batch.Material is SkinnedModelBasicEffectContent character && !batch.Additive)
            {
                SkinnedDeferredMaterial.Apply(characters, character, level.TextureFor);
                Draw(device, skinned.Effect, batch);
                drawn++;
                continue;
            }
            if (batch.Material is RenderDeferredLiquidEffectContent or LavaEffectContent)
            {
                var (effect, liquid) = batch.Material is LavaEffectContent ? (lava, liquids.lava) : (water, liquids.water);
                LiquidMaterial.Apply(liquid, batch.Material, batch.World, frame.Seconds, level.TextureFor);
                var reflects = batch.Material is RenderDeferredLiquidEffectContent w && level.TextureFor(w.ReflectionMap) is not null;
                effect.Effect.CurrentTechnique = effect.Effect.Techniques[LiquidMaterial.Technique(batch.Material, reflects, pass)];
                Draw(device, effect.Effect, batch);
                drawn++;
                continue;
            }
            if (batch.Deferred is not { } material)
            {
                continue;
            }
            RenderDeferredMaterial.Apply(parameters, batch.World, material, level.TextureFor);
            var technique = pass switch
            {
                OpaqueKind.Shadow => RenderDeferredMaterial.Shadow,
                OpaqueKind.Depth => RenderDeferredMaterial.Depth,
                _ => RenderDeferredMaterial.Technique(material, level.TextureFor(material.ReflectionMap) is not null),
            };
            deferred.Effect.CurrentTechnique = deferred.Effect.Techniques[technique];
            Draw(device, deferred.Effect, batch);
            drawn++;
        }
        return drawn;
    }

    /// <summary>
    /// The scene's sky picture where nothing was drawn: a fullscreen pass at the far plane, tested
    /// LessEqual against Main's depth without writing it. The effect fits the picture to the screen's
    /// height, so it needs the real target size.
    /// </summary>
    private void DrawSky(GraphicsDevice device, GpuLevel level)
    {
        if (level.SkyMap is not { } map)
        {
            return;
        }
        var parameters = new FnaEffectParameters(sky.Effect);
        parameters.SetValue("SourceTexture0", map);
        parameters.SetValue("SourceDimensions", new Vector2(map.Width, map.Height));
        parameters.SetValue("Color", level.SkyColor);
        quad.Draw(device, sky, BlendState.Opaque, SkyDepth, new Vector2(main!.Width, main.Height));
    }

    private void DrawAdditive(GraphicsDevice device, GpuLevel level, Func<Content.Xna.ExternalReference?, Texture?> textures)
    {
        device.BlendState = AdditiveBlend;
        device.DepthStencilState = DepthStencilState.DepthRead;
        var parameters = new FnaEffectParameters(additive.Effect);
        foreach (var batch in level.Batches)
        {
            if (!batch.Additive || batch.Material is not AdditiveEffectContent material || !frustum.Intersects(batch.Bounds))
            {
                continue;
            }
            var texture = material.TextureEnabled ? textures(material.Texture) : null;
            parameters.SetValue("World", batch.World);
            parameters.SetValue("TextureScale", Vector2.One);
            parameters.SetValue("TextureOffset", Vector2.Zero);
            parameters.SetValue("ColorTint", new Vector4(material.ColorTint, 1));   // the original reads the tint with alpha 1
            parameters.SetValue("VertexColorEnabled", material.VertexColorEnabled);
            parameters.SetValue("TextureEnabled", texture is not null);
            parameters.SetValue("Texture", texture);
            Draw(device, additive.Effect, batch);
        }
    }

    private static void Draw(GraphicsDevice device, Effect effect, GpuBatch batch)
    {
        device.SetVertexBuffer(batch.Vertices);
        device.Indices = batch.Indices;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.VertexCount, batch.StartIndex, batch.PrimitiveCount);
        }
    }
}
