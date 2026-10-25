using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The sun's shadow map for this frame and the view it was drawn from.</summary>
internal sealed record SunShadow(Texture2D Map, SunShadowView View);

/// <summary>
/// The light accumulation of the deferred pipeline (docs/engine/ARCHITECTURE.md §3.2): the sun as a
/// fullscreen pass (with its shadow map when it has one), then each point light's sphere and each
/// spot light's cone, all added as One / InvSrcColor into the light target. Render thread only.
/// </summary>
internal sealed class LightPass : IDisposable
{
    private const int WithShadows = 0;
    private const int WithoutShadows = 1;

    /// <summary>Spot light techniques: with or without shadows, with or without distance attenuation.</summary>
    private const int SpotShadowedAttenuated = 0;
    private const int SpotAttenuated = 1;
    private const int SpotShadowedUnattenuated = 2;
    private const int SpotUnattenuated = 3;

    /// <summary>How close to a light's volume the camera may come before its inner side is drawn: more than any near plane the viewer uses.</summary>
    private const float NearMargin = 12;

    /// <summary>
    /// The original shifts screen lookups by half a pixel, as Direct3D 9 needs to sample texel
    /// centres; FNA follows the later convention where pixel and texel centres already match, so the
    /// shift would sample on texel edges (rows of wrong depth with point sampling). It is set to zero.
    /// </summary>
    private static readonly Vector2 NoHalfPixel = Vector2.Zero;

    /// <summary>Light accumulation: the original adds lights as One / InvSrcColor (a screen-like sum).</summary>
    private static readonly BlendState LightBlend = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceColor,
    };

    private static readonly string[] SunParameters =
    [
        "SourceTexture0", "SourceTexture1", "SourceTexture3", "LightDirection", "DiffuseColor", "AmbientColor", "SpecularAmount",
        "ProjectedTextureEnabled", "ProjectedTextureIntensity", "ShadowMapSize", "LightViewProjection", "SnappedGroundPosition",
    ];

    private readonly GameEffect sun;
    private readonly GameEffect point;
    private readonly GameEffect spot;
    private readonly LightMesh sphere;
    private readonly LightMesh cone;

    public LightPass(GraphicsDevice device, GameEffect sun, GameEffect point, GameEffect spot)
    {
        // The techniques are used by index; a different effect fails here, so the viewer falls back.
        if (sun.Effect.Techniques.Count <= WithoutShadows || point.Effect.Techniques.Count <= WithoutShadows
            || spot.Effect.Techniques.Count <= SpotUnattenuated)
        {
            throw new InvalidDataException("the light effects do not have the expected techniques");
        }
        EffectChecks.Require(sun, SunParameters);
        EffectChecks.Require(point, ["ShadowMap"]);
        EffectChecks.Require(spot, ["ShadowMap", "ShadowMapSize", "LightViewProjection"]);
        (this.sun, this.point, this.spot) = (sun, point, spot);
        sphere = new LightMesh(device, LightVolumes.Sphere());
        try
        {
            cone = new LightMesh(device, LightVolumes.Cone());
        }
        catch
        {
            sphere.Dispose();
            throw;
        }
    }

    public IEnumerable<GameEffect> Effects => [sun, point, spot];

    /// <summary>Adds every light into the bound light target; the G-buffer's normals and depth are read.</summary>
    public void Accumulate(GraphicsDevice device, FullscreenQuad quad, FrameLights lit, DeferredFrame frame, BoundingFrustum view,
        Texture2D normal, Texture2D depth, SunShadow? shadow, LightShadows shadows)
    {
        DrawSun(device, quad, frame.Sun, normal, depth, shadow);
        DrawPointLights(device, lit.Points, view, frame.Globals.CameraPosition, normal, depth, shadows);
        DrawSpotLights(device, lit.Spots, view, frame.Globals.CameraPosition, normal, depth, shadows);
    }

    private void DrawSun(GraphicsDevice device, FullscreenQuad quad, DirectionalLighting light, Texture2D normal, Texture2D depth, SunShadow? shadow)
    {
        var parameters = new FnaEffectParameters(sun.Effect);
        parameters.SetValue("SourceTexture0", normal);
        parameters.SetValue("SourceTexture1", depth);
        parameters.SetValue("LightDirection", light.Direction);
        parameters.SetValue("DiffuseColor", light.Diffuse);
        parameters.SetValue("AmbientColor", light.Ambient);
        parameters.SetValue("SpecularAmount", light.Specular);
        // Cloud shadows (a projected texture) are not used by the levels' suns.
        parameters.SetValue("ProjectedTextureEnabled", false);
        parameters.SetValue("ProjectedTextureIntensity", 0f);
        if (shadow is not null)
        {
            parameters.SetValue("SourceTexture3", shadow.Map);
            parameters.SetValue("ShadowMapSize", (float)shadow.Map.Width);
            parameters.SetValue("LightViewProjection", shadow.View.LightViewProjection);
            parameters.SetValue("SnappedGroundPosition", shadow.View.SnappedGround);
        }
        sun.Effect.CurrentTechnique = sun.Effect.Techniques[shadow is null ? WithoutShadows : WithShadows];
        quad.Draw(device, sun, LightBlend);
    }

    /// <summary>
    /// Each point light whose reach is in view, as its sphere: added like the sun, without depth
    /// testing (the light is computed from the G-buffer per pixel). The outer side is drawn, as in the
    /// original, unless the camera is in or near the sphere (the original's camera never was): then
    /// the inner side, so the light neither vanishes nor loses what the near plane cuts off.
    /// </summary>
    private void DrawPointLights(GraphicsDevice device, IReadOnlyList<PointLighting> lights, BoundingFrustum view, Vector3 camera,
        Texture2D normal, Texture2D depth, LightShadows shadows)
    {
        device.BlendState = LightBlend;
        device.DepthStencilState = DepthStencilState.None;
        var parameters = new FnaEffectParameters(point.Effect);
        parameters.SetValue("NormalMap", normal);
        parameters.SetValue("DepthMap", depth);
        parameters.SetValue("HalfPixel", NoHalfPixel);
        sphere.Bind(device);
        foreach (var lamp in lights)
        {
            if (!view.Intersects(new BoundingSphere(lamp.Position, lamp.Radius)))
            {
                continue;
            }
            var shadow = shadows.For(lamp);
            point.Effect.CurrentTechnique = point.Effect.Techniques[shadow is null ? WithoutShadows : WithShadows];
            parameters.SetValue("ShadowMap", shadow);
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
    private void DrawSpotLights(GraphicsDevice device, IReadOnlyList<SpotLighting> lights, BoundingFrustum view, Vector3 camera,
        Texture2D normal, Texture2D depth, LightShadows shadows)
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
            if (!view.Intersects(new BoundingSphere(lamp.Position, lamp.Range)))
            {
                continue;
            }
            var shadow = shadows.For(lamp);
            spot.Effect.CurrentTechnique = spot.Effect.Techniques[(shadow is null, lamp.Attenuation) switch
            {
                (true, true) => SpotAttenuated,
                (true, false) => SpotUnattenuated,
                (false, true) => SpotShadowedAttenuated,
                (false, false) => SpotShadowedUnattenuated,
            }];
            if (shadow is { } map)
            {
                parameters.SetValue("ShadowMap", map.Map);
                parameters.SetValue("ShadowMapSize", (float)map.Map.Width);
                parameters.SetValue("LightViewProjection", map.View);
            }
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

    public void Dispose()
    {
        sphere.Dispose();
        cone.Dispose();
    }
}
