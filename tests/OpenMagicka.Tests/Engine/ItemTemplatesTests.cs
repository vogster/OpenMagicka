using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;
using DamageEvent = OpenMagicka.Content.Magicka.DamageEvent;
using SoundEvent = OpenMagicka.Content.Magicka.SoundEvent;
using EffectEvent = OpenMagicka.Content.Magicka.EffectEvent;
using EventCondition = OpenMagicka.Gameplay.Items.EventCondition;
using AttackProperties = OpenMagicka.Content.Magicka.AttackProperties;
using ContentElements = OpenMagicka.Content.Magicka.Elements;
using GameElements = OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Engine;

/// <summary>Item files made into item templates (ENTITIES_AI.md §8.1.1).</summary>
public class ItemTemplatesTests
{
    private static readonly ItemRanged NoRanged = new(0, false, 0, 0, 0, 0, 0, 0, 0, "", "", "", 0, "", "", [], null, []);

    private static ItemContent Item(byte weaponClass = 8, byte passive = 0, int block = 20, params ResistanceContent[] resistances) =>
        new("sword_iron", "#item_sword", "#item_sword_d", [], Pickable: true, Bound: false, block, weaponClass, CooldownTime: 0.5f,
            false, false, false, resistances, passive, PassiveAbilityValue: 1.5f, [], [], null, null, new ItemMelee(2.5f, true, []), NoRanged, 1, null, []);

    [Fact]
    public void TheFieldsCarryOver()
    {
        var sword = ItemTemplates.From(Item(passive: 11, resistances: new ResistanceContent(ContentElements.Fire, 0.5f, -10, false)));

        Assert.Equal(("sword_iron", WeaponClass.SlashMedium, 20), (sword.Id, sword.WeaponClass, sword.BlockValue));
        Assert.Equal((PassiveAbility.MoveSpeed, 1.5f, 0.5f), (sword.PassiveAbility, sword.PassiveValue, sword.CooldownTime));
        Assert.Equal((2.5f, true, true, false), (sword.MeleeRange, sword.MeleeMultiHit, sword.Pickable, sword.Bound));
        Assert.Equal((0.5f, -10f), (sword.Resistances![GameElements.Fire].Multiplier, sword.Resistances[GameElements.Fire].Modifier));
        Assert.Equal(1f, sword.Resistances[GameElements.Water].Multiplier);         // a neutral table with overrides
    }

    [Fact]
    public void MeleeEvents_CarryOver_AndTheLaterKindsAreLeftOut()
    {
        EventCollectionContent[] groups =
        [
            new(EventConditionType.Hit, 0, ContentElements.None, 0, 0, true, [
                new DamageEvent(new DamageContent(AttackProperties.Damage | AttackProperties.Knockdown, ContentElements.Earth, 40, 2), false),
                new SoundEvent(SoundBanks.Weapons, "sword_hit", 1, false),
                new SplashEvent(new DamageContent(AttackProperties.Damage, ContentElements.Fire, 10, 1), 3)]),
            new(EventConditionType.Collision, 0, ContentElements.None, 0.5f, 0, false, [new EffectEvent(true, false, "sparks"), new RemoveEvent(0)]),
        ];

        var events = ItemTemplates.Events(groups);

        Assert.Equal(2, events.Count);
        Assert.Equal(new EventCondition(EventTrigger.Hit), events[0].Condition);
        var damage = Assert.IsType<OpenMagicka.Gameplay.Items.DamageEvent>(events[0].Events[0]);
        Assert.Equal(new OpenMagicka.Gameplay.Combat.DamageEntry(OpenMagicka.Gameplay.Combat.AttackProperties.Damage | OpenMagicka.Gameplay.Combat.AttackProperties.Knockdown,
            GameElements.Earth, 40, 2), damage.Damage);
        Assert.Equal(new OpenMagicka.Gameplay.Items.SoundEvent("sword_hit", (int)SoundBanks.Weapons, 1), events[0].Events[1]);
        Assert.Equal(2, events[0].Events.Count);                                     // the splash comes with spells
        Assert.Equal((EventTrigger.Collision, 0.5f, false), (events[1].Condition.Trigger, events[1].Condition.Threshold, events[1].Condition.Repeat));
        Assert.Equal(new OpenMagicka.Gameplay.Items.EffectEvent("sparks", true, false), Assert.Single(events[1].Events));
    }

    [Theory]
    [InlineData("", -1)]
    [InlineData("7", 7)]
    [InlineData("C/3", 131)]
    [InlineData("e/1", 257)]
    [InlineData("A/5", 5)]                                                         // an unknown sheet adds nothing, as the original
    public void TracerSprites_ReadAsTheOriginal(string text, int sprite) => Assert.Equal(sprite, ItemTemplates.Sprite(text));

    [Theory]
    [InlineData((byte)27, (byte)0, 0)]
    [InlineData((byte)0, (byte)17, 0)]
    [InlineData((byte)0, (byte)0, -5)]
    public void BadValues_FailOnLoad(byte weaponClass, byte passive, int block) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemTemplates.From(Item(weaponClass, passive, block)));
}
