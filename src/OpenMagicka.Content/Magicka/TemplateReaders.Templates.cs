using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

internal static partial class TemplateReaders
{
    private const int MaxCharacterLights = 4;

    private static CharacterTemplateContent ReadCharacter(XnbContentReader r)
    {
        var id = r.ReadString();
        var displayId = r.ReadString();
        var factions = r.ReadInt32();
        var bloodType = r.ReadInt32();
        var flags = new bool[7];
        for (var i = 0; i < flags.Length; i++)
        {
            flags[i] = r.ReadBoolean();
        }
        var sounds = r.ReadList(1 + 4, ReadSound);
        var gibs = r.ReadList(1 + 8, ReadGib);
        var lights = r.ReadList(1 + PointLightSize, x => new AttachedLightContent(x.ReadString(), ReadPointLight(x)));
        if (lights.Length > MaxCharacterLights)
        {
            throw new InvalidDataException($"{lights.Length} attached lights; the original allows {MaxCharacterLights}");
        }
        var stats = new CharacterStats(
            MaxHitpoints: r.ReadSingle(),
            NumberOfHealthBars: r.ReadInt32(),
            Undying: r.ReadBoolean(),
            UndieTime: r.ReadSingle(),
            UndieHitPoints: r.ReadSingle(),
            HitTolerance: r.ReadInt32(),
            KnockdownTolerance: r.ReadSingle(),
            ScoreValue: r.ReadInt32(),
            ExperienceValue: r.ReadInt32(),
            RewardOnKill: r.ReadBoolean(),
            RewardOnOverkill: r.ReadBoolean(),
            Regeneration: r.ReadInt32(),
            MaxPanic: r.ReadSingle(),
            ZapModifier: r.ReadSingle(),
            Length: r.ReadSingle(),
            Radius: r.ReadSingle(),
            Mass: r.ReadSingle(),
            Speed: r.ReadSingle(),
            TurnSpeed: r.ReadSingle(),
            BleedRate: r.ReadSingle(),
            StunTime: r.ReadSingle());
        var summonBank = (SoundBanks)r.ReadInt32();
        var summonCue = r.ReadString();
        var resistances = r.ReadList(ResistanceSize, ReadResistance);
        var models = r.ReadList(1 + 16, ReadModelVariant);
        var skeleton = r.ReadExternalReference();
        var attachedEffects = r.ReadList(2, ReadAttachedEffect);
        var animationSets = ReadAnimationSets(r);
        var equipment = r.ReadList(4 + 1 + 12 + 1,
            x => new EquipmentContent(x.ReadInt32(), x.ReadString(), x.ReadVector3(), x.ReadExternalReference()));
        var events = ReadConditions(r);
        var ai = new CharacterAiWeights(
            AlertRadius: r.ReadSingle(),
            GroupChase: r.ReadSingle(),
            GroupSeparation: r.ReadSingle(),
            GroupCohesion: r.ReadSingle(),
            GroupAlignment: r.ReadSingle(),
            GroupWander: r.ReadSingle(),
            FriendlyAvoidance: r.ReadSingle(),
            EnemyAvoidance: r.ReadSingle(),
            SightAvoidance: r.ReadSingle(),
            DangerAvoidance: r.ReadSingle(),
            AngerWeight: r.ReadSingle(),
            DistanceWeight: r.ReadSingle(),
            HealthWeight: r.ReadSingle(),
            Flocking: r.ReadBoolean(),
            BreakFreeStrength: r.ReadSingle());
        var abilities = r.ReadList(1 + 4 + 1 + 1 + 4, ReadAbility);
        var moveAnimations = r.ReadList(1 + 4, x => new MoveAnimationsContent((MovementProperties)x.ReadByte(), ReadStrings(x)));
        var buffs = r.ReadList(BuffHeaderSize, ReadBuff);
        var auras = r.ReadList(AuraHeaderSize, ReadAura);
        return new CharacterTemplateContent(id, displayId, (Factions)factions, bloodType,
            flags[0], flags[1], flags[2], flags[3], flags[4], flags[5], flags[6],
            sounds, gibs, lights, stats, summonBank, summonCue, resistances, models, skeleton, attachedEffects,
            animationSets, equipment, events, ai, abilities, moveAnimations, buffs, auras);
    }

