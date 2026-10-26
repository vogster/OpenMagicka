using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>A character's slots, the items in them and what they change (ENTITIES_AI.md §8.1.1).</summary>
public class EquipmentTests
{
    private static readonly ItemTemplate Sword = new("sword", WeaponClass.SlashMedium, BlockValue: 20);

    private static readonly ItemTemplate Staff = new("staff", WeaponClass.Staff, Resistances: ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0.5f));

    /// <summary>A shield of this block value in slot 0 (replacing what was there).</summary>
    internal static Item Shield(Character character, int blocks)
    {
        var shield = new Item(new ItemTemplate("shield", BlockValue: blocks));
        character.Equip(0, shield, "LeftHand");
        return shield;
    }

    /// <summary>A point two metres in front of a character facing −Z.</summary>
    internal static Vector3 InFront(Character character) => character.Position - 2 * Vector3.UnitZ;

    private static DamageEntry Earth(float amount) => new(AttackProperties.Damage, Elements.Earth, amount, 1);

    [Fact]
    public void AStretchWithoutWeapons_PutsTheUnarmedItemInTheWeaponAndStaffSlots_AndEndingItGivesThemBack()
    {
        using var world = World();
        var template = Goblin with
        {
            Equipment = [new EquipmentTemplate(0, "LeftHand", Vector3.Zero, Sword), new EquipmentTemplate(1, "RightHand", Vector3.Zero, Staff)],
        };
        var wizard = world.Spawn(template, new Vector3(0, 0.9f, 0));
        wizard.IsPlayer = true;
        var unarmed = new ItemTemplate("weapon_unarmed");
        Assert.Equal(1, wizard.SpellSource);
        Assert.NotNull(wizard.Defences.Staff);

        wizard.Disarm(unarmed);
        Assert.Equal(("weapon_unarmed", "weapon_unarmed"), (wizard.ItemIn(0)!.Template.Id, wizard.ItemIn(1)!.Template.Id));
        Assert.Equal(("LeftHand", "RightHand"), (wizard.Equipment[0].BoneName, wizard.Equipment[1].BoneName));
        Assert.Equal(-1, wizard.SpellSource);                                  // no staff: self casts only
        Assert.Null(wizard.Defences.Staff);

        wizard.Rearm();
        Assert.Equal((Sword, Staff), (wizard.ItemIn(0)!.Template, wizard.ItemIn(1)!.Template));
        Assert.Equal(1, wizard.SpellSource);
        Assert.NotNull(wizard.Defences.Staff);
    }

    [Fact]
    public void ATemplateWithoutAStaff_IsDisarmedOnlyInItsWeaponSlot()
    {
        using var world = World();
        var wizard = world.Spawn(Goblin with { Equipment = [new EquipmentTemplate(0, "RightHand", Vector3.Zero, Sword)] }, new Vector3(0, 0.9f, 0));

        wizard.Disarm(new ItemTemplate("weapon_unarmed"));

        Assert.Equal("weapon_unarmed", wizard.ItemIn(0)!.Template.Id);
        Assert.Null(wizard.ItemIn(1));                                         // nowhere to hold it
    }

    [Fact]
    public void ATemplatesItems_AreCopiedForEachCharacter()
    {
        using var world = World();
        var template = Goblin with { Equipment = [new EquipmentTemplate(0, "RightHand", Vector3.Zero, Sword)] };
        var one = world.Spawn(template, new Vector3(0, 0.9f, 0));
        var two = world.Spawn(template, new Vector3(3, 0.9f, 0));

        Assert.Same(Sword, one.ItemIn(0)!.Template);
        Assert.NotSame(one.ItemIn(0), two.ItemIn(0));
        Assert.Equal("RightHand", one.Equipment[0].BoneName);
        Assert.All(one.Equipment.Skip(1), s => Assert.Null(s.Item));
    }

    [Fact]
    public void TheWeaponsSet_PlaysItsAnimations_AndAStaffsDoesNot()
    {
        using var world = World();
        var slash = new CharacterAnimation("attack_melee0", new AnimationClip("slash", 0.7f), 1, 0, false, []);
        var basics = Clips();
        var animations = new CharacterAnimations([basics.DefaultSet, .. Enumerable.Repeat<AnimationSet?>(null, 7), new AnimationSet([slash]),
            .. Enumerable.Repeat<AnimationSet?>(null, 8), new AnimationSet([slash with { Clip = new AnimationClip("wave", 0.7f) }])]);
        var goblin = Animated(world, clips: animations);

        goblin.Equip(1, new Item(Staff), "RightHand");
        goblin.ForceAnimation("attack_melee0");
        Assert.Equal("attack_melee0", goblin.Animator!.Clock.Clip!.Name);           // the staff's set is never used

        goblin.Equip(0, new Item(Sword), "RightHand");
        goblin.ForceAnimation("attack_melee0");
        Assert.Equal("slash", goblin.Animator.Clock.Clip!.Name);
    }

    [Fact]
    public void AFreshCharacter_StandsInItsWeaponsIdle()
    {
        using var world = World();
        var idle = new CharacterAnimation("idle", new AnimationClip("sword_idle", 2), 1, 0, true, []);
        var animations = new CharacterAnimations([Clips().DefaultSet, .. Enumerable.Repeat<AnimationSet?>(null, 7), new AnimationSet([idle])]);
        var template = Goblin with { Equipment = [new EquipmentTemplate(0, "RightHand", Vector3.Zero, Sword)] };

        var goblin = Animated(world, template, clips: animations);

        Assert.Equal(8, goblin.Animator!.CurrentSet);
        Assert.Equal("sword_idle", goblin.Animator.Clock.Clip!.Name);
    }

    [Fact]
    public void TheSlotOneResistances_DoNotDependOnTheOrder_AndBuffsStay()
    {
        using var world = World();
        var early = Spawn(world);
        var late = Spawn(world, at: new Vector3(5, 0.9f, 0));
        var ward = new OpenMagicka.Gameplay.Buffs.ResistanceBuff(10, new Resistance(Elements.Fire, 0.5f, 0));
        (early.IsPlayer, _) = (true, 0);
        early.AddBuff(ward);
        early.Equip(Character.ResistanceSlot, new Item(Staff), "RightHand");
        late.Equip(Character.ResistanceSlot, new Item(Staff), "RightHand");
        late.IsPlayer = true;

        Assert.Same(Staff.Resistances, early.Defences.Staff);
        Assert.Same(Staff.Resistances, late.Defences.Staff);
        Assert.Equal(ward.Resistance, Assert.Single(early.Defences.Buffs!));
        late.IsPlayer = false;
        Assert.Null(late.Defences.Staff);
    }

    [Fact]
    public void TheFirstStaff_IsTheSpellSource()
    {
        using var world = World();
        var wizard = Spawn(world);
        Assert.Equal(-1, wizard.SpellSource);

        wizard.Equip(3, new Item(Staff), "RightHand");
        wizard.Equip(5, new Item(Staff), "LeftHand");

        Assert.Equal(3, wizard.SpellSource);
        wizard.Unequip(3);
        Assert.Equal(5, wizard.SpellSource);
    }

    [Fact]
    public void AnAvatarsSlotOneItem_Resists_WithTheTemplate()
    {
        using var world = World();
        var wizard = Spawn(world);
        var goblin = Spawn(world, at: new Vector3(5, 0.9f, 0));
        wizard.Equip(Character.ResistanceSlot, new Item(Staff), "RightHand");
        goblin.Equip(Character.ResistanceSlot, new Item(Staff), "RightHand");
        wizard.IsPlayer = true;

        wizard.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, Vector3.Zero);
        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, Vector3.Zero);

        Assert.Same(Staff.Resistances, wizard.Defences.Staff);
        Assert.True(wizard.HitPoints > goblin.HitPoints, $"{wizard.HitPoints} vs {goblin.HitPoints}");   // only the avatar's staff counts
    }

    [Fact]
    public void ABlock_AddsUpEveryItem_AndOnlyFromInFront()
    {
        using var world = World();
        var knight = Spawn(world);
        Shield(knight, 30);
        knight.Equip(4, new Item(Sword), "RightHand");                                // 30 + 20
        knight.BlockSlot = 4;

        Assert.Equal(50, knight.BlockValue);
        knight.Damage(Earth(100), null, InFront(knight));
        Assert.Equal(250f, knight.HitPoints);
        knight.Damage(Earth(100), null, knight.Position + 2 * Vector3.UnitZ);         // from behind
        Assert.Equal(150f, knight.HitPoints);
        knight.Damage(Earth(100), null, knight.Position + new Vector3(2, 0, -1));     // 63° and more to the side
        Assert.Equal(50f, knight.HitPoints);
    }

    [Fact]
    public void NoBlock_FromTheOwnSpotOrStraightAbove_AndABadPositionIsRejected()
    {
        using var world = World();
        var knight = Spawn(world);
        Shield(knight, 1000);
        knight.BlockSlot = 0;

        knight.Damage(Earth(10), null, knight.Position);
        knight.Damage(Earth(10), null, knight.Position + 3 * Vector3.UnitY);

        Assert.Equal(280f, knight.HitPoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => knight.Damage(Earth(10), null, new Vector3(float.NaN, 0, 0)));
    }

    [Fact]
    public void ABlockOnAnEmptySlot_StillCountsTheOtherItems()
    {
        using var world = World();
        var knight = Spawn(world);
        Shield(knight, 1000);
        knight.BlockSlot = 6;

        var result = knight.Damage(Earth(100), null, InFront(knight));

        Assert.True(result.HasFlag(DamageResult.Deflected));
    }

    [Fact]
    public void WeaponVisibility_ShowsAgain_AndIgnoresAnEmptySlot()
    {
        using var world = World();
        var goblin = Spawn(world);
        var sword = new Item(Sword) { IsVisible = false };
        goblin.Equip(0, sword, "RightHand");

        new WeaponVisibility(0, 0, 0, true).Run(goblin, first: true);
        new WeaponVisibility(0, 0, 5, false).Run(goblin, first: true);

        Assert.True(sword.IsVisible);
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Unequip(Character.Slots));
    }

    [Fact]
    public void WeaponVisibility_HidesTheSlotsItem_AndLeavesItHidden()
    {
        using var world = World();
        var hide = new WeaponVisibility(0, 0.5f, 0, false);
        var goblin = Animated(world, clips: ClipsWith([new CharacterAnimation("special0", new AnimationClip("special0", 1), 1, 0, false, [hide])]));
        var sword = new Item(Sword);
        goblin.Equip(0, sword, "RightHand");
        Run(world, 0.1f);

        goblin.ForceAnimation("special0");
        Run(world, 2);

        Assert.False(sword.IsVisible);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponVisibility(0, 0, Character.Slots, true));
    }

    [Fact]
    public void PassiveAbilities_ComeFromWhatIsHeld()
    {
        using var world = World();
        var wizard = Spawn(world);
        wizard.Equip(1, new Item(new ItemTemplate("swift", PassiveAbility: PassiveAbility.MoveSpeed, PassiveValue: 1.2f)), "RightHand");

        Assert.True(wizard.HasPassiveAbility(PassiveAbility.MoveSpeed));
        Assert.False(wizard.HasPassiveAbility(PassiveAbility.Glow));
        Assert.False(wizard.HasPassiveAbility(PassiveAbility.None));
        wizard.Unequip(1);
        Assert.False(wizard.HasPassiveAbility(PassiveAbility.MoveSpeed));
    }

    [Fact]
    public void BadSlotsAndItems_AreRejected()
    {
        using var world = World();
        var goblin = Spawn(world);

        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Equip(Character.Slots, new Item(Sword), "RightHand"));
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.ItemIn(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sword with { WeaponClass = (WeaponClass)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sword with { MeleeRange = float.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sword with { BlockValue = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Equip(0, new Item(Sword), "RightHand", new Vector3(float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Spawn(Goblin with { Equipment = [new EquipmentTemplate(8, "a", Vector3.Zero, Sword)] }, new Vector3(4, 0.9f, 0)));
    }

    [Fact]
    public void ALaterItemInTheSameSlot_ReplacesTheEarlierOne()
    {
        using var world = World();
        var twice = Goblin with { Equipment = [new EquipmentTemplate(0, "a", Vector3.Zero, Sword), new EquipmentTemplate(0, "b", Vector3.Zero, Staff)] };

        var goblin = world.Spawn(twice, new Vector3(4, 0.9f, 0));

        Assert.Same(Staff, goblin.ItemIn(0)!.Template);
        Assert.Equal("b", goblin.Equipment[0].BoneName);
    }
}
