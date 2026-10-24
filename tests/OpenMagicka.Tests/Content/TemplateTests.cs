using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;
using static OpenMagicka.Tests.Content.TemplateTestWriter;
using static OpenMagicka.Tests.Content.XnbTestWriter;

namespace OpenMagicka.Tests.Content;

public class TemplateTests
{
    private static XnbContent Load(XnbTestWriter w) => XnbContent.Load(w.Build(), XnbReaderRegistry.Magicka);

    private static readonly float[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>Physics template up to (excluding) the ConditionCollection.</summary>
    private static XnbTestWriter PhysicsStart(int lights = 0)
    {
        var w = new XnbTestWriter().TypeId(Readers + "PhysicsEntityTemplateReader" + Magicka);
        w.B(true, false, true).F(50).I(100).B(true);
        w.I(1).I(8).F(0.5f, 1);                                  // resistance without status flag: Fire
        w.I(1).S(@"..\Gibs\barrel_gib0").F(2, 1);                  // one gib
        w.S("hit_wood").S("Misc/wood_hit").S("");
        w.Null();                                                 // no model
        w.B(true).TypeId(XnaContent + "ListReader`1[[Microsoft.Xna.Framework.Vector3" + Xna + "]]" + Xna)
            .UInts(3).F(0, 0, 0, 1, 0, 0, 0, 1, 0).I(1, 0, 1, 2);
        w.I(2).S("main").F(0, 0, 0, 1, 1, 1, 0, 0, 0, 1).S("extra").F(0, 0, 0, 1, 1, 1, 0, 0, 0, 1);
        w.I(1).S("smoke").F(Identity);
        return w.I(lights);
    }

    [Fact]
    public void PhysicsEntityTemplate_ReadsEverySection()
    {
        var w = PhysicsStart().Conditions(
            e => e.Bytes(0).Damage(1, 8, 10, 1).B(true),                        // Damage
            e => e.Bytes(1).IntDamage(1, 8, 25, 2).F(3),                        // Splash (int amount)
            e => e.Bytes(8).S("goblin").S("idle").S("spawn").F(50).Bytes(1, 2, 3).F(90).F(0, 1, 0),
            e => e.Bytes(15).S("meteor").I(2, 8, 8),                            // CastMagick
            e => e.Bytes(9));                                                   // Overkill
        w.S("barrel_explosive");
        w.B(true).F(0.75f);                                                     // animation, radius
        w.I(1).S("barrel_mdl").F(1.5f, 1, 0.5f, 0.5f);
        w.B(true).S("barrel_skeleton");
        w.I(1).S("top").S("fuse_sparks");
        w.AnimationSets(setIndex: 0, body: a =>
        {
            a.S("idle").S("barrel_idle").F(1, 0.2f).B(true).I(3);
            a.S("Footstep").F(0, 0.1f);
            a.S("castspell").F(0.1f, 0.2f).B(false).S("hand");
            a.S("SpecialAbility").F(0.2f, 0.9f).I(-1).S("Blink").S("").S("blink_fx").I(1, 0x20);
        });

        var template = Assert.IsType<PhysicsEntityTemplateContent>(Load(w).Root);

        Assert.Equal((true, false, true, 50f, 100, true), (template.Movable, template.Pushable, template.Solid, template.Mass, template.MaxHitPoints, template.CanHaveStatus));
        Assert.Equal(new ResistanceContent(Elements.Fire, 0.5f, 1, false), Assert.Single(template.Resistances));
        Assert.Equal(new ExternalReference(@"..\Gibs\barrel_gib0"), Assert.Single(template.Gibs).Model);
        Assert.Equal(("hit_wood", "Misc/wood_hit", ""), (template.HitEffect, template.HitSound, template.GibTrailEffect));
        Assert.Equal(new CollisionTriangle(0, 1, 2), Assert.Single(template.CollisionMesh!.Triangles));
        Assert.Equal(["main", "extra"], template.Boxes.Select(b => b.Name));
        Assert.Equal("smoke", Assert.Single(template.Effects).Effect);

        var events = Assert.Single(template.Events);
        Assert.Equal(EventConditionType.Death, events.Condition);
        Assert.Equal(new DamageContent(AttackProperties.Damage, Elements.Fire, 10, 1), Assert.IsType<DamageEvent>(events.Events[0]).Damage);
        Assert.Equal(25f, Assert.IsType<SplashEvent>(events.Events[1]).Damage.Amount);
        var spawn = Assert.IsType<SpawnEvent>(events.Events[2]);
        Assert.Equal(("goblin", 90f, new Vector3(0, 1, 0)), (spawn.CharacterType, spawn.Rotation, spawn.Offset));
        Assert.Equal([Elements.Fire, Elements.Fire], Assert.IsType<CastMagickEvent>(events.Events[3]).Elements);
        Assert.IsType<OverkillEvent>(events.Events[4]);

        Assert.Equal("barrel_explosive", template.Id);
        var animation = template.Animation!;
        Assert.Equal(0.75f, animation.Radius);
        Assert.Equal(new ExternalReference("barrel_skeleton"), animation.Skeleton);
        Assert.Equal(27, animation.AnimationSets.Count);
        var idle = Assert.Single(animation.AnimationSets[0].Animations);
        Assert.Equal(("idle", "barrel_idle", true), (idle.Animation, idle.Clip.ClipName, idle.Clip.Loop));
        Assert.IsType<FootstepAction>(idle.Clip.Actions[0]);
        Assert.Equal("hand", Assert.IsType<CastSpellAction>(idle.Clip.Actions[1]).Source);
        var special = Assert.IsType<SpecialAbilityAction>(idle.Clip.Actions[2]);
        Assert.Equal(("Blink", "blink_fx", Elements.Arcane), (special.Ability!.TypeName, special.Ability.DisplayName, special.Ability.Elements[0]));
    }

    [Fact]
    public void PhysicsEntityTemplate_WithLights_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(PhysicsStart(lights: 1)));
    }

    [Fact]
    public void Events_UnimplementedBlast_IsInvalid()
    {
        var w = PhysicsStart().Conditions(e => e.Bytes(7));

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void Conditions_MoreThanFive_AreInvalid()
    {
        var w = PhysicsStart().I(6);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void AnimationAction_UnknownType_IsInvalid()
    {
        var w = PhysicsStart().Conditions().S("p").B(true).F(1).I(0).B(false).I(0);
        w.AnimationSets(0, a => a.S("idle").S("clip").F(1, 0).B(false).I(1).S("Teleport").F(0, 1));

        var error = Assert.Throws<InvalidDataException>(() => Load(w));
        Assert.Contains("Teleport", error.Message);
    }

    private static XnbTestWriter ItemWith(Action<XnbTestWriter> auras)
    {
        var w = new XnbTestWriter().TypeId(Readers + "ItemReader" + Magicka);
        w.S("staff_of_life").S("#item_staff_life").S("#item_staff_life_d");
        w.I(1).S("staff_hum").I(0x10);
        w.B(true, false).I(5).Bytes(19).F(1.5f).B(false, false, true);
        w.I(1).I(0x40).F(0, -50).B(true);                         // resistance with status: Life
        w.Bytes(6).F(2);                                          // passive ability AreaRegeneration
        w.I(2).S("glow").S("sparkle");
        w.I(1).PointLight();
        w.B(true).F(30).S("Heal").S("cast_area").S("heal_fx").I(1, 0x40);
        w.F(2.5f).B(true).Conditions();
        w.F(10).B(false).F(0, 30, 0.2f, 20).I(6, 300).F(0.9f);
        w.S("Weapons/staff_shot").S("muzzle").S("").F(80).S("B/12").S("3");
        w.Conditions().S("").Conditions();
        w.F(1.2f).S(@"..\..\Models\Items\staff_life");
        auras(w);
        return w;
    }

    [Fact]
    public void Item_ReadsGunSpecialAbilityAndBuffAura()
    {
        var w = ItemWith(a =>
        {
            a.I(1);
            a.Bytes(0, 0, 2).F(0, 1, 0).S("aura_fx").F(0, 5).S("goblin,orc").I(0);       // Buff aura
            a.Bytes(2, 2).F(0, 0, 1).F(10).S("shield_fx").I(0x8).F(-1, 0.5f).B(true);      // Resistance buff
        });

        var item = Assert.IsType<ItemContent>(Load(w).Root);

        Assert.Equal(("staff_of_life", (byte)19, 1.5f, true), (item.Name, item.WeaponClass, item.CooldownTime, item.PauseSounds));
        Assert.Equal(new SoundContent("staff_hum", SoundBanks.Spells), Assert.Single(item.Sounds));
        Assert.Equal(new ResistanceContent(Elements.Life, 0, -50, true), Assert.Single(item.Resistances));
        Assert.Equal(["glow", "sparkle"], item.Effects);
        Assert.Equal(LightVariationType.Flicker, Assert.Single(item.Lights).Variation);
        Assert.Equal((30f, "Heal"), (item.SpecialRechargeTime, item.SpecialAbility!.TypeName));
        Assert.Equal((2.5f, true), (item.Melee.Range, item.Melee.MultiHit));
        Assert.Equal((6, 300, "Weapons/staff_shot", "B/12"), (item.Ranged.GunClip, item.Ranged.GunRate, item.Ranged.GunSound, item.Ranged.NonTracerSprite));
        Assert.Null(item.Ranged.ProjectileModel);
        Assert.Equal(new ExternalReference(@"..\..\Models\Items\staff_life"), item.Model);

        var aura = Assert.IsType<BuffAura>(Assert.Single(item.Auras));
        Assert.Equal((AuraTarget.Friendly, 5f, "goblin,orc"), (aura.Target, aura.Radius, aura.TargetTypes));
        var buff = Assert.IsType<ResistanceBuff>(aura.Buff);
        // The modifier is stored before the multiplier in buffs.
        Assert.Equal(new ResistanceContent(Elements.Fire, 0.5f, -1, true), buff.Resistance);
    }

    [Fact]
    public void Aura_UnknownType_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(ItemWith(a => a.I(1).Bytes(0, 9, 0).F(0, 0, 0).S("").F(0, 0).S("").I(0))));
    }

    private static XnbTestWriter CharacterWith(int lights, Action<XnbTestWriter> abilities)
    {
        var w = new XnbTestWriter().TypeId(Readers + "CharacterTemplateReader" + Magicka);
        w.S("goblin_shaman").S("#goblin_shaman").I(0x4, 1);
        w.B(false, false, true, false, false, false, true);
        w.I(1).S("goblin_chant").I(0x20);
        w.I(0);                                                                       // gibs
        w.I(lights);
        for (var i = 0; i < lights; i++)
        {
            w.S("head").PointLight();
        }
        w.F(300).I(2).B(false).F(0, 0).I(3).F(0.5f).I(10, 25).B(true, false).I(1);
        w.F(100, 1, 0.8f, 0.4f, 70, 4, 6, 0.1f, 1.5f);
        w.I(0x10).S("summon_fire");
        w.I(1).I(0x8).F(0.5f, 0).B(false);
        w.I(1).S("goblin_mdl").F(1, 1, 1, 1);
        w.S("goblin_skeleton");
        w.I(1).S("staff").S("staff_glow");
        w.AnimationSets(0, a => a.S("idle").S("goblin_idle").F(1, 0.1f).B(true).I(0));
        w.I(1).I(0).S("RightAttach").F(0, 1.5f, 0).S(@"..\Items\goblin_staff");
        w.Conditions();
        w.F(15, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12).B(true).F(40);
        abilities(w);
        w.I(1).Bytes(1).I(2).S("walk").S("swim");
        w.I(1).Bytes(4, 1).F(1, 1, 0).F(5).S("").F(1.5f);                         // Boost buff
        w.I(1).Bytes(5, 1, 0).F(0, 0, 0).S("").F(0, 3).S("").I(0).F(0.25f);        // Deflect aura on self
        return w;
    }

    [Fact]
    public void CharacterTemplate_ReadsStatsAbilitiesBuffsAndAuras()
    {
        var w = CharacterWith(lights: 1, abilities: a =>
        {
            a.I(4);
            a.S("CastSpell").F(5).Bytes(2).B(true).S("distance > 5").I(1).S("cast_force");
            a.F(3, 15, 0.5f, 1, 2).I(0).I(2, 8, 8);
            a.S("Melee").F(1).Bytes(2).B(false).I(1).S("attack_melee0").F(0, 2, 1).I(1, 0).B(true);
            a.S("throwgrip").F(4).Bytes(2).B(false).I(0).F(6, 1, 30).I(1).IntDamage(2, 1, 40, 1);
            a.S("ZombieGrip").F(8).Bytes(2).B(false).I(0).F(2, 0, 1, 90).S("drop");
        });

        var template = Assert.IsType<CharacterTemplateContent>(Load(w).Root);

        Assert.Equal(("goblin_shaman", Factions.FRIENDLY, true, true), (template.Id, template.Factions, template.Fearless, template.CanSeeInvisible));
        Assert.Equal("head", Assert.Single(template.Lights).JointName);
        Assert.Equal((300f, 2, 3, 25, 1, 70f, 1.5f), (template.Stats.MaxHitpoints, template.Stats.NumberOfHealthBars,
            template.Stats.HitTolerance, template.Stats.ExperienceValue, template.Stats.Regeneration, template.Stats.Mass, template.Stats.StunTime));
        Assert.Equal((SoundBanks.Spells, "summon_fire"), (template.SummonElementBank, template.SummonElementCue));
        Assert.Equal(new ExternalReference("goblin_skeleton"), template.Skeleton);
        Assert.Equal(new EquipmentContent(0, "RightAttach", new Vector3(0, 1.5f, 0), new ExternalReference(@"..\Items\goblin_staff")), Assert.Single(template.Equipment));
        Assert.Equal((15f, 12f, true, 40f), (template.Ai.AlertRadius, template.Ai.HealthWeight, template.Ai.Flocking, template.Ai.BreakFreeStrength));

        var cast = Assert.IsType<CastSpellAbility>(template.Abilities[0]);
        Assert.Equal(("distance > 5", AbilityTarget.Enemy, 15f), (cast.Fuzzy, cast.Target, cast.MaxRange));
        Assert.Equal([Elements.Fire, Elements.Fire], cast.Elements);
        Assert.Equal([0], Assert.IsType<MeleeAbility>(template.Abilities[1]).Weapons);
        Assert.Equal(40f, Assert.Single(Assert.IsType<ThrowGripAbility>(template.Abilities[2]).Damages).Amount);
        var zombie = Assert.IsType<PickUpCharacterAbility>(template.Abilities[3]);
        Assert.Equal(("ZombieGrip", "drop", true), (zombie.TypeName, zombie.DropAnimation, zombie.IsZombieGrip));

        Assert.Equal(new MoveAnimationsContent(MovementProperties.Water, ["walk", "swim"]), Assert.Single(template.MoveAnimations), new MoveAnimationsComparer());
        Assert.Equal(1.5f, Assert.IsType<BoostBuff>(Assert.Single(template.Buffs)).Amount);
        Assert.Equal((AuraTarget.Self, 0.25f), (template.Auras[0].Target, Assert.IsType<DeflectAura>(template.Auras[0]).Strength));
    }

    [Fact]
    public void CharacterTemplate_MoreThanFourLights_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(CharacterWith(lights: 5, abilities: a => a.I(0))));
    }

    [Fact]
    public void Ability_UnknownType_IsInvalid()
    {
        var w = CharacterWith(lights: 0, abilities: a => a.I(1).S("Teleport").F(1).Bytes(2).B(false).I(0));

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    private static XnbTestWriter PhysicsWithActions(int count, Action<XnbTestWriter> actions) =>
        PhysicsStart().Conditions().S("p").B(true).F(1).I(0).B(false).I(0)
            .AnimationSets(0, a =>
            {
                a.S("idle").S("clip").F(1, 0).B(false).I(count);
                actions(a);
            });

    [Fact]
    public void AnimationActions_WithOptionalAndSkippedFields_RoundTrip()
    {
        var w = PhysicsWithActions(6, a =>
        {
            a.S("Jump").F(0, 1).F(45).B(true).F(2).B(false);                      // min range only
            a.S("jump").F(0, 1).F(30).B(true).F(1).B(true).F(9);                  // both ranges
            a.S("Grip").F(0, 1).Bytes(1).F(1.5f, 3).S("hand").S("neck").B(true);
            a.S("PlayEffect").F(0, 1).S("mouth").B(true).S("breath").F(99);       // trailing float ignored
            a.S("CameraShake").F(0, 1).S("ignored").F(0.5f, 2);                   // leading string ignored
            a.S("DamageGrip").F(0, 1).B(false).I(2).Damage(1, 0x40, 5, 1).Damage(1, 8, 7, 1);
        });

        var actions = Assert.Single(Assert.IsType<PhysicsEntityTemplateContent>(Load(w).Root).Animation!.AnimationSets[0].Animations).Clip.Actions;

        Assert.Equal((2f, (float?)null), (Assert.IsType<JumpAction>(actions[0]).MinRange, ((JumpAction)actions[0]).MaxRange));
        Assert.Equal((1f, 9f), (((JumpAction)actions[1]).MinRange!.Value, ((JumpAction)actions[1]).MaxRange!.Value));
        var grip = Assert.IsType<GripAction>(actions[2]);
        Assert.Equal((GripType.Ride, "hand", "neck", true), (grip.Type, grip.GripAttachBone, grip.TargetAttachBone, grip.FinishOnGrip));
        Assert.Equal(("mouth", true, "breath"), (((PlayEffectAction)actions[3]).Bone, ((PlayEffectAction)actions[3]).Attach, ((PlayEffectAction)actions[3]).Effect));
        Assert.Equal((0.5f, 2f), (((CameraShakeAction)actions[4]).Duration, ((CameraShakeAction)actions[4]).Magnitude));
        Assert.Equal([5f, 7f], Assert.IsType<DamageGripAction>(actions[5]).Damages.Select(d => d.Amount));
    }

    [Fact]
    public void SmallestPossibleItems_EndingAtTheEndOfTheStream_Load()
    {
        // Overkill events (1 byte each) are the smallest list items: the minimum sizes must allow them.
        var w = PhysicsStart().I(1).Bytes(0x20).I(0, 0).F(0, 0).B(false).I(3).Bytes(9, 9, 9);
        w.S("p").B(true).F(1).I(0).B(false).I(0).AnimationSets();

        var events = Assert.Single(Assert.IsType<PhysicsEntityTemplateContent>(Load(w).Root).Events).Events;

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.IsType<OverkillEvent>(e));
    }

    [Fact]
    public void EventCountBeyondTheData_IsInvalid()
    {
        var w = PhysicsStart().I(1).Bytes(0x20).I(0, 0).F(0, 0).B(false).I(1_000_000);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void UndefinedEnumValues_AreKeptAsStored()
    {
        var w = CharacterWith(lights: 0, abilities: a => a.I(1).S("ConfuseGrip").F(1).Bytes(0).B(false).I(0));

        var ability = Assert.Single(Assert.IsType<CharacterTemplateContent>(Load(w).Root).Abilities);

        Assert.Equal((AbilityTarget)0, ability.Target);
    }

    private sealed class MoveAnimationsComparer : IEqualityComparer<MoveAnimationsContent>
    {
        public bool Equals(MoveAnimationsContent? x, MoveAnimationsContent? y) =>
            x!.Movement == y!.Movement && x.Animations.SequenceEqual(y.Animations);

        public int GetHashCode(MoveAnimationsContent obj) => obj.Movement.GetHashCode();
    }
}
