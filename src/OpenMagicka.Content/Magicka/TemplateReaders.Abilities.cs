using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

internal static partial class TemplateReaders
{
    // u8 type, u8 visual, colour, ttl, effect
    private const int BuffHeaderSize = 1 + 1 + 12 + 4 + 1;

    // u8 target, u8 type, u8 visual, colour, effect, ttl, radius, target types, faction
    private const int AuraHeaderSize = 1 + 1 + 1 + 12 + 1 + 4 + 4 + 1 + 4;

    /// <summary>Common part (class name, cooldown, target, optional fuzzy expression, animations), then the class's fields.</summary>
    private static AbilityContent ReadAbility(XnbContentReader r)
    {
        var typeName = r.ReadString();
        var cooldown = r.ReadSingle();
        var target = (AbilityTarget)r.ReadByte();
        var fuzzy = r.ReadBoolean() ? r.ReadString() : null;
        var animations = ReadStrings(r);
        return typeName.ToLowerInvariant() switch
        {
            "block" => new BlockAbility(cooldown, target, fuzzy, animations, r.ReadSingle(), r.ReadInt32()),
            "castspell" => new CastSpellAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), (CastType)r.ReadInt32(), ReadElements(r)),
            "confusegrip" => new ConfuseGripAbility(cooldown, target, fuzzy, animations),
            "damagegrip" => new DamageGripAbility(cooldown, target, fuzzy, animations),
            "removestatus" => new RemoveStatusAbility(cooldown, target, fuzzy, animations),
            "dash" => new DashAbility(cooldown, target, fuzzy, animations, r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadVector3()),
            "elementsteal" => new ElementStealAbility(cooldown, target, fuzzy, animations, r.ReadSingle(), r.ReadSingle()),
            "gripcharacterfrombehind" => new GripCharacterFromBehindAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            "jump" => new JumpAbility(cooldown, target, fuzzy, animations, r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            "melee" => new MeleeAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadList(4, x => x.ReadInt32()), r.ReadBoolean()),
            "pickupcharacter" or "zombiegrip" => new PickUpCharacterAbility(cooldown, target, fuzzy, animations, typeName,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadString()),
            "ranged" => new RangedAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadList(4, x => x.ReadInt32())),
            "specialabilityability" => new SpecialAbilityAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32()),
            "throwgrip" => new ThrowGripAbility(cooldown, target, fuzzy, animations,
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadList(DamageSize, ReadIntDamage)),
            _ => throw new InvalidDataException($"unknown ability '{typeName}'"),
        };
    }

    private static BuffContent ReadBuff(XnbContentReader r)
    {
        var type = r.ReadByte();
        var visual = (BuffVisualCategory)r.ReadByte();
        var color = r.ReadVector3();
        var ttl = r.ReadSingle();
        var effect = r.ReadString();
        return type switch
        {
            0 => new BoostDamageBuff(visual, color, ttl, effect, ReadDamage(r)),
            1 => new DealDamageBuff(visual, color, ttl, effect, ReadDamage(r)),
            2 => new ResistanceBuff(visual, color, ttl, effect, ReadBuffResistance(r)),
            3 => new UndyingBuff(visual, color, ttl, effect),
            4 => new BoostBuff(visual, color, ttl, effect, r.ReadSingle()),
            5 => new ReduceAggroBuff(visual, color, ttl, effect, r.ReadSingle()),
            6 or 7 or 8 => new ModifierBuff(visual, color, ttl, effect, (ModifierBuffKind)type, r.ReadSingle(), r.ReadSingle()),
            _ => throw new InvalidDataException($"unknown buff type {type}"),
        };
    }

    /// <summary>Unlike template resistances, a buff stores the modifier before the multiplier.</summary>
    private static ResistanceContent ReadBuffResistance(XnbContentReader r)
    {
        var elements = (Elements)r.ReadInt32();
        var modifier = r.ReadSingle();
        var multiplier = r.ReadSingle();
        return new ResistanceContent(elements, multiplier, modifier, r.ReadBoolean());
    }

    private static AuraContent ReadAura(XnbContentReader r)
    {
        var target = (AuraTarget)r.ReadByte();
        var type = r.ReadByte();
        var visual = (BuffVisualCategory)r.ReadByte();
        var color = r.ReadVector3();
        var effect = r.ReadString();
        var ttl = r.ReadSingle();
        var radius = r.ReadSingle();
        var targetTypes = r.ReadString();
        var faction = r.ReadInt32();
        return type switch
        {
            0 => new BuffAura(target, visual, color, effect, ttl, radius, targetTypes, faction, ReadBuff(r)),
            1 => new DeflectAura(target, visual, color, effect, ttl, radius, targetTypes, faction, r.ReadSingle()),
            2 => new BoostAura(target, visual, color, effect, ttl, radius, targetTypes, faction, r.ReadSingle()),
            3 => new LifeStealAura(target, visual, color, effect, ttl, radius, targetTypes, faction, r.ReadSingle()),
            4 => new LoveAura(target, visual, color, effect, ttl, radius, targetTypes, faction, r.ReadSingle(), r.ReadSingle()),
            _ => throw new InvalidDataException($"unknown aura type {type}"),
        };
    }
}