    private static ItemContent ReadItem(XnbContentReader r)
    {
        var name = r.ReadString();
        var displayName = r.ReadString();
        var description = r.ReadString();
        var sounds = r.ReadList(1 + 4, ReadSound);
        var pickable = r.ReadBoolean();
        var bound = r.ReadBoolean();
        var blockValue = r.ReadInt32();
        var weaponClass = r.ReadByte();
        var cooldown = r.ReadSingle();
        var hideModel = r.ReadBoolean();
        var hideEffect = r.ReadBoolean();
        var pauseSounds = r.ReadBoolean();
        var resistances = r.ReadList(ResistanceSize, ReadResistance);
        var passive = r.ReadByte();
        var passiveValue = r.ReadSingle();
        var effects = ReadStrings(r);
        var lights = r.ReadList(PointLightSize, ReadPointLight);
        float? recharge = null;
        SpecialAbilityContent? special = null;
        if (r.ReadBoolean())
        {
            recharge = r.ReadSingle();
            special = ReadSpecialAbility(r);
        }
        var melee = new ItemMelee(r.ReadSingle(), r.ReadBoolean(), ReadConditions(r));
        var ranged = new ItemRanged(
            Range: r.ReadSingle(),
            Facing: r.ReadBoolean(),
            Homing: r.ReadSingle(),
            Elevation: r.ReadSingle(),
            Danger: r.ReadSingle(),
            GunRange: r.ReadSingle(),
            GunClip: r.ReadInt32(),
            GunRate: r.ReadInt32(),
            GunAccuracy: r.ReadSingle(),
            GunSound: r.ReadString(),
            GunMuzzleEffect: r.ReadString(),
            GunShellsEffect: r.ReadString(),
            TracerVelocity: r.ReadSingle(),
            NonTracerSprite: r.ReadString(),
            TracerSprite: r.ReadString(),
            GunConditions: ReadConditions(r),
            ProjectileModel: r.ReadExternalReference(),
            RangedConditions: ReadConditions(r));
        var scale = r.ReadSingle();
        var model = r.ReadExternalReference();
        var auras = r.ReadList(AuraHeaderSize, ReadAura);
        return new ItemContent(name, displayName, description, sounds, pickable, bound, blockValue, weaponClass,
            cooldown, hideModel, hideEffect, pauseSounds, resistances, passive, passiveValue, effects, lights,
            recharge, special, melee, ranged, scale, model, auras);
    }

    private static PhysicsEntityTemplateContent ReadPhysicsEntity(XnbContentReader r)
    {
        var movable = r.ReadBoolean();
        var pushable = r.ReadBoolean();
        var solid = r.ReadBoolean();
        var mass = r.ReadSingle();
        var maxHitPoints = r.ReadInt32();
        var canHaveStatus = r.ReadBoolean();
        var resistances = r.ReadList(12, ReadResistanceWithoutStatus);
        var gibs = r.ReadList(1 + 8, ReadGib);
        var hitEffect = r.ReadString();
        var hitSound = r.ReadString();
        var gibTrail = r.ReadString();
        var model = r.ReadObject<ModelContent>();
        var collision = r.ReadBoolean() ? LevelModelReader.ReadCollisionMesh(r) : null;
        var boxes = r.ReadList(1 + 40,
            x => new CollisionBoxContent(x.ReadString(), x.ReadVector3(), x.ReadVector3(), x.ReadQuaternion()));
        var effects = r.ReadList(1 + 64, x => new PlacedEffectContent(x.ReadString(), x.ReadMatrix()));
        var lightCount = r.ReadInt32();
        if (lightCount != 0)
        {
            throw new InvalidDataException("physics entity lights are not implemented in the original");
        }
        var events = ReadConditions(r);
        var id = r.ReadString();
        // The original treats a missing flag at the end of the stream as false. Here shared
        // resources may follow the template, so the flag is always read; every file has it.
        PhysicsEntityAnimationContent? animation = null;
        if (r.ReadBoolean())
        {
            animation = new PhysicsEntityAnimationContent(
                Radius: r.ReadSingle(),
                Models: r.ReadList(1 + 16, ReadModelVariant),
                Skeleton: r.ReadBoolean() ? r.ReadExternalReference() : null,
                AttachedEffects: r.ReadList(2, ReadAttachedEffect),
                AnimationSets: ReadAnimationSets(r));
        }
        return new PhysicsEntityTemplateContent(movable, pushable, solid, mass, maxHitPoints, canHaveStatus,
            resistances, gibs, hitEffect, hitSound, gibTrail, model, collision, boxes, effects, events, id, animation);
    }
}
