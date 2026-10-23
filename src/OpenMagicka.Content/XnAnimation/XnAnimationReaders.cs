using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.XnAnimation;

/// <summary>XNAnimation 0.7 content readers. Layouts: docs/formats/XNB_READERS.md §2.</summary>
internal static class XnAnimationReaders
{
    // Counts are i32 in XNAnimation; ReadCount reads them as u32, so a negative count is rejected.
    // time + translation + orientation + scale
    private const int KeyframeSize = 4 + 12 + 16 + 12;

    public static IReadOnlyDictionary<string, XnbTypeReaderFunc> All { get; } = new Dictionary<string, XnbTypeReaderFunc>
    {
        ["SkinnedModelReader"] = ReadSkinnedModel,
        ["SkinnedModelBoneReader"] = ReadBone,
        ["AnimationClipReader"] = ReadClip,
        ["SkinnedModelBasicEffectReader"] = ReadBasicEffect,
    };

    /// <summary>Shared with Magicka's animated level parts, which store channels the same way.</summary>
    public static IReadOnlyList<AnimationKeyframe> ReadKeyframes(XnbContentReader r)
    {
        var keyframes = new AnimationKeyframe[r.ReadCount(KeyframeSize)];
        for (var i = 0; i < keyframes.Length; i++)
        {
            keyframes[i] = new AnimationKeyframe(r.ReadSingle(), ReadPose(r));
        }
        return keyframes;
    }

    private static SkinnedModelContent ReadSkinnedModel(XnbContentReader r)
    {
        var model = r.ReadObject<ModelContent>();
        var bones = ReadSharedRefs(r);
        var clips = ReadSharedRefs(r);
        return new SkinnedModelContent(model, bones, clips);
    }

    private static SkinnedModelBoneContent ReadBone(XnbContentReader r)
    {
        var index = r.ReadUInt16();
        var name = r.ReadString();
        var pose = ReadPose(r);
        var inverseBindPose = r.ReadMatrix();
        var parent = r.ReadSharedResourceRef();
        var children = ReadSharedRefs(r);
        return new SkinnedModelBoneContent(index, name, pose, inverseBindPose, parent, children);
    }

    private static AnimationClipContent ReadClip(XnbContentReader r)
    {
        var name = r.ReadString();
        var duration = r.ReadSingle();
        var channels = new AnimationChannelContent[r.ReadCount(1 + 4)];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = new AnimationChannelContent(r.ReadString(), ReadKeyframes(r));
        }
        return new AnimationClipContent(name, duration, channels);
    }

    private static SkinnedModelBasicEffectContent ReadBasicEffect(XnbContentReader r) => new(
        Technique: r.ReadByte(),
        EmissiveAmount: r.ReadSingle(),
        DiffuseColor: r.ReadVector3(),
        SpecularAmount: r.ReadSingle(),
        SpecularPower: r.ReadSingle(),
        Diffuse1Alpha: r.ReadSingle(),
        UseSoftLightBlend: r.ReadBoolean(),
        DiffuseMap0Enabled: r.ReadBoolean(),
        DiffuseMap1Enabled: r.ReadBoolean(),
        SpecularMapEnabled: r.ReadBoolean(),
        DamageMap0Enabled: r.ReadBoolean(),
        DamageMap1Enabled: r.ReadBoolean(),
        NormalMapEnabled: r.ReadBoolean(),
        DiffuseMap0: r.ReadContentRef<Texture2DContent>(),
        DiffuseMap1: r.ReadContentRef<Texture2DContent>(),
        SpecularMap: r.ReadContentRef<Texture2DContent>(),
        DamageMap0: r.ReadContentRef<Texture2DContent>(),
        DamageMap1: r.ReadContentRef<Texture2DContent>(),
        NormalMap: r.ReadContentRef<Texture2DContent>());

    private static BonePose ReadPose(XnbContentReader r) => new(r.ReadVector3(), r.ReadQuaternion(), r.ReadVector3());

    private static SharedResourceRef?[] ReadSharedRefs(XnbContentReader r)
    {
        var refs = new SharedResourceRef?[r.ReadCount(1)];
        for (var i = 0; i < refs.Length; i++)
        {
            refs[i] = r.ReadSharedResourceRef();
        }
        return refs;
    }
}
