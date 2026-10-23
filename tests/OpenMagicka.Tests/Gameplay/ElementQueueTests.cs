using OpenMagicka.Gameplay.Spells;
using El = OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Gameplay;

public class ElementQueueTests
{
    /// <summary>The default keyboard layout: Q Water, W Life, E Shield, R Cold, A Lightning, S Arcane, D Earth, F Fire.</summary>
    internal static El Key(char key) => key switch
    {
        'Q' => El.Water,
        'W' => El.Life,
        'E' => El.Shield,
        'R' => El.Cold,
        'A' => El.Lightning,
        'S' => El.Arcane,
        'D' => El.Earth,
        'F' => El.Fire,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    internal static ElementQueue Press(string keys, ElementQueue? start = null) =>
        keys.Where(c => c != ' ').Aggregate(start ?? ElementQueue.Empty, (queue, key) => queue.Conjure(Key(key)).Queue);

    private static El[] Kinds(ElementQueue queue) => queue.Entries.Select(e => e.Element).ToArray();

    [Theory]
    [InlineData("Q F", new[] { El.Steam })]
    [InlineData("Q R", new[] { El.Ice })]
    [InlineData("R Q", new[] { El.Ice })]
    [InlineData("Q A", new El[0])]
    [InlineData("E E", new El[0])]
    [InlineData("Q F R", new[] { El.Water })]
    [InlineData("Q R F", new[] { El.Water })]
    [InlineData("F D Q", new[] { El.Steam, El.Earth })]
    [InlineData("S Q R A F", new[] { El.Arcane, El.Water, El.Lightning })]
    [InlineData("D A D", new[] { El.Earth })]
    [InlineData("S W S", new[] { El.Arcane })]
    public void TheSpecsExamples_EndInTheseQueues(string keys, El[] expected)
    {
        Assert.Equal(expected, Kinds(Press(keys)));
    }

    [Theory]
    [InlineData("S Q R A F W", new El[0])]                                  // Life cancels Arcane, then Lightning cancels Water
    [InlineData("S Q R A F S", new[] { El.Arcane, El.Arcane })]
    [InlineData("S Q R A F D", new[] { El.Arcane, El.Water })]              // Earth cancels the Lightning before it
    public void AfterAnOppositePair_TheWalkGoesOnWithTheEntryBeforeIt(string keys, El[] expected)
    {
        // S Q R A F leaves [Arcane, Water, Lightning], an opposite pair the queue keeps (§2.2).
        Assert.Equal(expected, Kinds(Press(keys)));
    }

    [Fact]
    public void Conjuring_SaysWhetherTheQueueGrew_AndLeavesTheOldQueueAsItWas()
    {
        var one = Press("D");

        var (two, added) = one.Conjure(El.Fire);
        var (merged, grew) = two.Conjure(El.Water);

        Assert.True(added);
        Assert.False(grew);                                  // Water merged into Steam: the queue stays at 2
        Assert.Single(one.Entries);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void AFullQueue_RemovesTheLastOpposite_InsteadOfAddingTheElement()
    {
        var full = Press("D D D D S");

        var (after, added) = full.Conjure(El.Lightning);

        Assert.False(added);
        Assert.Equal([El.Earth, El.Earth, El.Earth, El.Arcane], Kinds(after));
    }

    [Fact]
    public void AFullQueue_MergesARevertableOrCombinableEntryInPlace_OrDropsTheElement()
    {
        var full = Press("D D Q F D D");                     // Earth, Earth, Steam, Earth, Earth

        Assert.Equal(El.Water, full.Conjure(El.Cold).Queue.Entries[2].Element);
        var waters = Press("Q D D D D");
        Assert.Equal(El.Steam, waters.Conjure(El.Fire).Queue.Entries[0].Element);
        var (same, added) = full.Conjure(El.Arcane);
        Assert.False(added);
        Assert.Equal(Kinds(full), Kinds(same));
    }

    [Fact]
    public void AFullQueue_PrefersAnOpposite_ThenARevertable_ThenACombinable()
    {
        var opposite = Press("Q F F D D D");                 // Steam, Fire, Earth, Earth, Earth
        var revertable = Press("Q F D D D Q");               // Steam, Earth, Earth, Earth, Water

        Assert.Equal([El.Steam, El.Earth, El.Earth, El.Earth], Kinds(opposite.Conjure(El.Cold).Queue));
        var (merged, added) = revertable.Conjure(El.Cold);
        Assert.Equal([El.Water, El.Earth, El.Earth, El.Earth, El.Water], Kinds(merged));
        Assert.False(added);
    }

    [Fact]
    public void LightningPrecedence_ReplacesTheLastNonLightningEntry_WhenNothingElseApplies()
    {
        var full = Press("S S S S S");

        var after = full.Conjure(El.Lightning, lightningPrecedence: true).Queue;
        var plain = full.Conjure(El.Lightning).Queue;
        var other = full.Conjure(El.Shield, lightningPrecedence: true).Queue;   // the flag does not look at the element (§2.2)

        Assert.Equal(El.Lightning, after.Entries[4].Element);
        Assert.Same(full, plain);
        Assert.Equal(El.Lightning, other.Entries[4].Element);
    }

    [Fact]
    public void AnElementConjuredOntoAMagick_RemovesTheMagick()
    {
        var (after, added) = Press("A S F").WithMagick(MagickType.Haste).Conjure(El.Fire);

        Assert.Empty(after.Entries);
        Assert.False(added);
    }

    [Fact]
    public void OnlySingleElementsAreConjured_AndOnlyMagicksReplaceTheQueue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementQueue.Empty.Conjure(El.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementQueue.Empty.Conjure(El.All));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementQueue.Empty.Conjure(El.Earth | El.Fire));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementQueue.Empty.WithMagick(MagickType.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementQueue.Empty.WithMagick(MagickType.NrOfMagicks));
    }

    [Fact]
    public void AnySpell_CanBeAdded_AsAWeaponsQueueDoes()
    {
        var half = new Spell(fire: 0.5f);

        var (queue, added) = Press("Q").Add(half);

        Assert.False(added);
        Assert.Equal(El.Steam | El.Water, queue.Entries[0].Element);   // half the Water boiled
        Assert.Equal((0.5f, 0.5f), (queue.Entries[0].Steam, queue.Entries[0].Water));
    }

    [Fact]
    public void TheCapacity_IsFiveForPlayers_AndChosenForOthers()
    {
        Assert.Equal(5, ElementQueue.Empty.Capacity);
        var npc = new ElementQueue(2);

        Assert.Equal(2, Press("D D D", npc).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElementQueue(0));
    }
}
