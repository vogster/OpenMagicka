using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Characters;
using GameElements = OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Engine;

/// <summary>A special ability from the content reader (ENTITIES_AI.md §8.1.7).</summary>
public class SpecialAbilityTemplatesTests
{
    [Fact]
    public void EmptyFields_TakeTheirDefaults()
    {
        var ability = ItemTemplates.Special(new SpecialAbilityContent("Zap", "", "", [Elements.Lightning]));

        Assert.Equal(("Zap", "cast_self", (string?)null, GameElements.Lightning), (ability.Type, ability.Animation, ability.DisplayName, ability.Elements![0]));
    }

    [Fact]
    public void GivenFields_CarryOver() =>
        Assert.Equal(("special1", "#item_specab_bash"), (ItemTemplates.Special(new SpecialAbilityContent("SpecialAnimation", "special1", "#item_specab_bash", [])) is var a
            ? (a.Animation, a.DisplayName) : default));
}
