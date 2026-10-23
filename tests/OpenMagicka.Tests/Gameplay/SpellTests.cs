using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class SpellTests
{
    private static Spell S(Elements element) => Spell.Of(element);

    [Theory]
    [InlineData(Elements.Water, Elements.Cold, Elements.Ice)]
    [InlineData(Elements.Water, Elements.Fire, Elements.Steam)]
    [InlineData(Elements.Steam, Elements.Cold, Elements.Water)]
    [InlineData(Elements.Ice, Elements.Fire, Elements.Water)]
    [InlineData(Elements.Fire, Elements.Cold, Elements.None)]
    [InlineData(Elements.Earth, Elements.Lightning, Elements.None)]
    [InlineData(Elements.Poison, Elements.Life, Elements.Life)]       // poison is cancelled, life is not
    [InlineData(Elements.Arcane, Elements.Life, Elements.Arcane | Elements.Life)]
    [InlineData(Elements.Steam, Elements.Lightning, Elements.Steam | Elements.Lightning)]
    public void Adding_MergesAndCancels(Elements a, Elements b, Elements result)
    {
        Assert.Equal(result, (S(a) + S(b)).Element);
        Assert.Equal(result, (S(b) + S(a)).Element);
    }

    [Fact]
    public void Adding_SumsMagnitudes()
    {
        var spell = S(Elements.Steam) + S(Elements.Steam) + S(Elements.Earth);

        Assert.Equal(2, spell[Elements.Steam]);
        Assert.Equal(1, spell[Elements.Earth]);
        Assert.Equal(3, spell.TotalMagnitude);
    }

    [Fact]
    public void AddingThreeElementsOneByOne_EndsInWater_InEitherOrder()
    {
        Assert.Equal(Elements.Water, (S(Elements.Water) + S(Elements.Fire) + S(Elements.Cold)).Element);   // Steam, then Steam + Cold
        Assert.Equal(Elements.Water, (S(Elements.Water) + (S(Elements.Fire) + S(Elements.Cold))).Element); // Fire and Cold cancel first
    }

    [Fact]
    public void Adding_UsesTheOriginalSums_SoMagnitudesCanGoNegative()
    {
        var mixed = new Spell(water: 1, fire: 1, cold: 1) + default(Spell);
        Assert.Equal(Elements.Steam | Elements.Ice, mixed.Element);
        Assert.Equal((-1f, -1f, -1f, 1f, 1f), (mixed[Elements.Water], mixed[Elements.Fire], mixed[Elements.Cold], mixed[Elements.Steam], mixed[Elements.Ice]));
    }

    [Fact]
    public void Of_GivesMagnitudeOneToEachFlag()
    {
        var spell = Spell.Of(Elements.Fire | Elements.Earth);

        Assert.Equal(Elements.Fire | Elements.Earth, spell.Element);
        Assert.Equal((1f, 1f, 2f), (spell[Elements.Fire], spell[Elements.Earth], spell.TotalMagnitude));
    }

    [Fact]
    public void TheIndexer_TakesOneElement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => S(Elements.Fire)[Elements.Fire | Elements.Earth]);
        Assert.Throws<ArgumentOutOfRangeException>(() => S(Elements.Fire)[Elements.None]);
    }

    [Fact]
    public void AMagick_IsAnEntryOfAllElements()
    {
        var magick = Spell.OfMagick(MagickType.Haste);

        Assert.Equal(Elements.All, magick.Element);
        Assert.Equal(MagickType.Haste, magick.Magick);
        Assert.Equal(MagickType.None, Spell.Of(Elements.Fire).Magick);
    }
}
