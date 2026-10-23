using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class ElementRelationsTests
{
    private static readonly Elements[] Single =
    [
        Elements.Earth, Elements.Water, Elements.Cold, Elements.Fire, Elements.Lightning, Elements.Arcane,
        Elements.Life, Elements.Shield, Elements.Ice, Elements.Steam, Elements.Poison,
    ];

    private static readonly HashSet<(Elements, Elements)> OppositePairs =
    [
        (Elements.Arcane, Elements.Life), (Elements.Fire, Elements.Cold), (Elements.Earth, Elements.Lightning),
        (Elements.Water, Elements.Lightning), (Elements.Shield, Elements.Shield),
    ];

    private static bool Listed(HashSet<(Elements, Elements)> pairs, Elements a, Elements b) => pairs.Contains((a, b)) || pairs.Contains((b, a));

    [Fact]
    public void Opposites_AreExactlyTheFivePairs_ForEveryPairOfSingleElements()
    {
        foreach (var a in Single)
        {
            foreach (var b in Single)
            {
                Assert.True(ElementRelations.Opposites(a, b) == Listed(OppositePairs, a, b), $"{a} / {b}");
            }
        }
    }

    [Fact]
    public void InclusiveOpposites_AddFireAndWater()
    {
        foreach (var a in Single)
        {
            foreach (var b in Single)
            {
                var expected = Listed(OppositePairs, a, b) || Listed([(Elements.Fire, Elements.Water)], a, b);
                Assert.True(ElementRelations.InclusiveOpposites(a, b) == expected, $"{a} / {b}");
            }
        }
    }

    [Fact]
    public void RevertableAndCombinable_AreTheirTwoPairsEach()
    {
        HashSet<(Elements, Elements)> revertable = [(Elements.Cold, Elements.Steam), (Elements.Ice, Elements.Fire)];
        HashSet<(Elements, Elements)> combinable = [(Elements.Water, Elements.Cold), (Elements.Water, Elements.Fire)];
        foreach (var a in Single)
        {
            foreach (var b in Single)
            {
                Assert.True(ElementRelations.Revertable(a, b) == Listed(revertable, a, b), $"revertable {a} / {b}");
                Assert.True(ElementRelations.Combinable(a, b) == Listed(combinable, a, b), $"combinable {a} / {b}");
            }
        }
    }

    [Fact]
    public void Relations_LookAtFlags_SoAMixedEntryMatchesThroughAnyOfThem()
    {
        Assert.True(ElementRelations.Opposites(Elements.Water | Elements.Earth, Elements.Lightning));
        Assert.True(ElementRelations.Opposites(Elements.Arcane | Elements.Life, Elements.Earth));     // both flags of a pair on one side
        Assert.False(ElementRelations.Opposites(Elements.Shield | Elements.Arcane, Elements.Arcane));   // Shield needs Shield on both sides
        Assert.All(Single.Where(e => e != Elements.Shield), e => Assert.True(ElementRelations.Opposites(e, Elements.All)));
    }

    [Theory]
    [InlineData(Elements.Earth, 0)]
    [InlineData(Elements.Fire, 3)]
    [InlineData(Elements.Poison, 10)]
    [InlineData(Elements.All, 15)]
    [InlineData(Elements.Water | Elements.Earth, 2)]   // round(log2(3)): mixed masks land on a neighbour
    public void TheElementIndex_IsTheRoundedLog2(Elements element, int index)
    {
        Assert.Equal(index, ElementIndex.Of(element));
    }

    [Fact]
    public void SingleElements_AreTheElevenFlags()
    {
        Assert.All(Single, e => Assert.True(ElementIndex.IsSingle(e)));
        Assert.False(ElementIndex.IsSingle(Elements.None));
        Assert.False(ElementIndex.IsSingle(Elements.All));
        Assert.False(ElementIndex.IsSingle(Elements.Earth | Elements.Fire));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementIndex.Of(Elements.None));
    }

    [Theory]
    [InlineData(Elements.Lightning, true, false, false, true)]     // lightning while wet
    [InlineData(Elements.Lightning, true, false, true, false)]     // ... unless an item allows it
    [InlineData(Elements.Fire, false, true, false, true)]          // fire while greased
    [InlineData(Elements.Fire, true, false, false, false)]
    [InlineData(Elements.Water, true, true, false, false)]
    public void ConjuringLightningWetOrFireGreased_Backfires(Elements element, bool wet, bool greased, bool wetLightning, bool backfires)
    {
        Assert.Equal(backfires, ElementRelations.Backfires(element, wet, greased, wetLightning));
    }
}
