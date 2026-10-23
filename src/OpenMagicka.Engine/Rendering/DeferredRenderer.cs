using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>What one frame is drawn with besides the geometry.</summary>
public sealed record DeferredFrame(GlobalEffectValues Globals, DirectionalLighting Sun, SceneFog Fog, Vector3 Background);

/// <summary>
/// The original deferred pipeline on the original effects (docs/engine/ARCHITECTURE.md §3.2–3.3):
/// a depth prepass into an R32F target, the G-buffer (Main + Normal, after RestoreDepth), the
/// sun into a light accumulator, the composition with fog, then additive objects, and the result
/// shown on the back buffer, with the level's sun, point and spot lights. Not yet: shadows, decals, sky map, particles,
/// post effects (OM-24 to OM-27). The original composes into Main while reading it; here Main is
/// copied first, since sampling and writing one target is undefined on modern APIs.
/// Render thread only.
/// </summary>
public sealed class DeferredRenderer : IDisposable
{
    public static readonly string[] EffectNames =
        [
            "RenderDeferredEffectWOVT", "ClearEffect", "RestoreDepthEffect", "DirectionalLightEffect", "DeferredShadingEffect", "AdditiveEffect",
            "SkinnedModelDeferredEffect", "PointLightEffect", "SpotLightEffect",
        ];

    /// <summary>
    /// The far plane the projection must use: the original camera always has 500, and the light
    /// shaders rebuild world positions from the linear depth with a view ray scaled by 1/500.
    /// </summary>
    public const float FarPlane = 500;

    private const int WithoutShadows = 1;

    /// <summary>
    /// The original shifts screen lookups by half a pixel, as Direct3D 9 needs to sample texel
    /// centres; FNA follows the later convention where pixel and texel centres already match, so the
    /// shift would sample on texel edges (rows of wrong depth with point sampling). It is set to zero.
    /// </summary>
    private static readonly Vector2 NoHalfPixel = Vector2.Zero;

    /// <summary>How close to a light's sphere the camera may come before its inner side is drawn: more than any near plane the viewer uses.</summary>
    private const float NearMargin = 12;

    /// <summary>Spot light techniques without shadows: with and without distance attenuation.</summary>
    private const int SpotAttenuated = 1;
    private const int SpotUnattenuated = 3;
    private static readonly Vector4 NormalClear = new(0.5f, 0.75f, 0.5f, 0.5f);

