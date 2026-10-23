using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.XnAnimation;

/// <summary>Local bone transform decomposed into translation, rotation and scale.</summary>
public readonly record struct BonePose(Vector3 Translation, Quaternion Orientation, Vector3 Scale);

public readonly record struct AnimationKeyframe(float Time, BonePose Pose);

/// <summary>Keyframes of one bone; expected in increasing time order (not checked on load).</summary>
public sealed record AnimationChannelContent(string BoneName, IReadOnlyList<AnimationKeyframe> Keyframes);

public sealed record AnimationClipContent(string Name, float Duration, IReadOnlyList<AnimationChannelContent> Channels);

/// <summary>A bone of the skeleton; parent and children point into the file's shared resources.</summary>
public sealed record SkinnedModelBoneContent(
    int Index,
    string Name,
    BonePose BindPose,
    Matrix InverseBindPose,
    SharedResourceRef? Parent,
    IReadOnlyList<SharedResourceRef?> Children);

/// <summary>
/// An XNA model plus its skeleton and animation clips. Bones (in bone index order) and clips are
/// shared resources of the same file; resolve them through <c>XnbContent.Resolve</c>.
/// </summary>
public sealed record SkinnedModelContent(
    ModelContent? Model,
    IReadOnlyList<SharedResourceRef?> Bones,
    IReadOnlyList<SharedResourceRef?> Clips);

public sealed record SkinnedModelBasicEffectContent(
    byte Technique,
    float EmissiveAmount,
    Vector3 DiffuseColor,
    float SpecularAmount,
    float SpecularPower,
    float Diffuse1Alpha,
    bool UseSoftLightBlend,
    bool DiffuseMap0Enabled,
    bool DiffuseMap1Enabled,
    bool SpecularMapEnabled,
    bool DamageMap0Enabled,
    bool DamageMap1Enabled,
    bool NormalMapEnabled,
    ContentRef<Texture2DContent> DiffuseMap0,
    ContentRef<Texture2DContent> DiffuseMap1,
    ContentRef<Texture2DContent> SpecularMap,
    ContentRef<Texture2DContent> DamageMap0,
    ContentRef<Texture2DContent> DamageMap1,
    ContentRef<Texture2DContent> NormalMap);
