using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Engine.EffectDirectorTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The standing spells' looks (OM-76): shields, missiles, barriers' and mines' effects.</summary>
public class SpellStandingTests
{
    private static LevelPlay Started()
    {
        var play = Play();
        play.Start();
        play.Tick(Tick);
        Drain(play);
        return play;
    }

    private static void Cast(LevelPlay play, CastType type, params Elements[] elements)
    {
        var wizard = play.Player.Avatar!;
        foreach (var element in elements)
        {
            wizard.Conjure(element);
        }
        wizard.CastType = type;
        wizard.CastSpell(fromStaff: true);
    }

    private static SpellPictures Pictures(LevelPlay play)
    {
        var pictures = new SpellPictures();
        play.Spells.Write(pictures);
        return pictures;
    }

    [Fact]
    public void TheShieldsMeshes_AreTheOriginals_ASphereOpenBelow_AndALensWall()
    {
        var (sphere, sphereIndices) = ShieldDrawing.Sphere();
        var (wall, wallIndices) = ShieldDrawing.Wall();

        Assert.Equal((2015, 11520), (sphere.Length, sphereIndices.Length));
        Assert.All(sphere, v => Assert.Equal(1, v.Position.Length(), 4));
        Assert.True(sphere.Min(v => v.Position.Y) > -0.99f);                                          // the bottom pole open
        Assert.Equal((242, 1200), (wall.Length, wallIndices.Length));
        Assert.Equal(0.2f, wall.Max(v => v.Position.X), 5);
        Assert.Equal(6, wall.Max(v => v.Position.Z), 2);
        Assert.InRange(wall.Max(v => v.Position.Y), 2.9f, 3);
        Assert.All(wallIndices, i => Assert.InRange(i, 0, 241));
    }

    [Fact]
    public void ASphereShield_IsTheShieldsColourTwice_BrightenedByArcaneAndLife()
    {
        Assert.Equal(SpellColours.Shield * 2, ShieldEffect.SphereColour(Spell.Of(Elements.Shield)));
        var both = ShieldEffect.SphereColour(new Spell(shield: 1, arcane: 4));
        Assert.True(Numerics.Vector3.Distance(2 * (SpellColours.Shield + SpellColours.Arcane * 2), both) < 1e-4f, both.ToString());
    }

    [Fact]
    public void AShield_FadesIn_RipplesWhereStruck_AndFadesOutAfterItFalls()
    {
        using var play = Started();
        Cast(play, CastType.Area, Elements.Shield);
        play.Tick(Tick);
        var shield = play.World.Entities.Entities.OfType<SpellShield>().Single();
        play.Tick(Tick);

        var first = Assert.Single(Pictures(play).Shields);
        Assert.Equal(ShieldForm.Sphere, first.Form);
        Assert.InRange(first.Fade, 0.2f, 0.9f);
        Assert.Equal(new Xna.Vector3(4, 3, 2), first.Colour);
        Assert.Equal(4, first.TextureScale);
        Assert.Equal(5.5f, first.World.Right.Length(), 3);                                            // scaled to its radius
        play.Spells.Hear(shield, new ShieldHitCue(shield, new Numerics.Vector3(1, 1, 1), 200));
        play.Tick(Tick);
        var struck = Pictures(play).Shields.Single();
        Assert.Equal(new Xna.Vector4(1, 1, 1, 5), struck.Ripples[0]);                                // 200 × 0.025, whole in its first frame
        shield.Kill();
        play.Tick(Tick);
        play.Tick(Tick);
        Assert.Empty(play.World.Entities.Entities.OfType<SpellShield>());
        Assert.InRange(Assert.Single(Pictures(play).Shields).Fade, 0.01f, 0.99f);                    // gone, still fading
        for (var t = 0f; t < 1; t += Tick)
        {
            play.Tick(Tick);
        }
        Assert.Empty(Pictures(play).Shields);
    }

