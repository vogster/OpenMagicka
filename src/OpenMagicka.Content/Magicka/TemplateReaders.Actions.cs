using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

internal static partial class TemplateReaders
{
    /// <summary>Class name (case-insensitive), start and end time, then the fields of that class.</summary>
    private static AnimationActionContent ReadAnimationAction(XnbContentReader r)
    {
        var typeName = r.ReadString();
        var start = r.ReadSingle();
        var end = r.ReadSingle();
        return typeName.ToLowerInvariant() switch
        {
            "block" => new BlockAction(start, end, r.ReadInt32()),
            "breakfree" => new BreakFreeAction(start, end, r.ReadSingle(), r.ReadInt32()),
            "camerashake" => ReadCameraShake(r, start, end),
            "castspell" => new CastSpellAction(start, end, r.ReadBoolean() ? null : r.ReadString()),
            "crouch" => new CrouchAction(start, end, r.ReadSingle(), r.ReadSingle()),
            "damagegrip" => new DamageGripAction(start, end, r.ReadBoolean(), r.ReadList(DamageSize, ReadDamage)),
            "dealdamage" => new DealDamageAction(start, end, r.ReadInt32(), (DamageTargets)r.ReadByte()),
            "detachitem" => new DetachItemAction(start, end, r.ReadInt32(), r.ReadVector3()),
            "ethereal" => new EtherealAction(start, end, r.ReadBoolean(), r.ReadSingle(), r.ReadSingle()),
            "footstep" => new FootstepAction(start, end),
            "grip" => new GripAction(start, end, (GripType)r.ReadByte(), r.ReadSingle(), r.ReadSingle(),
                r.ReadString(), r.ReadString(), r.ReadBoolean()),
            "gunfire" => new GunfireAction(start, end, r.ReadInt32(), r.ReadSingle()),
            "immortal" => new ImmortalAction(start, end, r.ReadBoolean()),
            "invisible" => new InvisibleAction(start, end, r.ReadBoolean()),
            "jump" => new JumpAction(start, end, r.ReadSingle(),
                r.ReadBoolean() ? r.ReadSingle() : null, r.ReadBoolean() ? r.ReadSingle() : null),
            "move" => new MoveAction(start, end, r.ReadVector3()),
            "overkillgrip" => new OverkillGripAction(start, end),
            "playeffect" => ReadPlayEffect(r, start, end),
            "playsound" => new PlaySoundAction(start, end, r.ReadString(), (SoundBanks)r.ReadInt32()),
            "releasegrip" => new ReleaseGripAction(start, end),
            "removestatus" => new RemoveStatusAction(start, end, r.ReadString()),
            "setitemattach" => new SetItemAttachAction(start, end, r.ReadInt32(), r.ReadString()),
            "spawnmissile" => new SpawnMissileAction(start, end, r.ReadInt32(), r.ReadVector3(), r.ReadBoolean()),
            "specialability" => ReadSpecialAbilityAction(r, start, end),
            "suicide" => new SuicideAction(start, end, r.ReadBoolean()),
            "throwgrip" => new ThrowGripAction(start, end),
            "tongue" => new TongueAction(start, end, r.ReadSingle()),
            "weaponvisibility" => new WeaponVisibilityAction(start, end, r.ReadInt32(), r.ReadBoolean()),
            _ => throw new InvalidDataException($"unknown animation action '{typeName}'"),
        };
    }

    /// <summary>The leading string is ignored by the original.</summary>
    private static CameraShakeAction ReadCameraShake(XnbContentReader r, float start, float end)
    {
        r.ReadString();
        return new CameraShakeAction(start, end, r.ReadSingle(), r.ReadSingle());
    }

    /// <summary>The trailing float is ignored by the original.</summary>
    private static PlayEffectAction ReadPlayEffect(XnbContentReader r, float start, float end)
    {
        var action = new PlayEffectAction(start, end, r.ReadString(), r.ReadBoolean(), r.ReadString());
        r.ReadSingle();
        return action;
    }

    private static SpecialAbilityAction ReadSpecialAbilityAction(XnbContentReader r, float start, float end)
    {
        var weapon = r.ReadInt32();
        return new SpecialAbilityAction(start, end, weapon, weapon < 0 ? ReadSpecialAbility(r) : null);
    }
}
