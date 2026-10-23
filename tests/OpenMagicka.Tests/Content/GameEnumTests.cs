using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Tests.Content;

public class GameEnumTests
{
    [Theory]
    [InlineData("physical", Elements.Earth)]
    [InlineData("Water,Life,Shield", Elements.Water | Elements.Life | Elements.Shield)]
    [InlineData("beams", Elements.Arcane | Elements.Life)]
    [InlineData("instant", Elements.Earth | Elements.Lightning | Elements.Arcane | Elements.Life | Elements.Ice | Elements.Steam)]
    public void Elements_HaveTheOriginalsAliasesAndGroups(string text, Elements expected)
    {
        Assert.Equal(expected, Enum.Parse<Elements>(text, ignoreCase: true));
    }

    [Fact]
    public void Values_MatchTheOriginal()
    {
        Assert.Equal((0x40, 0x3F00, 0xFF), ((int)Factions.WIZARD, (int)Factions.PLAYERS, (int)Factions.NEUTRAL));
        Assert.Equal(Factions.EVIL | Factions.FRIENDLY, Enum.Parse<Factions>("evil,friendly", ignoreCase: true));
        Assert.Equal(ReactTo.Proximity | ReactTo.Attack, Enum.Parse<ReactTo>("proximity, attack", ignoreCase: true));
        Assert.Equal((6, 0x100), ((int)Order.Panic, (int)StatusEffects.Bleeding));
        Assert.Equal(StatusEffects.Healing, StatusEffects.Life);
        Assert.Equal((0, 3, 5), ((int)CastType.Blast, (int)CastType.Self, (int)CastType.Magick));
        Assert.Equal((35, 37, 50), ((int)MagickType.NrOfMagicks, (int)MagickType.Shrink, (int)MagickType.InflictPoisonDamage));
        Assert.Equal(MagickType.Shrink, Enum.Parse<MagickType>("37", ignoreCase: true));
        Assert.Equal((4, 1), ((int)AIEventType.Loop, (int)LoopType.Reverse));
    }
}
