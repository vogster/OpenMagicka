using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

internal static partial class TemplateReaders
{
    /// <summary>The original stores event collections in a fixed array of 5.</summary>
    private const int MaxEventCollections = 5;

    private static EventCollectionContent[] ReadConditions(XnbContentReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > MaxEventCollections)
        {
            throw new InvalidDataException($"{count} event collections; at most {MaxEventCollections} are allowed");
        }
        var collections = new EventCollectionContent[count];
        for (var i = 0; i < count; i++)
        {
            collections[i] = new EventCollectionContent(
                Condition: (EventConditionType)r.ReadByte(),
                Hitpoints: r.ReadInt32(),
                Elements: (Elements)r.ReadInt32(),
                Threshold: r.ReadSingle(),
                Time: r.ReadSingle(),
                Repeat: r.ReadBoolean(),
                Events: r.ReadList(1, ReadEvent));
        }
        return collections;
    }

    private static EventContent ReadEvent(XnbContentReader r)
    {
        var type = r.ReadByte();
        return type switch
        {
            0 => new DamageEvent(ReadDamage(r), r.ReadBoolean()),
            1 => new SplashEvent(ReadIntDamage(r), r.ReadSingle()),
            2 => new SoundEvent((SoundBanks)r.ReadInt32(), r.ReadString(), r.ReadSingle(), r.ReadBoolean()),
            3 => new EffectEvent(r.ReadBoolean(), r.ReadBoolean(), r.ReadString()),
            4 => new RemoveEvent(r.ReadInt32()),
            5 => new CameraShakeEvent(r.ReadSingle(), r.ReadSingle(), r.ReadBoolean()),
            6 => new DecalEvent(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
            8 => new SpawnEvent(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadSingle(),
                (Order)r.ReadByte(), (ReactTo)r.ReadByte(), (Order)r.ReadByte(), r.ReadSingle(), r.ReadVector3()),
            9 => new OverkillEvent(),
            10 => new SpawnGibsEvent(r.ReadInt32(), r.ReadInt32()),
            11 => new SpawnItemEvent(r.ReadString()),
            12 => new SpawnMagickEvent(r.ReadString()),
            13 => new SpawnMissileEvent(r.ReadString(), r.ReadVector3(), r.ReadBoolean()),
            14 => new LightEvent(ReadPointLight(r)),
            15 => new CastMagickEvent(r.ReadString(), ReadElements(r)),
            16 => new DamageOwnerEvent(ReadDamage(r), r.ReadBoolean()),
            // 7 (Blast) and 17 (Callback) fail to load in the original as well.
            _ => throw new InvalidDataException($"unsupported event type {type}"),
        };
    }
}
