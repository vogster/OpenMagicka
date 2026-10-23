using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// What one frame is drawn with besides the geometry; <see cref="ShadowFocus"/> is where the sun's
/// shadows are wanted (see <see cref="SunShadowView.Focus"/>), <see cref="Seconds"/> the time the
/// liquids' waves and flow have run.
/// </summary>
public sealed record DeferredFrame(GlobalEffectValues Globals, DirectionalLighting Sun, SceneFog Fog, Vector3 Background, Vector3 ShadowFocus,
    float Seconds = 0);

/// <summary>
/// The original deferred pipeline on the original effects (docs/engine/ARCHITECTURE.md §3.2–3.3):
/// the sun's shadow map, a depth prepass into an R32F target, the G-buffer (Main + Normal, after
/// RestoreDepth), the lights into a light accumulator (<see cref="LightPass"/>), the composition
/// with fog, then additive objects, and the result shown on the back buffer. Shadow maps are drawn
/// first: the sun's, then the shadow-casting point and spot lights' (<see cref="LightShadows"/>).
/// Water and lava are drawn with the opaque objects (<see cref="LiquidMaterial"/>), the scene's sky
/// after the composition. Not yet: decals, particles, post effects (OM-25 to OM-27). The original
/// composes into Main while reading it; here Main is copied first, since sampling and writing one
/// target is undefined on modern APIs. Render thread only.
/// </summary>
public sealed class DeferredRenderer : IDisposable
{
    public static readonly string[] EffectNames =
        [
            "RenderDeferredEffectWOVT", "ClearEffect", "RestoreDepthEffect", "DirectionalLightEffect", "DeferredShadingEffect", "AdditiveEffect",
            "SkinnedModelDeferredEffect", "PointLightEffect", "SpotLightEffect", "RenderDeferredLiquidEffect", "LavaEffect", "SkyMapEffect",
        ];

    /// <summary>
    /// The far plane the projection must use: the original camera always has 500, and the light
    /// shaders rebuild world positions from the linear depth with a view ray scaled by 1/500.
    /// </summary>
    public const float FarPlane = 500;

    private static readonly Vector4 NormalClear = new(0.5f, 0.75f, 0.5f, 0.5f);

    /// <summary>The sky's depth test: behind everything drawn, without writing depth.</summary>
    private static readonly DepthStencilState SkyDepth = new()
    {
        DepthBufferEnable = true, DepthBufferWriteEnable = false, DepthBufferFunction = CompareFunction.LessEqual,
    };

    /// <summary>The shadow map is cleared far beyond any caster, so where nothing was drawn is lit.</summary>
    private static readonly Vector4 NoCaster = new(1e6f);

    /// <summary>Additive objects: InvDestColor / One.</summary>
    private static readonly BlendState AdditiveBlend = new()
    {
        ColorSourceBlend = Blend.InverseDestinationColor, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.InverseDestinationColor, AlphaDestinationBlend = Blend.One,
    };

    private readonly GameEffect deferred;
    private readonly GameEffect clear;
    private readonly GameEffect restoreDepth;
    private readonly GameEffect shading;
    private readonly GameEffect additive;
    private readonly GameEffect skinned;
    private readonly GameEffect water;
    private readonly GameEffect lava;
    private readonly GameEffect sky;
    private readonly LightPass lights;
    private readonly LightShadows lightShadows = new();
    private readonly Matrix[] bindPose;
    private readonly FullscreenQuad quad;
    private readonly SpriteBatch sprites;
    private readonly BoundingFrustum frustum = new(Matrix.Identity);
    private readonly BoundingFrustum shadowFrustum = new(Matrix.Identity);
    private RenderTarget2D? depth;
    private RenderTarget2D? main;
    private RenderTarget2D? normal;
    private RenderTarget2D? light;
    private RenderTarget2D? copy;
    private RenderTarget2D? shadowMap;

    private DeferredRenderer(GraphicsDevice device, IReadOnlyList<GameEffect> effects)
    {
        (deferred, clear, restoreDepth, shading, additive, skinned) = (effects[0], effects[1], effects[2], effects[4], effects[5], effects[6]);
        (water, lava, sky) = (effects[9], effects[10], effects[11]);
        // Characters are shown in their bind pose: every skin transform is the identity.
        bindPose = Enumerable.Repeat(Matrix.Identity, skinned.Effect.Parameters["Bones"]?.Elements.Count ?? 0).ToArray();
        // The techniques are used by index; a different effect fails here, so the viewer falls back.
        if (deferred.Effect.Techniques.Count <= RenderDeferredMaterial.Shadow || skinned.Effect.Techniques.Count <= SkinnedDeferredMaterial.Shadow
            || water.Effect.Techniques.Count <= LiquidMaterial.Shadow || lava.Effect.Techniques.Count <= LiquidMaterial.LavaShadow
            || bindPose.Length == 0
            || !new[] { "SourceTexture0", "SourceDimensions", "Color", "DestinationDimensions" }.All(new FnaEffectParameters(sky.Effect).Contains))
        {
            throw new InvalidDataException("the deferred effects do not have the expected techniques");
        }
        lights = new LightPass(device, effects[3], effects[7], effects[8]);
        try
        {
            quad = new FullscreenQuad(device);
            sprites = new SpriteBatch(device);
        }
        catch
        {
            lights.Dispose();
            quad?.Dispose();
            throw;
        }
    }

