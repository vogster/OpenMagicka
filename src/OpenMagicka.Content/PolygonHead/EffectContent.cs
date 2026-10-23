using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.PolygonHead;

/// <summary>
/// Main material of static geometry. <see cref="Layer1"/> is blended over <see cref="Layer0"/>
/// by vertex colour when present.
/// </summary>
public sealed record RenderDeferredEffectContent(
    float Alpha,
    float Sharpness,
    bool VertexColorEnabled,
    bool UseMaterialTextureForReflectiveness,
    ExternalReference? ReflectionMap,
    RenderDeferredLayer Layer0,
    RenderDeferredLayer? Layer1);

public sealed record RenderDeferredLayer(
    bool DiffuseTextureAlphaDisabled,
    bool AlphaMaskEnabled,
    Vector3 DiffuseColor,
    float SpecAmount,
    float SpecPower,
    float EmissiveAmount,
    float NormalPower,
    float Reflectiveness,
    ExternalReference? DiffuseTexture,
    ExternalReference? MaterialTexture,
    ExternalReference? NormalTexture);

/// <summary>Water (and its frozen state) used by level liquids.</summary>
public sealed record RenderDeferredLiquidEffectContent(
    ExternalReference? ReflectionMap,
    float WaveHeight,
    Vector2 WaveSpeed0,
    Vector2 WaveSpeed1,
    float WaterReflectiveness,
    Vector3 BottomColor,
    Vector3 DeepBottomColor,
    float WaterEmissiveAmount,
    float WaterSpecAmount,
    float WaterSpecPower,
    ExternalReference? BottomTexture,
    ExternalReference? WaterNormalMap,
    float IceReflectiveness,
    Vector3 IceColor,
    float IceEmissiveAmount,
    float IceSpecAmount,
    float IceSpecPower,
    ExternalReference? IceDiffuseMap,
    ExternalReference? IceNormalMap);

public sealed record LavaEffectContent(
    float MaskDistortion,
    Vector2 Speed0,
    Vector2 Speed1,
    float LavaHotEmissiveAmount,
    float LavaColdEmissiveAmount,
    float LavaSpecAmount,
    float LavaSpecPower,
    float TempFrequency,
    ExternalReference? ToneMap,
    ExternalReference? TempMap,
    ExternalReference? MaskMap,
    Vector3 RockColor,
    float RockEmissiveAmount,
    float RockSpecAmount,
    float RockSpecPower,
    float RockNormalPower,
    ExternalReference? RockTexture,
    ExternalReference? RockNormalMap);

/// <summary>Drawn in the additive pass; the colour tint's alpha is always 1.</summary>
public sealed record AdditiveEffectContent(Vector3 ColorTint, bool VertexColorEnabled, bool TextureEnabled, ExternalReference? Texture);

/// <summary>Normal-mapped skinned material (used by the Cthulhu boss models).</summary>
public sealed record SkinnedModelDeferredNormalMappedEffectContent(
    Vector3 DiffuseColor,
    float SpecularAmount,
    float SpecularPower,
    float EmissiveAmount,
    float NormalPower,
    ExternalReference? DiffuseMap,
    ExternalReference? MaterialMap,
    ExternalReference? DamageMap,
    ExternalReference? NormalMap,
    ExternalReference? NormalDamageMap);
