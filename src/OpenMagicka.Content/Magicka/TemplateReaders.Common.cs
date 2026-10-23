using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

/// <summary>
/// Readers of Magicka's entity templates. Layouts: docs/formats/XNB_READERS.md §5.
/// Enum values are kept as stored, as the original casts them; only data the original itself
/// fails to load is rejected (unknown class or event names, too many event collections or
/// character lights, physics entity lights, Blast and Callback events). Values the original
/// adjusts after loading (minimum lengths, defaults for zero) are kept raw.
/// </summary>
internal static partial class TemplateReaders
{
    private const int WeaponClasses = 27;

    // Minimum encoded sizes of list items, used to bound allocations.
    private const int DamageSize = 16;
    private const int ResistanceSize = 13;
    private const int PointLightSize = 4 + 12 + 12 + 4 + 1 + 4 + 4;

    public static IReadOnlyDictionary<string, XnbTypeReaderFunc> All { get; } = new Dictionary<string, XnbTypeReaderFunc>
    {
        ["CharacterTemplateReader"] = ReadCharacter,
        ["ItemReader"] = ReadItem,
        ["PhysicsEntityTemplateReader"] = ReadPhysicsEntity,
    };

    private static DamageContent ReadDamage(XnbContentReader r) =>
        new((AttackProperties)r.ReadInt32(), (Elements)r.ReadInt32(), r.ReadSingle(), r.ReadSingle());

    /// <summary>Damage whose amount is stored as an i32 (Splash events, ThrowGrip abilities).</summary>
    private static DamageContent ReadIntDamage(XnbContentReader r) =>
        new((AttackProperties)r.ReadInt32(), (Elements)r.ReadInt32(), r.ReadInt32(), r.ReadSingle());

    private static ResistanceContent ReadResistance(XnbContentReader r) =>
        new((Elements)r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean());

    /// <summary>Physics templates store resistances without the status flag.</summary>
    private static ResistanceContent ReadResistanceWithoutStatus(XnbContentReader r) =>
        new((Elements)r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), false);

    private static SoundContent ReadSound(XnbContentReader r) => new(r.ReadString(), (SoundBanks)r.ReadInt32());

    private static GibContent ReadGib(XnbContentReader r) => new(r.ReadExternalReference(), r.ReadSingle(), r.ReadSingle());

    private static PointLightContent ReadPointLight(XnbContentReader r)
    {
        var radius = r.ReadSingle();
        var diffuse = r.ReadVector3();
        var ambient = r.ReadVector3();
        var specular = r.ReadSingle();
        var variation = (LightVariationType)r.ReadByte();
        return new PointLightContent(radius, diffuse, ambient, specular, variation, r.ReadSingle(), r.ReadSingle());
    }

    private static ModelVariantContent ReadModelVariant(XnbContentReader r) =>
        new(r.ReadExternalReference(), r.ReadSingle(), r.ReadVector3());

    private static AttachedEffectContent ReadAttachedEffect(XnbContentReader r) => new(r.ReadString(), r.ReadString());

    private static Elements[] ReadElements(XnbContentReader r) => r.ReadList(4, x => (Elements)x.ReadInt32());

    private static SpecialAbilityContent ReadSpecialAbility(XnbContentReader r) =>
        new(r.ReadString(), r.ReadString(), r.ReadString(), ReadElements(r));

    private static string[] ReadStrings(XnbContentReader r) => r.ReadList(1, x => x.ReadString());

    /// <summary>One animation set per weapon class.</summary>
    private static AnimationSetContent[] ReadAnimationSets(XnbContentReader r)
    {
        var sets = new AnimationSetContent[WeaponClasses];
        for (var i = 0; i < sets.Length; i++)
        {
            sets[i] = new AnimationSetContent(r.ReadList(1 + 14, x => new NamedAnimation(x.ReadString(), ReadClipAction(x))));
        }
        return sets;
    }

    private static AnimationClipActionContent ReadClipAction(XnbContentReader r) => new(
        ClipName: r.ReadString(),
        Speed: r.ReadSingle(),
        BlendTime: r.ReadSingle(),
        Loop: r.ReadBoolean(),
        Actions: r.ReadList(1 + 8, ReadAnimationAction));
}
