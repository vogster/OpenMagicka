using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Engine.EffectDirectorTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The spells' bursts (OM-76): bolts, blasts and their discs and lights, tracers, gunfire.</summary>
public class SpellBurstTests
{
    private static SpellPictures Pictures(LevelPlay play)
    {
        var pictures = new SpellPictures();
        play.Spells.Write(pictures);
        return pictures;
    }

    private static LevelPlay Started()
    {
        var play = Play();
        play.Start();
        play.Tick(Tick);                                                                              // into the scene
        Drain(play);
        return play;
    }

    [Fact]
    public void ABoltsZigzag_StartsOnItsAxis_OneAcross_PairsTwoApart_OnTheLightningRow()
    {
        var zigzag = BurstDrawing.Zigzag(3);

        Assert.Equal(BurstDrawing.Pairs * 2, zigzag.Length);
        Assert.Equal((0.5f, -0.5f), (zigzag[0].Position.X, zigzag[1].Position.X));
        Assert.All(Enumerable.Range(0, BurstDrawing.Pairs), k => Assert.Equal(1, zigzag[k * 2].Position.X - zigzag[k * 2 + 1].Position.X, 5));
        Assert.All(Enumerable.Range(1, BurstDrawing.Pairs - 1), k => Assert.InRange(zigzag[k * 2].Position.X, -0.25f, 1.25f));
        Assert.Equal(-32, zigzag[^1].Position.Z);
        Assert.Equal((0.375f, 0.5f), (zigzag[0].TextureCoordinate.Y, zigzag[1].TextureCoordinate.Y));
        Assert.Equal(zigzag.Select(v => v.Position), BurstDrawing.Zigzag(3).Select(v => v.Position));   // a shape is fixed
    }

    [Fact]
    public void ABoltsStrip_IsStretchedFromItsStartToItsEnd_InEvenSteps_AtMost32()
    {
        var bolt = new BoltPicture(new Xna.Vector3(1, 1, 1), new Xna.Vector3(1, 1, -4.8f), Xna.Vector3.One, 1, 0);

        Assert.Equal((0, 4, 32), (BurstDrawing.Primitives(0.5f), BurstDrawing.Primitives(4.8f), BurstDrawing.Primitives(200)));
        var place = BurstDrawing.BoltPlace(bolt, new Xna.Vector3(1, 50, 0));
        Assert.Equal(6, BurstDrawing.Primitives(5.8f));
        Assert.True(Xna.Vector3.Distance(bolt.To, Xna.Vector3.Transform(new Xna.Vector3(0, 0, -6), place)) < 1e-4f);
        Assert.Equal(1, Xna.Vector3.TransformNormal(Xna.Vector3.UnitX, place).Length(), 4);         // 1 across, not stretched
    }

    [Fact]
    public void ABolt_StartsItsEffects_ShowsBrightThenFades_LitAlong_AndGoes()
    {
        using var play = Started();
        var cue = new LightningCue(new Numerics.Vector3(0, 1, 0), new Numerics.Vector3(4, 1, 0), false, SpellColours.Lightning);

        play.Spells.Hear(null, cue);
        play.Tick(Tick);

        Assert.Equal(["lightning_hit", "lightning_source"], Drain(play).Where(c => c.Kind == EffectCommandKind.Start).Select(c => c.Effect).Order());
        var pictures = Pictures(play);
        var bolt = Assert.Single(pictures.Bolts);
        Assert.Equal(MathF.Sqrt((0.25f - Tick) * 10), bolt.Brightness, 4);
        Assert.Equal(new Xna.Vector3(0.75f, 0.5f, 1) * 1.5f * MathF.Sqrt(0.25f - Tick), Assert.Single(pictures.Lights).Diffuse);
        Run(play, 0.25f);
        Assert.Empty(Pictures(play).Bolts);
    }

    [Fact]
    public void ABlast_ThrowsItsEffectEightWays_AsFarAsEachReaches()
    {
        using var play = Started();
        float[] reach = [1, 1, 0.5f, 1, 1, 1, 1, 1];
        var blast = new BlastCue(Elements.Fire, new Numerics.Vector3(2, 0, 3), 10, 1, Ground: false, Arcane: false, reach);

        play.Spells.Hear(null, blast);
        play.Tick(Tick);

        var starts = Drain(play).Where(c => c.Kind == EffectCommandKind.Start).ToList();
        Assert.Equal(8, starts.Count);
        Assert.All(starts, s => Assert.Equal("area_fire1_b", s.Effect));
        var quarter = SpellVisuals.BlastWay(blast, 2);                                                // a quarter round, cut to half
        Assert.True(Numerics.Vector3.Distance(new Numerics.Vector3(0, 0, -0.5f), new Numerics.Vector3(quarter.M11, quarter.M12, quarter.M13)) < 1e-4f);
        Assert.Equal(new Numerics.Vector3(0, 1, 0), new Numerics.Vector3(quarter.M21, quarter.M22, quarter.M23));   // up not scaled
        Assert.Equal(blast.Position, quarter.Translation);
        Assert.Empty(Pictures(play).Points);                                                          // a blast in the air has no light
    }