    /// <summary>Loads the effects (created on the render thread through the library's queue) and builds the renderer there.</summary>
    public static async Task<DeferredRenderer> Create(ShaderLibrary shaders, Threading.RenderQueue<GraphicsDevice> gpu)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        var effects = await Task.WhenAll(EffectNames.Select(shaders.Load)).ConfigureAwait(false);
        return await gpu.Enqueue(device => new DeferredRenderer(device, effects)).ConfigureAwait(false);
    }

    /// <summary>Draws the level and shows it on the back buffer; returns how many batches were drawn into the G-buffer.</summary>
    public int Render(GraphicsDevice device, GpuLevel level, DeferredFrame frame)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(frame);
        EnsureTargets(device);
        device.RasterizerState = RasterizerState.CullNone;
        UnbindTextures(device);
        var globals = frame.Globals;
        frustum.Matrix = globals.ViewProjection;
        var shadow = DrawSunShadow(device, level, frame);
        lightShadows.Draw(device, level, frustum, (viewProjection, cull) => DrawCasters(device, level, frame, viewProjection, Matrix.Identity, cull));

        foreach (var effect in new[] { deferred, clear, restoreDepth, shading, additive, skinned, water, lava, sky }.Concat(lights.Effects))
        {
            effect.Globals.Apply(globals);
        }

        // Depth prepass into the R32F target.
        UnbindTextures(device);
        device.SetRenderTarget(depth);
        device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.White, 1, 0);
        quad.Draw(device, clear, BlendState.Opaque);
        DrawOpaque(device, level, frame, OpaqueKind.Depth, frustum);

        // G-buffer: RestoreDepth fills Main, Normal and the depth buffer, then the materials.
        UnbindTextures(device);
        device.SetRenderTargets(main, normal);
        device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        var parameters = new FnaEffectParameters(restoreDepth.Effect);
        parameters.SetValue("SourceTexture0", depth);
        parameters.SetValue("Color0", new Vector4(frame.Background, 0));
        parameters.SetValue("Color1", NormalClear);
        quad.Draw(device, restoreDepth, BlendState.Opaque);
        var drawn = DrawOpaque(device, level, frame, OpaqueKind.GBuffer, frustum);

        // The lights into the light accumulator.
        UnbindTextures(device);
        device.SetRenderTarget(light);
        device.Clear(Color.Transparent);
        lights.Accumulate(device, quad, level, frame, frustum, normal!, depth!, shadow, lightShadows);

        // Composition: Main is copied, then shaded back into Main with fog (Main keeps its depth for the additives).
        UnbindTextures(device);
        device.SetRenderTarget(copy);
        Blit(device, main!);
        UnbindTextures(device);
        device.SetRenderTarget(main);
        var shade = new FnaEffectParameters(shading.Effect);
        shade.SetValue("SourceTexture0", copy);
        shade.SetValue("SourceTexture1", normal);
        shade.SetValue("SourceTexture2", depth);
        shade.SetValue("SourceTexture4", light);
        shade.SetValue("FogEnable", frame.Fog.Enabled);
        shade.SetValue("FogStart", frame.Fog.Start);
        shade.SetValue("FogEnd", frame.Fog.End);
        shade.SetValue("FogColor", frame.Fog.Color);
        quad.Draw(device, shading, BlendState.Opaque);
        DrawSky(device, level);
        DrawAdditive(device, level, level.TextureFor);

        UnbindTextures(device);
        device.SetRenderTarget(null);
        Blit(device, main!);
        UnbindTextures(device);
        return drawn;
    }

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

    /// <summary>
    /// Clears the texture slots the passes use, so no target is bound as a texture while it is drawn
    /// into, and restores the wrapping sampler the Blit's sprite batch replaced.
    /// </summary>
    private static void UnbindTextures(GraphicsDevice device)
    {
        for (var slot = 0; slot < 16; slot++)
        {
            device.Textures[slot] = null;
        }
        device.SamplerStates[0] = SamplerState.LinearWrap;
    }

    private void Blit(GraphicsDevice device, Texture2D source)
    {
        sprites.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        var viewport = device.Viewport;
        sprites.Draw(source, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White);
        sprites.End();
    }

    /// <summary>Screen-sized targets, recreated when the back buffer changes size.</summary>
    private void EnsureTargets(GraphicsDevice device)
    {
        var width = device.PresentationParameters.BackBufferWidth;
        var height = device.PresentationParameters.BackBufferHeight;
        if (main is not null && main.Width == width && main.Height == height)
        {
            return;
        }
        DisposeTargets();
        depth = new RenderTarget2D(device, width, height, false, SurfaceFormat.Single, DepthFormat.Depth24Stencil8);
        main = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        normal = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        light = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        copy = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
    }

    private void DisposeTargets()
    {
        foreach (var target in new[] { depth, main, normal, light, copy })
        {
            target?.Dispose();
        }
    }

    public void Dispose()
    {
        DisposeTargets();
        shadowMap?.Dispose();
        lightShadows.Dispose();
        quad.Dispose();
        sprites.Dispose();
        lights.Dispose();
    }
}
