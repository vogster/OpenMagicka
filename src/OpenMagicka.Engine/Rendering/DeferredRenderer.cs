using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// What one frame is drawn with besides the geometry; <see cref="ShadowFocus"/> is where the sun's
/// shadows are wanted (see <see cref="SunShadowView.Focus"/>), <see cref="Seconds"/> the time the
/// liquids' waves and flow have run, <see cref="Parts"/> where the level's animated parts are now (by
/// <see cref="LevelParts.Order"/>; none, or one missing: at rest).
/// </summary>
public sealed record DeferredFrame(GlobalEffectValues Globals, DirectionalLighting Sun, SceneFog Fog, Vector3 Background, Vector3 ShadowFocus,
    float Seconds = 0, Particles.ParticlePool? Particles = null, PostProcessing? Post = null, IReadOnlyList<ModelInstance>? Models = null,
    CharacterRimLight? RimLight = null, IReadOnlyList<Matrix>? Parts = null);

/// <summary>
/// The light on the characters' edges a scene gives the skinned effect (its <c>&lt;RimLight&gt;</c>), as the
/// original sets it when the scene starts; the original's values before a scene sets its own by default.
/// </summary>
public sealed record CharacterRimLight(float Glow, float Power, float Bias)
{
    public static CharacterRimLight Default { get; } = new(0.8f, 1.5f, 0.2f);

    public static CharacterRimLight For(Content.Levels.SceneSettings? settings) =>
        settings?.RimLight is { } rim ? new(rim.Glow, rim.Power, rim.Bias) : Default;
}

/// <summary>
/// A model drawn at a place this frame (a character, a prop, an item): its batches placed by the world
/// matrix; a skinned one posed by its bones, which are in the world already (none: its bind pose at the
/// place). The tint colours a character's skin.
/// </summary>
public sealed record ModelInstance(GpuLevel Model, Matrix World, Matrix[]? Bones = null, Vector3? Tint = null);

/// <summary>
/// The original deferred pipeline on the original effects (docs/engine/ARCHITECTURE.md §3.2–3.3):
/// the sun's shadow map, a depth prepass into an R32F target, the G-buffer (Main + Normal, after
/// RestoreDepth), the lights into a light accumulator (<see cref="LightPass"/>), the composition
/// with fog, then additive objects, and the result shown on the back buffer. Shadow maps are drawn
/// first: the sun's, then the shadow-casting point and spot lights' (<see cref="LightShadows"/>).
/// Water and lava are drawn with the opaque objects (<see cref="LiquidMaterial"/>), the scene's sky
/// after the composition, the particles last (<see cref="ParticlePass"/>), then the filter and bloom onto the back
/// buffer (<see cref="PostPass"/>). Not yet: decals (OM-66), the scenes' own post effects. The original
/// composes into Main while reading it; here Main is copied first, since sampling and writing one
/// target is undefined on modern APIs. Render thread only.
/// </summary>
public sealed partial class DeferredRenderer : IDisposable
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
    private readonly Matrix[] instancePalette;
    private readonly GameEffect water;
    private readonly GameEffect lava;
    private readonly GameEffect sky;
    private readonly LightPass lights;
    private readonly ParticlePass? particles;
    private readonly PostPass? post;
    private readonly LightShadows lightShadows = new();
    private Matrix[] partMoves = [];
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

    /// <summary>The particles' effect and sprite sheets, owned by the renderer once it is built.</summary>
    private sealed record ParticleResources(GameEffect Effect, Texture3D[] Sheets);

    private DeferredRenderer(GraphicsDevice device, IReadOnlyList<GameEffect> effects, ParticleResources? particleResources, GameEffect[]? postEffects)
    {
        (deferred, clear, restoreDepth, shading, additive, skinned) = (effects[0], effects[1], effects[2], effects[4], effects[5], effects[6]);
        (water, lava, sky) = (effects[9], effects[10], effects[11]);
        // Characters are shown in their bind pose: every skin transform is the identity.
        bindPose = Enumerable.Repeat(Matrix.Identity, skinned.Effect.Parameters["Bones"]?.Elements.Count ?? 0).ToArray();
        instancePalette = new Matrix[bindPose.Length];
        // The techniques are used by index; a different effect fails here, so the viewer falls back.
        if (deferred.Effect.Techniques.Count <= RenderDeferredMaterial.Shadow || skinned.Effect.Techniques.Count <= SkinnedDeferredMaterial.Shadow
            || water.Effect.Techniques.Count <= LiquidMaterial.Shadow || lava.Effect.Techniques.Count <= LiquidMaterial.LavaShadow
            || bindPose.Length == 0
            || !new[] { "SourceTexture0", "SourceDimensions", "Color", "DestinationDimensions" }.All(new FnaEffectParameters(sky.Effect).Contains))
        {
            throw new InvalidDataException("the deferred effects do not have the expected techniques");
        }
        if (postEffects is not null)
        {
            try
            {
                post = new PostPass(postEffects[0], postEffects[1]);
            }
            catch (InvalidDataException e)
            {
                PostUnavailable = e.Message;
            }
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
        if (particleResources is not null)
        {
            try
            {
                particles = new ParticlePass(device, particleResources.Effect, particleResources.Sheets);
            }
            catch (InvalidDataException e)
            {
                // A different particle effect: everything else still draws.
                ParticlesUnavailable = e.Message;
                Array.ForEach(particleResources.Sheets, s => s.Dispose());
            }
        }
    }

    /// <summary>Why the particles are not drawn, when they were asked for but could not be set up.</summary>
    public string? ParticlesUnavailable { get; private set; }

    /// <summary>Why the filter and bloom are not applied (the scene is then shown as it is), or null.</summary>
    public string? PostUnavailable { get; private set; }

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
        PlaceParts(level, frame);
        var lit = PartLight.Place(level, frame.Parts);
        frame = frame with { Sun = lit.Sun(frame.Sun) };
        var shadow = DrawSunShadow(device, level, frame);
        lightShadows.Draw(device, lit, frustum, (viewProjection, cull) => DrawCasters(device, level, frame, viewProjection, Matrix.Identity, cull));

        foreach (var effect in new[] { deferred, clear, restoreDepth, shading, additive, skinned, water, lava, sky }.Concat(lights.Effects).Concat(post?.Effects ?? []))
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
        lights.Accumulate(device, quad, lit, frame, frustum, normal!, depth!, shadow, lightShadows);

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
        DrawAdditive(device, level, level.TextureFor, frame.Models);
        if (particles is not null && frame.Particles is { } pool)
        {
            particles.Draw(device, pool, globals.View, globals.Projection, depth!);
        }

        UnbindTextures(device);
        if (post is not null)
        {
            post.Draw(device, quad, main!, frame.Post ?? PostProcessing.Default);
        }
        else
        {
            device.SetRenderTarget(null);
            Blit(device, main!);
        }
        UnbindTextures(device);
        return drawn;
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
        particles?.Dispose();
        post?.Dispose();
    }
}