    [Fact]
    public void AGroundBlast_ThrowsItsGroundEffect_AndLightsAWhile()
    {
        using var play = Started();
        var blast = new BlastCue(Elements.Cold, Numerics.Vector3.Zero, 4, 1, Ground: true, Arcane: false, [.. Enumerable.Repeat(1f, 8)]);

        play.Spells.Hear(null, blast);
        play.Tick(Tick);

        Assert.All(Drain(play).Where(c => c.Kind == EffectCommandKind.Start), s => Assert.Equal("area_ground_cold", s.Effect));
        var light = Assert.Single(Pictures(play).Points);
        Assert.Equal(8, light.Radius);
        Run(play, 1.2f);
        Assert.Single(Pictures(play).Points);
        Run(play, 0.1f);
        Assert.Empty(Pictures(play).Points);                                                          // up within its 1 s, then 0.25 s down
    }

    [Fact]
    public void AnArcaneBlast_SpreadsItsDisc_TwoSquaresTurning_AndItsElementsExplode()
    {
        using var play = Started();
        var blast = new BlastCue(Elements.Arcane | Elements.Fire | Elements.Shield, Numerics.Vector3.Zero, 3, 1, false, Arcane: true, [.. Enumerable.Repeat(1f, 8)]);

        play.Spells.Hear(null, blast);
        play.Tick(Tick);

        Assert.Equal(["beam_arcane_explosion", "beam_fire_explosion"], Drain(play).Where(c => c.Kind == EffectCommandKind.Start).Select(c => c.Effect).Order());
        var disc = Assert.Single(Pictures(play).Discs);
        Assert.Equal(5 * Tick * 3, disc.Radius, 4);                                                   // it grows from nothing
        Assert.Equal(1, disc.Alpha);
        Assert.Single(Pictures(play).Points);
        Run(play, 0.4f);
        Assert.Empty(Pictures(play).Discs);
    }

    [Fact]
    public void APassingLight_ComesUp_StaysItsTime_ThenGoesFromFull()
    {
        var light = new SpellVisuals.PassingLight(Numerics.Vector3.Zero, Numerics.Vector3.One, 2, 6, 2, 0.25f);

        light.Update(0.2f);
        Assert.Equal(0.4f, light.Brightness, 4);
        light.Update(0.05f);
        Assert.Equal(1, light.Brightness);                                                            // its time ran out half way up: full
        light.Update(0.25f);
        Assert.Equal(0.5f, light.Brightness, 4);
        light.Update(0.25f);
        Assert.True(light.IsOut);
    }

    [Fact]
    public void ATracer_FliesItsShot_AsOneParticle_FromAUnitPastTheMuzzle()
    {
        var tracer = new TracerCue(Numerics.Vector3.Zero, new Numerics.Vector3(0, 0, -22), 100, 35, 0);

        var particle = SpellVisuals.Tracer(tracer)!.Value;

        Assert.Equal(new Xna.Vector3(0, 0, -1), particle.Position);
        Assert.Equal(new Xna.Vector3(0, 0, -100), particle.Velocity);
        Assert.Equal(0.2f, particle.TTL, 5);
        Assert.Equal((35, 1f, 1f), (particle.Sprite, particle.Drag, particle.StartSize));
        Assert.Null(SpellVisuals.Tracer(tracer with { To = new Numerics.Vector3(0, 0, -1.5f) }));   // too short to fly
        Assert.Null(SpellVisuals.Tracer(tracer with { Speed = 0 }));                                  // a gun whose tracers do not fly
    }

    [Fact]
    public void ATracer_WaitsItsDelay_ThenGoesToThePool()
    {
        using var play = Started();

        play.Spells.Hear(null, new TracerCue(Numerics.Vector3.Zero, new Numerics.Vector3(0, 0, -22), 100, 35, 0.05f));
        play.Tick(Tick);
        Assert.DoesNotContain(Drain(play), c => c.Kind == EffectCommandKind.Particle);
        Run(play, 0.05f);
        Assert.Single(Drain(play), c => c.Kind == EffectCommandKind.Particle);
    }

    [Fact]
    public void AGun_FlashesAtItsMuzzle_AndThrowsShellsWhileItFires()
    {
        using var play = Started();
        var gun = new Item(new ItemTemplate("rifle", WeaponClass.Rifle, Attach0: Numerics.Matrix4x4.CreateTranslation(0, 0, -1),
            Gun: new GunTemplate(30, 10, 5, 1, 100, [], Muzzle: "muzzle_flash", Shells: "shells")));

        play.Spells.Hear(null, new MuzzleCue(gun));
        play.Spells.Hear(null, new GunfireCue(gun, Firing: true));
        play.Tick(Tick);
        var started = Drain(play).Where(c => c.Kind == EffectCommandKind.Start).ToList();
        Assert.Equal(new Numerics.Vector3(0, 0, -1), Assert.Single(started, c => c.Effect == "muzzle_flash").World.Translation);
        var shells = Assert.Single(started, c => c.Effect == "shells");

        play.Spells.Hear(null, new GunfireCue(gun, Firing: false));
        play.Tick(Tick);
        Assert.Contains(Drain(play), c => c.Kind == EffectCommandKind.Stop && c.Id == shells.Id);
    }

    private static void Run(LevelPlay play, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            play.Tick(Tick);
        }
    }
}
