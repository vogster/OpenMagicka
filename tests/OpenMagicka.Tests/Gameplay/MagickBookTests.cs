using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class MagickBookTests
{
    /// <summary>Docs/gameplay/SPELLS.md §10.3: each magick with one of the shortest key layouts.</summary>
    public static TheoryData<MagickType, string> Magicks => new()
    {
        { MagickType.Revive, "W A" }, { MagickType.Grease, "Q D W" }, { MagickType.Haste, "A S F" },
        { MagickType.Invisibility, "S E Q S F" }, { MagickType.Teleport, "A S A" }, { MagickType.Fear, "R S E" },
        { MagickType.Charm, "W E D" }, { MagickType.ThunderB, "Q F A S A" }, { MagickType.Rain, "Q Q F" },
        { MagickType.Tornado, "D Q F Q Q F" }, { MagickType.Blizzard, "R R Q R" }, { MagickType.MeteorS, "F D F Q D F" },
        { MagickType.Conflagration, "Q F F F Q F F Q" }, { MagickType.ThunderS, "Q Q F F A S A" }, { MagickType.TimeWarp, "R E" },
        { MagickType.Vortex, "Q R S Q E Q R R" }, { MagickType.SUndead, "Q R D S R" }, { MagickType.SElemental, "S E D Q S F" },
        { MagickType.SDeath, "S R R Q R S" }, { MagickType.SPhoenix, "W A F" }, { MagickType.Nullify, "S E" },
        { MagickType.Corporealize, "S Q F A E S" }, { MagickType.CTD, "A A F W" }, { MagickType.Napalm, "Q D W F F F" },
        { MagickType.Portal, "Q F A E" }, { MagickType.TractorPull, "D S" }, { MagickType.ProppMagick, "F F Q S" },
        { MagickType.Levitate, "Q S Q F F" }, { MagickType.ChainLightning, "A A A" }, { MagickType.Confuse, "S E A" },
        { MagickType.Wave, "D Q D Q D F F" }, { MagickType.PerformanceEnchantment, "W F A F W" },
        { MagickType.JudgementSpray, "Q Q R R S E" }, { MagickType.Amalgameddon, "S Q R A F" },
    };

    [Theory]
    [MemberData(nameof(Magicks))]
    public void EveryMagick_IsRecognised_FromItsKeysThroughTheQueue(MagickType magick, string keys)
    {
        var queue = ElementQueueTests.Press(keys);

        Assert.Equal(MagickBook.Sequences[magick], queue.Entries.Select(e => e.Element));
        Assert.Equal(magick, MagickBook.Recognize(queue.Entries));
        Assert.Equal(magick, MagickBook.Hint(queue.Entries));
    }

    [Fact]
    public void CastingAMagick_LeavesOneMagickEntry()
    {
        var cast = ElementQueueTests.Press("A S F").WithMagick(MagickType.Haste);

        Assert.Equal(Elements.All, Assert.Single(cast.Entries).Element);
        Assert.Equal(MagickType.Haste, cast.Entries[0].Magick);
    }

    [Fact]
    public void TheBook_HasThe34PlayerMagicks_OnePathEach()
    {
        Assert.Equal(34, MagickBook.Sequences.Count);
        Assert.Equal(Enumerable.Range(1, 34).Select(i => (MagickType)i), MagickBook.Sequences.Keys.Order());
        Assert.Equal(34, MagickBook.Sequences.Values.Select(s => string.Join(",", s)).Distinct().Count());
        Assert.Equal(35, (int)MagickType.NrOfMagicks);
    }

    [Fact]
    public void Recognising_NeedsTheWholePath_EndingOnAMagick()
    {
        Assert.Equal(MagickType.None, MagickBook.Recognize([]));
        Assert.Equal(MagickType.None, MagickBook.Recognize(ElementQueueTests.Press("W").Entries));        // a prefix of Revive
        Assert.Equal(MagickType.None, MagickBook.Recognize(ElementQueueTests.Press("W A E").Entries));    // no Revive-then-Shield path
        Assert.Equal(MagickType.SPhoenix, MagickBook.Recognize(ElementQueueTests.Press("W A F").Entries)); // Revive's path goes on
        Assert.Equal(MagickType.None, MagickBook.Recognize([Spell.OfMagick(MagickType.Haste)]));
    }

    [Fact]
    public void TheHint_TakesTheLastMagickReached_AndRestartsWhereThePathBreaks()
    {
        Assert.Equal(MagickType.Revive, MagickBook.Hint(ElementQueueTests.Press("W A E").Entries));     // breaks after Revive
        Assert.Equal(MagickType.None, MagickBook.Hint(ElementQueueTests.Press("W").Entries));
        Assert.Equal(MagickType.None, MagickBook.Hint(ElementQueueTests.Press("R W A").Entries));       // Cold, then no Cold-Life: W is used up, A restarts
        Assert.Equal(MagickType.Revive, MagickBook.Hint(ElementQueueTests.Press("E W A").Entries));     // no path starts with Shield, then Revive
        Assert.Equal(MagickType.None, MagickBook.Hint(ElementQueueTests.Press("W A E D").Entries));   // breaks after Revive, restarts at D
    }
}
