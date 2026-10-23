using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// How a character's material (<c>SkinnedModelBasicEffect</c> in its model) feeds the original
/// <c>SkinnedModelDeferredEffect</c>, as the game draws a character in the G-buffer: white diffuse
/// colour, the base map (1) under the detail map (0) with soft-light blending, the specular map as
/// the material map, no damage, colourising or overlay effects. The bones are set by the renderer.
/// </summary>
public static class SkinnedDeferredMaterial
{
    public const int Default = 0;
    public const int Depth = 3;
    public const int Shadow = 4;

    /// <summary>The textures the material uses: each map whose flag is set.</summary>
    public static IEnumerable<ExternalReference> References(SkinnedModelBasicEffectContent material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return new[]
        {
            material.DiffuseMap0Enabled ? material.DiffuseMap0.External : null,
            material.DiffuseMap1Enabled ? material.DiffuseMap1.External : null,
            material.SpecularMapEnabled ? material.SpecularMap.External : null,
            material.NormalMapEnabled ? material.NormalMap.External : null,
        }.OfType<ExternalReference>();
    }

    public static void Apply(IEffectParameters effect, SkinnedModelBasicEffectContent material, Func<ExternalReference?, Texture?> textures)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(textures);
        effect.SetValue("DiffuseColor", Vector3.One);
        effect.SetValue("TintColor", Vector3.One);
        effect.SetValue("EmissiveAmount", material.EmissiveAmount);
        effect.SetValue("SpecularAmount", material.SpecularAmount);
        effect.SetValue("SpecularPower", material.SpecularPower);
        effect.SetValue("Alpha", 1f);
        effect.SetValue("Damage", 0f);
        effect.SetValue("OverlayAlpha", material.Diffuse1Alpha);
        effect.SetValue("UseSoftLightBlend", material.UseSoftLightBlend);
        effect.SetValue("FresnelPower", 4f);
        effect.SetValue("Colorize", Vector4.Zero);
        effect.SetValue("DamageMap0Enabled", false);
        effect.SetValue("DamageMap1Enabled", false);
        Map("DiffuseMap0", "DiffuseMap0Enabled", material.DiffuseMap0Enabled, material.DiffuseMap0);
        Map("DiffuseMap1", "DiffuseMap1Enabled", material.DiffuseMap1Enabled, material.DiffuseMap1);
        Map("MaterialMap", "SpecularMapEnabled", material.SpecularMapEnabled, material.SpecularMap);
        Map("NormalMap", "NormalMapEnabled", material.NormalMapEnabled, material.NormalMap);

        void Map(string name, string flag, bool enabled, ContentRef<Texture2DContent> map)
        {
            var texture = enabled ? textures(map.External) : null;
            effect.SetValue(name, texture);
            effect.SetValue(flag, texture is not null);
        }
    }
}
