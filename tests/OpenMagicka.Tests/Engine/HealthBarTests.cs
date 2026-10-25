using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The original HUD's health bars (OM-54 phase A).</summary>
public class HealthBarTests
{
    private static List<HealthBarPicture> Bars(GameWorld world, HealthBarOption option = HealthBarOption.On, bool ui = true)
    {
        var bars = new List<HealthBarPicture>();
        HealthBars.Collect(world.Entities.Entities, option, ui, bars);
        return bars;
    }

    [Fact]
    public void APlayersBar_IsRedAtItsFeet_AlwaysShown_AndFlashesOnABlow()
    {
        using var world = World();
        var wizard = Spawn(world);
        wizard.IsPlayer = true;
        world.Tick(Tick);

        var bar = Assert.Single(Bars(world));
        Assert.Equal((1f, 1.25f, 0f), (bar.Share, bar.Height, bar.Lower));
        Assert.Equal(new Xna.Vector4(1, 0, 0, 1), bar.Colour);
        Assert.Equal(wizard.Position.Y - wizard.Template.Length / 2 - wizard.Template.Radius, bar.At.Y, 4);

        wizard.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, wizard.Position);
        bar = Assert.Single(Bars(world));
        Assert.True(bar.Share < 1);
        Assert.Equal(1 + HealthBars.FlashTime * HealthBars.FlashGain, bar.Colour.X, 4);                  // white flash right after the blow
        Run(world, 0.5f);
        Assert.Equal(new Xna.Vector4(1, 0, 0, 1), Assert.Single(Bars(world)).Colour);                // it faded, the bar stays
    }

    [Fact]
    public void AnNpcsBar_ShowsAfterABlow_ThenFades()
    {
        using var world = World();
        var goblin = Spawn(world);
        world.Tick(Tick);
        Assert.Empty(Bars(world));                                                                   // never hurt: no bar

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1), null, goblin.Position);
        Assert.Equal(1, Assert.Single(Bars(world)).Colour.W);
        Run(world, 2.5f);
        Assert.Equal(0.5f, Assert.Single(Bars(world)).Colour.W, 1);
        Run(world, 1);
        Assert.Empty(Bars(world));

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1), null, goblin.Position);
        Assert.Empty(Bars(world, HealthBarOption.PlayersOnly));
        Assert.Empty(Bars(world, ui: false));
    }

    [Fact]
    public void AFoeOfSeveralBars_StacksThem_RedGreyAndItsShare()
    {
        using var world = World();
        var troll = Spawn(world, Goblin with { HealthBars = 3 });
        troll.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, troll.Position);

        var bars = Bars(world);

        Assert.Equal([0f, 6f, 12f], bars.Select(b => b.Lower));
        Assert.Equal((1f, 1f), (bars[0].Share, bars[1].Share));
        Assert.True(bars[2].Share < 1);
        Assert.Equal(new Xna.Vector3(0.6f, 0.6f, 0.6f), new Xna.Vector3(bars[1].Colour.X, bars[1].Colour.Y, bars[1].Colour.Z));
        Assert.True(bars[2].Colour.X > 2);                                                          // gold, and the blow's flash
    }

    [Fact]
    public void APoisonedPlayer_IsGreen_AndAShieldHasItsBar()
    {
        using var world = World();
        var wizard = Spawn(world);
        wizard.IsPlayer = true;
        wizard.Damage(new DamageEntry(AttackProperties.Status, Elements.Poison, 50, 1), null, wizard.Position);
        var shield = new SpellShield(world.Physics, null, ShieldShape.Sphere, new Numerics.Vector3(5, 1, 0), -Numerics.Vector3.UnitZ, ShieldRules.ShieldRadius, 500);
        world.Entities.Add(shield);
        world.Tick(Tick);

        var bars = Bars(world);

        Assert.Contains(bars, b => b.Colour.Y >= 1 && b.Colour.X < 0.01f);                         // green
        var shieldBar = Assert.Single(bars, b => b.Lower == 12);
        Assert.InRange(shieldBar.Share, 0.09f, 500 / SpellShield.MaxHitPoints);                          // less what it lost in a tick
    }

    [Fact]
    public void ABar_IsAFrameAndAFill_ScaledAndCentredUnderItsPoint()
    {
        var quads = new List<GuiSprite>();
        GuiPass.Bar(new HealthBarPicture(default, 0.5f, 0.75f, 1.25f, new Xna.Vector4(1, 0, 0, 1), 0), new Xna.Vector2(100, 100), 512, 512, quads);

        Assert.Equal(4, quads.Count);
        Assert.Equal(new Xna.Vector2(100.5f - 24 - 3, 116.5f), quads[0].TopLeft);                   // the left cap, 4 units of 0.75
        Assert.Equal(new Xna.Vector2(100.5f + 24 + 3, 126.5f), quads[2].BottomRight);               // 10 px high
        Assert.Equal(new Xna.Vector2(100.5f, 126.5f), quads[3].BottomRight);                        // half the bar filled
        Assert.Equal(new Xna.Vector2(2 / 512f, 81 / 512f), quads[3].UvTopLeft);
        Assert.Equal(new Xna.Vector4(1, 1, 1, 1), quads[0].Colour);
    }
}
