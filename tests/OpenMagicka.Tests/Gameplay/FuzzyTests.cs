using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The fuzzy expressions of the AI's abilities (ENTITIES_AI.md §7.5).</summary>
public class FuzzyTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static float Weigh(string text, Character self, Character target) =>
        FuzzyExpression.Parse(text).Value(new FuzzyArguments(self, target, 12));

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3*2", 16)]
    [InlineData("!0.25", 0.75)]
    [InlineData("(!0.25-0.5)*10", 2.5)]
    [InlineData("8/2/2", 2)]
    [InlineData(" 0.5 \n+ 0.25", 0.75)]
    public void Operators_BindByRankThenBracket(string text, float expected)
    {
        var (world, a, b) = Pair();
        using var _ = world;
        Assert.Equal(expected, Weigh(text, a, b), 4);
    }

    [Fact]
    public void WhatStandsLeftOfANot_IsDropped_AsTheOriginal()
    {
        var (world, a, b) = Pair();
        using var _ = world;

        Assert.Equal(0.75f, Weigh("5!0.25", a, b), 4);
    }

    [Fact]
    public void TheDistanceBell_IsOneInTheMiddle_AndATenthAtTheEnds()
    {
        Assert.Equal(1, FuzzyMath.DistanceExponential(10, 5, 15), 4);
        Assert.Equal(0.1f, FuzzyMath.DistanceExponential(15, 5, 15), 4);
        Assert.Equal(0.1f, FuzzyMath.DistanceExponential(5, 5, 15), 4);
        Assert.Equal(0.1f, FuzzyMath.DistanceExponential(3, 3), 4);                    // one number: the range (−3, 3)
        Assert.Equal(1, FuzzyMath.DistanceLinear(10, 5, 15), 4);
        Assert.Equal(0.5f, FuzzyMath.DistanceLinear(7.5f, 5, 15), 4);
        Assert.Equal(0, FuzzyMath.Anger(0));
    }

    [Fact]
    public void Variables_AskTheThinkerOrTheTarget()
    {
        var (world, self, target) = Pair();
        using var _ = world;
        target.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 150, 1), null, target.Position);
        target.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 100, 1), null, target.Position);
        Run(world, Tick);

        Assert.Equal(1, Weigh("s.health", self, target), 3);
        Assert.Equal(0.5f, Weigh("t.health", self, target), 2);
        Assert.Equal(1, Weigh("t.status(wet)", self, target));
        Assert.Equal(0, Weigh("s.status(wet)", self, target));
        Assert.Equal(0, Weigh("t.status(wet|burning)", self, target));              // all of them, as the original
        Assert.Equal(FuzzyMath.DistanceExponential(4, 0, 15), Weigh("distExp(0,15)", self, target), 4);
        Assert.Equal(1, Weigh("t.isCharacter * s.isc", self, target));
        Assert.Equal(0, Weigh("t.resistance(cold)", self, target));                  // as the original: asked of no element
        Assert.Equal(1, Weigh("LOS", self, target));
        Assert.Equal(0.5f, Weigh("s.EDensity(10)", self, target), 4);                 // one foe near the thinker
        Assert.Equal(0, Weigh("s.FDensity(10)", self, target), 4);
        Assert.Equal(0.5f, Weigh("FDensity(10)", self, target), 4);                   // no scope: around the target, where the thinker stands
    }

    [Fact]
    public void LineOfSight_IsBlockedByAnotherInTheWay()
    {
        var (world, self, target) = Pair();
        using var _ = world;
        Animated(world, Foe, at: (self.Position + target.Position) / 2);
        Run(world, Tick);

        Assert.Equal(0, Weigh("LOS", self, target));
        Assert.Equal(1, Weigh("!LOS", self, target));
    }

    [Fact]
    public void Danger_GrowsWithTheThinkersOwnQueue()
    {
        var (world, self, target) = Pair();
        using var _ = world;
        var before = Weigh("Danger(Offensive)", self, target);
        self.Conjure(Elements.Fire);
        self.Conjure(Elements.Fire);

        var after = Weigh("Danger(Offensive)", self, target);
        Assert.True(after > before);
        Assert.True(after >= 1 - MathF.Pow(0.25f, 2) - 1e-4f, $"its own two elements count: {after}");
    }

    [Theory]
    [InlineData("status(wet)")]
    [InlineData("s.threat(fire)")]
    [InlineData("nosuchthing(3)")]
    [InlineData("t.status(soggy)")]
    public void Nonsense_IsRefusedWhenRead(string text)
    {
        Assert.Throws<FormatException>(() => FuzzyExpression.Parse(text));
    }

    [Fact]
    public void EqualRanks_FallAsTheOldQuicksortLeavesThem()
    {
        Assert.Equal("((1 - 2) - 3)", FuzzyExpression.Parse("1-2-3").ToString());
        Assert.Equal("((1 - (2 - 3)) - (4 - 5))", FuzzyExpression.Parse("1-2-3-4-5").ToString());   // as .NET 3.5's sort: not left to right
        Assert.Equal("((((1 - 2) - 3) - 4) - 5)", FuzzyExpression.Parse("1-2-3-4-5", leftToRight: true).ToString());
    }

    [Theory]
    [InlineData("Danger")]
    [InlineData("distExp")]
    public void AMissingArgument_IsAFormatError(string text) => Assert.Throws<FormatException>(() => FuzzyExpression.Parse(text));

    /// <summary>Two goblins of different factions 4 apart.</summary>
    private static (OpenMagicka.Gameplay.GameWorld, Character, Character) Pair()
    {
        var world = World();
        var self = Animated(world, Goblin, at: new Vector3(0, 0.9f, 0));
        var target = Animated(world, Foe, at: new Vector3(4, 0.9f, 0));
        Run(world, Tick);
        return (world, self, target);
    }
}