    /// <summary>Light accumulation: the original adds lights as One / InvSrcColor (a screen-like sum).</summary>
    private static readonly BlendState LightBlend = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceColor,
    };

    /// <summary>Additive objects: InvDestColor / One.</summary>
    private static readonly BlendState AdditiveBlend = new()
    {
        ColorSourceBlend = Blend.InverseDestinationColor, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.InverseDestinationColor, AlphaDestinationBlend = Blend.One,
    };

    private readonly GameEffect deferred;
    private readonly GameEffect clear;
    private readonly GameEffect restoreDepth;
    private readonly GameEffect sun;
    private readonly GameEffect shading;
    private readonly GameEffect additive;
    private readonly GameEffect skinned;
    private readonly GameEffect point;
    private readonly GameEffect spot;
    private readonly LightMesh sphere;
    private readonly LightMesh cone;
    private readonly Matrix[] bindPose;
    private readonly FullscreenQuad quad;
    private readonly SpriteBatch sprites;
    private readonly BoundingFrustum frustum = new(Matrix.Identity);
    private RenderTarget2D? depth;
    private RenderTarget2D? main;
    private RenderTarget2D? normal;
    private RenderTarget2D? light;
    private RenderTarget2D? copy;

    private DeferredRenderer(GraphicsDevice device, IReadOnlyList<GameEffect> effects)
    {
        (deferred, clear, restoreDepth, sun, shading, additive, skinned, point, spot) =
            (effects[0], effects[1], effects[2], effects[3], effects[4], effects[5], effects[6], effects[7], effects[8]);
        // Characters are shown in their bind pose: every skin transform is the identity.
        bindPose = Enumerable.Repeat(Matrix.Identity, skinned.Effect.Parameters["Bones"]?.Elements.Count ?? 0).ToArray();
        // The techniques are used by index; a different effect fails here, so the viewer falls back.
        if (deferred.Effect.Techniques.Count <= RenderDeferredMaterial.Shadow || sun.Effect.Techniques.Count <= WithoutShadows
            || skinned.Effect.Techniques.Count <= SkinnedDeferredMaterial.Depth || bindPose.Length == 0
            || point.Effect.Techniques.Count <= WithoutShadows || spot.Effect.Techniques.Count <= SpotUnattenuated)
        {
            throw new InvalidDataException("the deferred effects do not have the expected techniques");
        }
        quad = new FullscreenQuad(device);
        try
        {
            sprites = new SpriteBatch(device);
            sphere = new LightMesh(device, LightVolumes.Sphere());
            cone = new LightMesh(device, LightVolumes.Cone());
        }
        catch
        {
            quad.Dispose();
            sprites?.Dispose();
            sphere?.Dispose();
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
        var globals = frame.Globals;
        frustum.Matrix = globals.ViewProjection;
        foreach (var effect in new[] { deferred, clear, restoreDepth, sun, shading, additive, skinned, point, spot })
        {
            effect.Globals.Apply(globals);
        }
        device.RasterizerState = RasterizerState.CullNone;
        UnbindTextures(device);

        // Depth prepass into the R32F target.
        device.SetRenderTarget(depth);
        device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.White, 1, 0);
        DrawFullscreen(device, clear, BlendState.Opaque);
        DrawOpaque(device, level, depthPass: true);

        // G-buffer: RestoreDepth fills Main, Normal and the depth buffer, then the materials.
        UnbindTextures(device);
        device.SetRenderTargets(main, normal);
        device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        var parameters = new FnaEffectParameters(restoreDepth.Effect);
        parameters.SetValue("SourceTexture0", depth);
        parameters.SetValue("Color0", new Vector4(frame.Background, 0));
        parameters.SetValue("Color1", NormalClear);
        DrawFullscreen(device, restoreDepth, BlendState.Opaque);
        var drawn = DrawOpaque(device, level, depthPass: false);

        // The sun into the light accumulator.
        UnbindTextures(device);
        device.SetRenderTarget(light);
        device.Clear(Color.Transparent);
        var sunParameters = new FnaEffectParameters(sun.Effect);
        sunParameters.SetValue("SourceTexture0", normal);
        sunParameters.SetValue("SourceTexture1", depth);
        sunParameters.SetValue("LightDirection", frame.Sun.Direction);
        sunParameters.SetValue("DiffuseColor", frame.Sun.Diffuse);
        sunParameters.SetValue("AmbientColor", frame.Sun.Ambient);
        sunParameters.SetValue("SpecularAmount", frame.Sun.Specular);
        sun.Effect.CurrentTechnique = sun.Effect.Techniques[WithoutShadows];
        DrawFullscreen(device, sun, LightBlend);
        DrawPointLights(device, level.PointLights, globals.CameraPosition);
        DrawSpotLights(device, level.SpotLights, globals.CameraPosition);

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
        DrawFullscreen(device, shading, BlendState.Opaque);
        DrawAdditive(device, level, level.TextureFor);

        UnbindTextures(device);
        device.SetRenderTarget(null);
        Blit(device, main!);
        UnbindTextures(device);
        return drawn;
    }

    private int DrawOpaque(GraphicsDevice device, GpuLevel level, bool depthPass)
    {
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        var parameters = new FnaEffectParameters(deferred.Effect);
        var characters = new FnaEffectParameters(skinned.Effect);
        skinned.Effect.Parameters["Bones"].SetValue(bindPose);
        skinned.Effect.CurrentTechnique = skinned.Effect.Techniques[depthPass ? SkinnedDeferredMaterial.Depth : SkinnedDeferredMaterial.Default];
        var drawn = 0;
        foreach (var batch in level.Batches)
        {
            if (!frustum.Intersects(batch.Bounds))
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
            if (batch.Deferred is not { } material)
            {
                continue;
            }
            RenderDeferredMaterial.Apply(parameters, batch.World, material, level.TextureFor);
            var technique = depthPass ? RenderDeferredMaterial.Depth
                : RenderDeferredMaterial.Technique(material, level.TextureFor(material.ReflectionMap) is not null);
            deferred.Effect.CurrentTechnique = deferred.Effect.Techniques[technique];
            Draw(device, deferred.Effect, batch);
            drawn++;
        }
        return drawn;
    }

    /// <summary>
    /// Each point light whose reach is in view, as its sphere: added like the sun, without depth
    /// testing (the light is computed from the G-buffer per pixel). The outer side is drawn, as in the
    /// original, unless the camera is in or near the sphere (the original's camera never was): then
    /// the inner side, so the light neither vanishes nor loses what the near plane cuts off.
    /// </summary>
    private void DrawPointLights(GraphicsDevice device, IReadOnlyList<PointLighting> lights, Vector3 camera)
    {
        device.BlendState = LightBlend;
        device.DepthStencilState = DepthStencilState.None;
        point.Effect.CurrentTechnique = point.Effect.Techniques[WithoutShadows];
        var parameters = new FnaEffectParameters(point.Effect);
        parameters.SetValue("NormalMap", normal);
        parameters.SetValue("DepthMap", depth);
        parameters.SetValue("HalfPixel", NoHalfPixel);
        sphere.Bind(device);
        foreach (var lamp in lights)
        {
            if (!frustum.Intersects(new BoundingSphere(lamp.Position, lamp.Radius)))
            {
                continue;
            }
            parameters.SetValue("World", lamp.World);
            parameters.SetValue("LightPosition", lamp.Position);
            parameters.SetValue("LightRadius", lamp.Radius);
            parameters.SetValue("DiffuseColor", lamp.Diffuse);
            parameters.SetValue("AmbientColor", lamp.Ambient);
            parameters.SetValue("SpecularAmount", lamp.Specular);
            sphere.Draw(device, point.Effect, Cull(camera, lamp.Position, lamp.Radius));
        }
        device.RasterizerState = RasterizerState.CullNone;
    }

    /// <summary>Each spot light whose reach is in view, as its cone, drawn like the point lights.</summary>
    private void DrawSpotLights(GraphicsDevice device, IReadOnlyList<SpotLighting> lights, Vector3 camera)
    {
        device.BlendState = LightBlend;
        device.DepthStencilState = DepthStencilState.None;
        var parameters = new FnaEffectParameters(spot.Effect);
        parameters.SetValue("NormalMap", normal);
        parameters.SetValue("DepthMap", depth);
        parameters.SetValue("HalfPixel", NoHalfPixel);
        cone.Bind(device);
        foreach (var lamp in lights)
        {
            if (!frustum.Intersects(new BoundingSphere(lamp.Position, lamp.Range)))
            {
                continue;
            }
            spot.Effect.CurrentTechnique = spot.Effect.Techniques[lamp.Attenuation ? SpotAttenuated : SpotUnattenuated];
            parameters.SetValue("World", lamp.World);
            parameters.SetValue("LightPosition", lamp.Position);
            parameters.SetValue("LightDirection", lamp.Direction);
            parameters.SetValue("LightRange", lamp.Range);
            parameters.SetValue("LightCutoffAngle", lamp.Cutoff);
            parameters.SetValue("LightSharpness", lamp.Sharpness);
            parameters.SetValue("DiffuseColor", lamp.Diffuse);
            parameters.SetValue("AmbientColor", lamp.Ambient);
            parameters.SetValue("SpecularAmount", lamp.Specular);
            cone.Draw(device, spot.Effect, Cull(camera, lamp.Position, lamp.Range));
        }
        device.RasterizerState = RasterizerState.CullNone;
    }

    /// <summary>The outer side of a light volume, or the inner one when the camera is in or near it.</summary>
    private static RasterizerState Cull(Vector3 camera, Vector3 light, float reach) =>
        Vector3.Distance(camera, light) < reach + NearMargin ? RasterizerState.CullClockwise : RasterizerState.CullCounterClockwise;


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

    private void DrawFullscreen(GraphicsDevice device, GameEffect effect, BlendState blend)
    {
        device.BlendState = blend;
        device.DepthStencilState = DepthStencilState.None;
        // The effects only use the size for the half-pixel shift (0.5 / size); an infinite size makes it zero.
        new FnaEffectParameters(effect.Effect).SetValue("DestinationDimensions", new Vector2(float.PositiveInfinity));
        quad.Draw(device, effect.Effect);
    }

    /// <summary>
    /// Clears the texture slots the passes use, so no target is bound as a texture while it is drawn
    /// into, and restores the wrapping sampler the Blit's sprite batch replaced.
    /// </summary>
    private static void UnbindTextures(GraphicsDevice device)
    {
        for (var slot = 0; slot < 5; slot++)
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
        quad.Dispose();
        sprites.Dispose();
        sphere.Dispose();
        cone.Dispose();
    }
}

/// <summary>The original's fullscreen quad: a triangle strip at (±1.5, ±1.5, 1) whose texture coordinates map (±1, ±1) to 0…1.</summary>
internal sealed class FullscreenQuad : IDisposable
{
    private readonly VertexBuffer vertices;

    public FullscreenQuad(GraphicsDevice device)
    {
        vertices = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
        vertices.SetData(new[]
        {
            new VertexPositionTexture(new Vector3(1.5f, 1.5f, 1), new Vector2(1.25f, -0.25f)),
            new VertexPositionTexture(new Vector3(1.5f, -1.5f, 1), new Vector2(1.25f, 1.25f)),
            new VertexPositionTexture(new Vector3(-1.5f, 1.5f, 1), new Vector2(-0.25f, -0.25f)),
            new VertexPositionTexture(new Vector3(-1.5f, -1.5f, 1), new Vector2(-0.25f, 1.25f)),
        });
    }

    public void Draw(GraphicsDevice device, Effect effect)
    {
        device.SetVertexBuffer(vertices);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }
    }

    public void Dispose() => vertices.Dispose();
}
