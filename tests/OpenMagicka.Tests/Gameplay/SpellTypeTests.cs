using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class SpellTypeTests
{
    [Theory]
    [InlineData(Elements.All, SpellType.Magick)]
    [InlineData(Elements.None, SpellType.Push)]
    [InlineData(Elements.Shield | Elements.Earth | Elements.Arcane, SpellType.Shield)]
    [InlineData(Elements.Earth | Elements.Arcane, SpellType.Projectile)]
    [InlineData(Elements.Ice | Elements.Lightning, SpellType.Projectile)]
    [InlineData(Elements.Arcane | Elements.Lightning, SpellType.Beam)]
    [InlineData(Elements.Life, SpellType.Beam)]
    [InlineData(Elements.Lightning | Elements.Fire, SpellType.Lightning)]
    [InlineData(Elements.Lightning | Elements.Steam, SpellType.Spray)]
    [InlineData(Elements.Water | Elements.Cold | Elements.Fire | Elements.Steam | Elements.Poison, SpellType.Spray)]
    public void TheType_FollowsThePriority(Elements elements, SpellType type)
    {
        Assert.Equal(type, SpellTypes.Of(elements));
    }

    [Fact]
    public void ASpellsType_ComesFromItsElements()
    {
        Assert.Equal(SpellType.Lightning, Spell.Of(Elements.Lightning).Type);
        Assert.Equal(SpellType.Magick, Spell.OfMagick(MagickType.Haste).Type);
        Assert.Equal(SpellType.Push, default(Spell).Type);
    }

    [Fact]
    public void Combining_FoldsTheQueueLeftToRight_OrReturnsALeadingMagick()
    {
        Assert.Equal(SpellType.Push, ElementQueue.Empty.Combine().Type);
        var spell = ElementQueueTests.Press("Q F Q F D").Combine();
        Assert.Equal((2f, 1f), (spell.Steam, spell.Earth));
        Assert.Equal(SpellType.Projectile, spell.Type);
        var magick = ElementQueueTests.Press("A S F").WithMagick(MagickType.Haste).Combine();
        Assert.Equal(MagickType.Haste, magick.Magick);
    }

    [Theory]
    [InlineData(SpellType.Spray, 0, 0, 0, 0)]                // no elements: √0
    [InlineData(SpellType.Spray, 5, 0, 0, 1)]                // five fire: radius factor 1
    [InlineData(SpellType.Projectile, 0, 5, 0, 1)]
    [InlineData(SpellType.Beam, 0, 0, 1.25f, 0.5f)]
    [InlineData(SpellType.Lightning, 5, 5, 5, 0)]            // only spray, projectile and beam blasts have a size
    public void TheBlastSize_IsTheRootOfTheTypesBiggestElementOverFive(SpellType type, float fire, float earth, float arcane, float size)
    {
        var spell = new Spell(fire: fire, earth: earth, arcane: arcane);

        Assert.Equal(size, spell.BlastSizeAs(type), 5);
    }

    [Fact]
    public void Normalising_ScalesTheMagnitudesToATotalOfOne()
    {
        var spell = new Spell(fire: 3, earth: 1).Normalized();

        Assert.Equal((0.75f, 0.25f, 1f), (spell.Fire, spell.Earth, spell.TotalMagnitude));
        Assert.Equal(default, default(Spell).Normalized());
        Assert.Equal(4f, new Spell(water: 1, fire: 2, cold: 0.5f, steam: 0.25f, poison: 0.25f, earth: 9).TotalSprayMagnitude);
    }
}
