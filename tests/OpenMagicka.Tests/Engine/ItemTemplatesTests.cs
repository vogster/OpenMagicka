using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Items;
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

    [Theory]
    [InlineData((byte)27, (byte)0, 0)]
    [InlineData((byte)0, (byte)17, 0)]
    [InlineData((byte)0, (byte)0, -5)]
    public void BadValues_FailOnLoad(byte weaponClass, byte passive, int block) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemTemplates.From(Item(weaponClass, passive, block)));
}