    [Fact]
    public void ARipple_GoesIntoTheFirstSpentSlot_OrStrengthensOneThatClose()
    {
        using var play = Started();
        Cast(play, CastType.Force, Elements.Shield);
        play.Tick(Tick);
        var look = new SpellVisuals.ShieldLook(play.World.Entities.Entities.OfType<SpellShield>().Single());

        look.Hit(Numerics.Vector3.Zero, 100);
        look.Hit(new Numerics.Vector3(0.1f, 0, 0), 100);
        look.Hit(new Numerics.Vector3(3, 0, 0), 1000);

        Assert.Equal(5, look.Ripples[0].W);                                                           // 2.5, then 2.5 more at 0.1 away
        Assert.Equal(new Xna.Vector4(3, 0, 0, 10), look.Ripples[1]);                                 // at most 10
        look.Update(0.1f, standing: true);
        Assert.Equal(0, look.Ripples[0].W, 4);
        look.Update(1, standing: true);
        Assert.All(look.Ripples, r => Assert.Equal(0, r.W));                                          // spent, not below
        Assert.Equal(ShieldForm.Disc, SpellVisuals.Picture(look).Form);
        Assert.Equal(MathF.Cos(MathF.PI / 4), SpellVisuals.Picture(look).MinDot, 5);
    }

    [Fact]
    public void ASpellsMissile_IsARockByItsEarth_IcyWithIce_AShardForIceAlone()
    {
        Assert.Equal("Models/Missiles/Earth0", SpellMissiles.Model(new Spell(earth: 1)));
        Assert.Equal("Models/Missiles/Earth2", SpellMissiles.Model(new Spell(earth: 3)));
        Assert.Equal("Models/Missiles/Earth3", SpellMissiles.Model(new Spell(earth: 9)));
        Assert.Equal("Models/Missiles/IceEarth1", SpellMissiles.Model(new Spell(earth: 1, ice: 1)));
        Assert.Equal("Models/Missiles/Ice3", SpellMissiles.Model(new Spell(ice: 2)));
        Assert.Null(SpellMissiles.Model(new Spell(fire: 2)));
    }

    [Fact]
    public void AMissile_IsDrawnAlongItsFlight_ThreeTimes_AndLitByItsElements()
    {
        using var world = World();
        var missile = new Missile(world.Physics, null, new Numerics.Vector3(0, 2, 0), new Numerics.Vector3(10, 0, 0), 0.2f, []) { Look = "Models/Missiles/Earth0" };
        world.Entities.Add(missile);
        var models = new List<DrawnModel>();

        EntityPictures.Collect(world, _ => null, models);

        Assert.Equal(EntityPictures.MissileCopies, models.Count(m => m.Asset == "Models/Missiles/Earth0"));
        var at = models[0].World;
        Assert.True(Numerics.Vector3.Distance(Numerics.Vector3.UnitX, Numerics.Vector3.TransformNormal(-Numerics.Vector3.UnitZ, at)) < 1e-4f);
        Assert.Equal(new Numerics.Vector3(0, 2, 0), at.Translation);
        Assert.Equal((SpellColours.Fire + SpellColours.Cold) / 2, SpellVisuals.MissileLight(Elements.Fire | Elements.Cold | Elements.Earth));
        Assert.Null(SpellVisuals.MissileLight(Elements.Earth | Elements.Water));
    }

    [Fact]
    public void AnElementalBarrier_IsAWallOfItsElement_TheStrongest_SteamWithLightning()
    {
        Assert.Equal(Elements.Fire, SpellVisuals.WallElement(new Spell(shield: 1, fire: 2, water: 1)));
        Assert.Equal(Elements.Steam, SpellVisuals.WallElement(new Spell(shield: 1, lightning: 1, steam: 1)));
        Assert.Equal(Elements.Lightning, SpellVisuals.WallElement(new Spell(shield: 1, lightning: 1, cold: 3)));
        Assert.Equal(Elements.Water, SpellVisuals.WallElement(new Spell(shield: 1, water: 1, cold: 1)));       // the first of equals
        Assert.Null(SpellVisuals.WallElement(Spell.Of(Elements.Shield)));
    }

    [Fact]
    public void ABarrierOfFire_StandsAsItsWallOfFlames_ThatFollowsIt()
    {
        using var play = Started();
        Cast(play, CastType.Force, Elements.Shield, Elements.Fire);
        for (var t = 0f; t < 0.3f; t += Tick)
        {
            play.Tick(Tick);
        }

        var walls = Drain(play).Where(c => c is { Kind: EffectCommandKind.Start, Effect: "barrier_fire1_b" }).ToList();
        Assert.NotEmpty(walls);
        Assert.Equal(play.World.Entities.Entities.OfType<OpenMagicka.Gameplay.Spells.Effects.Barrier>().Count(), walls.Count);
    }
}
