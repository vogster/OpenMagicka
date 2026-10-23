using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// How a <c>RenderDeferredEffect</c> material (level geometry, props) feeds the original effect
/// (docs/engine/ARCHITECTURE.md §3.2): its technique and parameters. The textures come from a
/// lookup, so this stays testable without a GPU.
/// </summary>
public static class RenderDeferredMaterial
{
    public const int SingleLayer = 0;
    public const int DualLayer = 1;
    public const int SingleLayerReflection = 2;
    public const int DualLayerReflection = 3;
    public const int Depth = 4;
    public const int Shadow = 5;

    /// <summary>The G-buffer technique: with a reflection map the reflection variants; a second diffuse texture makes it dual.</summary>
    public static int Technique(RenderDeferredEffectContent material, bool hasReflectionMap)
    {
        ArgumentNullException.ThrowIfNull(material);
        var dual = material.Layer1?.DiffuseTexture is not null;
        return hasReflectionMap ? dual ? DualLayerReflection : SingleLayerReflection : dual ? DualLayer : SingleLayer;
    }

    /// <summary>Sets the world matrix and every material parameter; a texture's flag says whether one was bound.</summary>
    public static void Apply(IEffectParameters effect, Matrix world, RenderDeferredEffectContent material, Func<ExternalReference?, Texture?> textures)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(textures);
        effect.SetValue("World", world);
        effect.SetValue("Alpha", material.Alpha);
        effect.SetValue("Sharpness", material.Sharpness);
        effect.SetValue("VertexColorEnabled", material.VertexColorEnabled);
        effect.SetValue("UseMaterialTextureForReflectiveness", material.UseMaterialTextureForReflectiveness);
        effect.SetValue("ReflectionMap", textures(material.ReflectionMap));
        Layer(effect, 0, material.Layer0, textures);
        Layer(effect, 1, material.Layer1, textures);
    }

    /// <summary>
    /// A single-layer material for a batch whose own effect the G-buffer pass does not have yet
    /// (plain models): its diffuse texture and colour.
    /// </summary>
    public static RenderDeferredEffectContent StandIn(ExternalReference? diffuse, Vector3 diffuseColor)
    {
        var layer = new RenderDeferredLayer(false, false, diffuseColor, 0, 0, 0, 0, 0, diffuse, null, null);
        return new RenderDeferredEffectContent(1, 0, false, false, null, layer, null);
    }

    /// <summary>A missing layer is set to zeros and no textures, as the original's empty material is.</summary>
    private static void Layer(IEffectParameters effect, int n, RenderDeferredLayer? layer, Func<ExternalReference?, Texture?> textures)
    {
        effect.SetValue($"DiffuseColor{n}", layer?.DiffuseColor ?? Vector3.Zero);
        effect.SetValue($"SpecAmount{n}", layer?.SpecAmount ?? 0);
        effect.SetValue($"SpecPower{n}", layer?.SpecPower ?? 0);
        effect.SetValue($"EmissiveAmount{n}", layer?.EmissiveAmount ?? 0);
        effect.SetValue($"NormalPower{n}", layer?.NormalPower ?? 0);
        effect.SetValue($"Reflectiveness{n}", layer?.Reflectiveness ?? 0);
        effect.SetValue($"DiffuseTexture{n}AlphaDisabled", layer?.DiffuseTextureAlphaDisabled ?? false);
        effect.SetValue($"AlphaMask{n}Enabled", layer?.AlphaMaskEnabled ?? false);
        Texture($"DiffuseTexture{n}", $"DiffuseTexture{n}Enabled", layer?.DiffuseTexture);
        Texture($"MaterialTexture{n}", $"MaterialTexture{n}Enabled", layer?.MaterialTexture);
        Texture($"NormalTexture{n}", $"NormalMap{n}Enabled", layer?.NormalTexture);

        void Texture(string name, string flag, ExternalReference? reference)
        {
            var texture = reference is null ? null : textures(reference);
            effect.SetValue(name, texture);
            effect.SetValue(flag, texture is not null);
        }
    }
}
