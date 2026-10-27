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
        var effects = new OpaqueEffects(new FnaEffectParameters(deferred.Effect), new FnaEffectParameters(skinned.Effect),
            new FnaEffectParameters(water.Effect), new FnaEffectParameters(lava.Effect));
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        skinned.Effect.CurrentTechnique = skinned.Effect.Techniques[pass switch
        {
            OpaqueKind.Shadow => SkinnedDeferredMaterial.Shadow,
            OpaqueKind.Depth => SkinnedDeferredMaterial.Depth,
            _ => SkinnedDeferredMaterial.Default,
        }];
        var drawn = 0;
        var rim = frame.RimLight ?? CharacterRimLight.Default;
        effects.Characters.SetValue("RimLightGlow", rim.Glow);
        effects.Characters.SetValue("RimLightPower", rim.Power);
        effects.Characters.SetValue("RimLightBias", rim.Bias);
        skinned.Effect.Parameters["Bones"].SetValue(bindPose);
        foreach (var batch in level.Batches)
        {
            var move = Move(batch);
            if (view.Intersects(Transformed(batch.Bounds, move)) && DrawOpaque(device, level, batch, batch.World * move, frame, pass, effects))
            {
                drawn++;
            }
        }
        foreach (var instance in frame.Models ?? [])
        {
            skinned.Effect.Parameters["Bones"].SetValue(Palette(instance));
            foreach (var batch in instance.Drawn)
            {
                var posed = instance.Bones is not null && batch.Material is SkinnedModelBasicEffectContent;   // its pose may be far from its bind pose
                if ((posed || view.Intersects(Transformed(batch.Bounds, instance.World))) && DrawOpaque(device, instance.Model, batch, batch.World * instance.World, frame, pass,
                    effects, instance.Tint, instance.Emissive))
                {
                    drawn++;
                }
            }
        }
        return drawn;
    }

    /// <summary>The effects' parameters the opaque passes set.</summary>
    private readonly record struct OpaqueEffects(FnaEffectParameters Deferred, FnaEffectParameters Characters, FnaEffectParameters Water,
        FnaEffectParameters Lava);

    /// <summary>One opaque batch at a place; false when it is not drawn with these passes (an additive one).</summary>
    private bool DrawOpaque(GraphicsDevice device, GpuLevel source, GpuBatch batch, Matrix world, DeferredFrame frame, OpaqueKind pass, OpaqueEffects effects,
        Vector3? tint = null, float? emissive = null)
    {
        if (batch.Material is SkinnedModelBasicEffectContent character && !batch.Additive)
        {
            SkinnedDeferredMaterial.Apply(effects.Characters, character, source.TextureFor);
            if (emissive is { } glow)
            {
                effects.Characters.SetValue("EmissiveAmount", glow);
            }
            if (tint is { } colour)
            {
                effects.Characters.SetValue("TintColor", colour);
            }
            Draw(device, skinned.Effect, batch);
            return true;
        }
        if (batch.Material is RenderDeferredLiquidEffectContent or LavaEffectContent)
        {
            var (effect, liquid) = batch.Material is LavaEffectContent ? (lava, effects.Lava) : (water, effects.Water);
            LiquidMaterial.Apply(liquid, batch.Material, world, frame.Seconds, source.TextureFor);
            var reflects = batch.Material is RenderDeferredLiquidEffectContent w && source.TextureFor(w.ReflectionMap) is not null;
            effect.Effect.CurrentTechnique = effect.Effect.Techniques[LiquidMaterial.Technique(batch.Material, reflects, pass)];
            Draw(device, effect.Effect, batch);
            return true;
        }
        if (batch.Deferred is not { } material)
        {
            return false;
        }
        RenderDeferredMaterial.Apply(effects.Deferred, world, material, source.TextureFor);
        var technique = pass switch
        {
            OpaqueKind.Shadow => RenderDeferredMaterial.Shadow,
            OpaqueKind.Depth => RenderDeferredMaterial.Depth,
            _ => RenderDeferredMaterial.Technique(material, source.TextureFor(material.ReflectionMap) is not null),
        };
        deferred.Effect.CurrentTechnique = deferred.Effect.Techniques[technique];
        Draw(device, deferred.Effect, batch);
        return true;
    }

    /// <summary>
    /// A skinned instance's bone palette, as long as the effect takes: its bones (already in the world),
    /// or with none the model's bind pose at its place; bones past the effect's are left out, and the rest
    /// filled with the place.
    /// </summary>
    private Matrix[] Palette(ModelInstance instance)
    {
        var count = instance.Bones is { } bones ? Math.Min(bones.Length, instancePalette.Length) : 0;
        for (var i = 0; i < instancePalette.Length; i++)
        {
            instancePalette[i] = i < count ? instance.Bones![i] : instance.World;
        }
        return instancePalette;
    }

    /// <summary>The frame's moves of the level's parts from their rest poses (<see cref="LevelParts.Moves"/>).</summary>
    private void PlaceParts(GpuLevel level, DeferredFrame frame)
    {
        if (partMoves.Length != level.PartUnrests.Count)
        {
            partMoves = new Matrix[level.PartUnrests.Count];
        }
        LevelParts.Moves(level.PartUnrests, frame.Parts, partMoves);
    }

    /// <summary>How far a level batch's part has moved this frame; identity for the level's own geometry.</summary>
    private Matrix Move(GpuBatch batch) => batch.Part >= 0 && batch.Part < partMoves.Length ? partMoves[batch.Part] : Matrix.Identity;

    /// <summary>The box around a box placed by a transform.</summary>
    internal static BoundingBox Transformed(BoundingBox box, Matrix world)
    {
        if (world == Matrix.Identity)
        {
            return box;
        }
        var corners = box.GetCorners();
        Vector3.Transform(corners, ref world, corners);
        return BoundingBox.CreateFromPoints(corners);
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

    private void DrawAdditive(GraphicsDevice device, GpuLevel level, Func<Content.Xna.ExternalReference?, Texture?> textures,
        IReadOnlyList<ModelInstance>? frameModels = null)
    {
        device.BlendState = AdditiveBlend;
        device.DepthStencilState = DepthStencilState.DepthRead;
        var parameters = new FnaEffectParameters(additive.Effect);
        foreach (var (source, batch, place) in level.Batches.Select(b => (level, b, Move(b)))
            .Concat((frameModels ?? []).SelectMany(m => m.Drawn.Select(b => (m.Model, b, m.World)))))
        {
            var world = batch.World * place;
            if (!batch.Additive || batch.Material is not AdditiveEffectContent material || !frustum.Intersects(Transformed(batch.Bounds, place)))
            {
                continue;
            }
            var texture = material.TextureEnabled ? (ReferenceEquals(source, level) ? textures(material.Texture) : source.TextureFor(material.Texture)) : null;
            parameters.SetValue("World", world);
            parameters.SetValue("TextureScale", Vector2.One);
            parameters.SetValue("TextureOffset", Vector2.Zero);
            parameters.SetValue("ColorTint", new Vector4(material.ColorTint, 1));   // the original reads the tint with alpha 1
            parameters.SetValue("VertexColorEnabled", material.VertexColorEnabled);
            parameters.SetValue("TextureEnabled", texture is not null);
            parameters.SetValue("Texture", texture);
            Draw(device, additive.Effect, batch);
        }
    }

    /// <summary>The level's batches a frame's highlights light: the deferred meshes of the parts that glow (not their additive or liquid ones).</summary>
    internal static IEnumerable<GpuBatch> Highlighted(IEnumerable<GpuBatch> batches, IReadOnlyList<bool> glowing) =>
        batches.Where(b => b.Deferred is not null && b.Part >= 0 && b.Part < glowing.Count && glowing[b.Part]);

    /// <summary>
    /// The parts a script highlights (highlightAnim, OM-86) drawn again as the original's, with the additives: their
    /// deferred meshes in the effect's additive fresnel (white, power 1, no bloat), a soft rim brightening what lies
    /// behind, constant while it lasts; the faces turned away culled, depth read and not written.
    /// </summary>
    private void DrawHighlights(GraphicsDevice device, GpuLevel level, DeferredFrame frame)
    {
        var effect = deferred.Effect;
        highlightable ??= effect.Techniques.Count > RenderDeferredMaterial.AdditiveFresnel && HighlightParameters.All(n => effect.Parameters[n] is not null);
        if (frame.Highlighted is not { } glowing || !glowing.Contains(true) || highlightable is not true)
        {
            return;                                                           // an effect without the glow: none drawn
        }
        device.BlendState = AdditiveBlend;
        device.DepthStencilState = DepthStencilState.DepthRead;
        var (cull, technique) = (device.RasterizerState, effect.CurrentTechnique);
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        effect.CurrentTechnique = effect.Techniques[RenderDeferredMaterial.AdditiveFresnel];
        var parameters = new FnaEffectParameters(effect);
        parameters.SetValue("DiffuseColor0", Vector3.One);
        parameters.SetValue("FresnelPower", 1f);
        parameters.SetValue("Bloat", 0f);
        foreach (var batch in Highlighted(level.Batches, glowing))
        {
            var move = Move(batch);
            if (frustum.Intersects(Transformed(batch.Bounds, move)))
            {
                parameters.SetValue("World", batch.World * move);
                Draw(device, effect, batch);
            }
        }
        effect.CurrentTechnique = technique;
        device.RasterizerState = cull;
    }

    private bool? highlightable;

    /// <summary>The parameters the highlight sets on the deferred effect (checked once: an effect without them draws no glow).</summary>
    internal static readonly string[] HighlightParameters = ["World", "DiffuseColor0", "FresnelPower", "Bloat"];

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
