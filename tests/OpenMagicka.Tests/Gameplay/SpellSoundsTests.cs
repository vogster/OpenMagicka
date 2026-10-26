using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The spells' sounds on casting, as the original's table chooses them (SPELLS.md §4.3).</summary>
public class SpellSoundsTests
{
    private static Spell Of(params Elements[] elements) => elements.Aggregate(default(Spell), (spell, element) => spell + Spell.Of(element));

    [Fact]
    public void AForwardCast_PlaysEachElementsSpray_AtItsMagnitude_InTheOriginalsOrder()
    {
        Assert.Equal([("spell_fire_spray", 2f), ("spell_lightning_spray", 1f)],
            SpellSounds.For(Of(Elements.Lightning, Elements.Fire, Elements.Fire), SpellType.Spray, CastType.Force));
    }

    [Fact]
    public void Lightning_PlaysItsSprayForEveryCast()
    {
        foreach (var cast in (CastType[])[CastType.Force, CastType.Area, CastType.Self, CastType.None])
        {
            Assert.Equal("spell_lightning_spray", Assert.Single(SpellSounds.For(Of(Elements.Lightning), SpellType.Lightning, cast)).Cue);
        }
    }

    [Fact]
    public void AreaSelfAndBlastCasts_AndTheBeamsOwn()
    {
        Assert.Equal("spell_fire_area", Assert.Single(SpellSounds.For(Of(Elements.Fire), SpellType.Spray, CastType.Area)).Cue);
        Assert.Equal("spell_fire_blast", Assert.Single(SpellSounds.For(Of(Elements.Fire), SpellType.Spray, CastType.None)).Cue);
        Assert.Equal("spell_arcane_self", Assert.Single(SpellSounds.For(Of(Elements.Arcane), SpellType.Beam, CastType.Self)).Cue);
        Assert.Equal("spell_arcane_area", Assert.Single(SpellSounds.For(Of(Elements.Arcane), SpellType.Beam, CastType.Weapon)).Cue);
        Assert.Equal(["spell_arcane_self"], SpellSounds.For(Of(Elements.Arcane, Elements.Earth), SpellType.Beam, CastType.Self).Select(s => s.Cue));
        Assert.Empty(SpellSounds.For(Of(Elements.Fire), SpellType.Push, CastType.Force));
    }
}
