using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Tests.Content;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The spells' animated models (OM-76): barriers, mines, the self shields.</summary>
public class SpellModelTests
{
    private const string Effects = "Models/Effects/";

    [Fact]
    public void ASolidBarrier_IsItsIceAndItsRock_AsTheOriginalPicksThem()
    {
        Assert.Equal([(Effects + "iceBarrier0_mesh", Effects + "iceBarrier_animation")], SpellVisuals.BarrierModels(new Spell(shield: 1, ice: 1)));
        Assert.Equal(Effects + "icebarrier_geyser", SpellVisuals.BarrierModels(new Spell(shield: 1, ice: 1, fire: 1)).Single().Mesh);
        Assert.Equal(Effects + "icebarrier_tesla", SpellVisuals.BarrierModels(new Spell(shield: 1, ice: 1, lightning: 1)).Single().Mesh);
        Assert.Equal(Effects + "iceBarrier0_mesh", SpellVisuals.BarrierModels(new Spell(shield: 1, ice: 1, fire: 1, arcane: 1)).Single().Mesh);
        Assert.Equal([Effects + "iceBarrier_Earth", Effects + "earthBarrier0_mesh"], SpellVisuals.BarrierModels(new Spell(shield: 1, ice: 1, earth: 1)).Select(m => m.Mesh));
        Assert.Equal(Effects + "earthBarrier_volcano", SpellVisuals.BarrierModels(new Spell(shield: 1, earth: 1, steam: 1)).Single().Mesh);
        Assert.Equal(Effects + "earthBarrier0_mesh", SpellVisuals.BarrierModels(new Spell(shield: 1, earth: 1, lightning: 1)).Single().Mesh);   // no volcano of lightning
        Assert.Empty(SpellVisuals.BarrierModels(new Spell(shield: 1, fire: 1)));
    }

    [Fact]
    public void AJet_IsOfLightning_ElseOfTheStrongestSprayedElement()
    {
        Assert.Equal(Elements.Lightning, SpellVisuals.JetElement(new Spell(lightning: 1, fire: 5)));
        Assert.Equal(Elements.Steam, SpellVisuals.JetElement(new Spell(steam: 2, water: 1)));
        Assert.Equal(Elements.Cold, SpellVisuals.JetElement(new Spell(cold: 1, fire: 1)));                 // the first of equals
        Assert.Null(SpellVisuals.JetElement(new Spell(earth: 1)));
    }

    [Fact]
    public void AModelAtPlay_IsPosedByItsClip_HeldAtItsEnds_AndPlaysBackwardsFromItsEnd()
    {
        var skeleton = new Skeleton([
            new SkeletonBone("root", -1, BonePose.Identity, Numerics.Matrix4x4.Identity),
            new SkeletonBone("tip", 0, new BonePose(new Numerics.Vector3(0, 1, 0), Numerics.Quaternion.Identity, Numerics.Vector3.One), Numerics.Matrix4x4.Identity)]);
        var rise = new AnimationClip("emerge0", 0.5f, new Dictionary<string, IReadOnlyList<Keyframe>>
        {
            ["root"] = [new Keyframe(0, new BonePose(new Numerics.Vector3(0, -2, 0), Numerics.Quaternion.Identity, Numerics.Vector3.One)),
                new Keyframe(0.5f, BonePose.Identity)],
        });
        var posed = new SpellVisuals.Posed("rock", new AnimatedModel(skeleton, new Dictionary<string, AnimationClip> { ["emerge0"] = rise }), rise, 2,
            Numerics.Matrix4x4.CreateTranslation(5, 0, 0));

        posed.Update(0.125f);
        Assert.Equal(new Numerics.Vector3(5, -1, 0), posed.Placed[0].Translation);                     // half way up at twice the speed
        Assert.Equal(new Numerics.Vector3(5, 0, 0), posed.Placed[1].Translation);                      // the bone under it at its bind
        posed.Update(1);
        Assert.Equal(new Numerics.Vector3(5, 0, 0), posed.Placed[0].Translation);                      // held at the end
        posed.Sink(rise);
        Assert.Equal(0.5f, posed.Time);
        posed.Update(0.25f);
        Assert.Equal(new Numerics.Vector3(5, -2, 0), posed.Placed[0].Translation);                     // back into the ground
        posed.Play(rise, 2);
        posed.Update(0.05f);
        posed.Sink(rise);
        Assert.Equal(0.1f, posed.Time, 4);                                                             // broken as it rose: it sinks from there
    }

    [Fact]
    public void ABubbleShimmers_FromThreeSides_AndFlashesAfterABlow()
    {
        var projections = BubbleDrawing.Projections(Xna.Vector3.Zero, Xna.Vector3.Forward);
        Assert.Equal(3, projections.Length);
        Assert.Equal(projections[0] * Xna.Matrix.CreateRotationY(Xna.MathHelper.TwoPi / 3), projections[1]);

        var look = new SpellVisuals.SelfShieldLook();
        Assert.Equal(SpellVisuals.BubbleBrightness, look.Brightness);
        look.SinceHit = 0;
        Assert.Equal(4 + 0.333f * 60, look.Brightness, 3);
    }

    [Fact]
    public void ABarrierStands_OnItsGround_AlongItsWay_ScaledByIt()
    {
        using var world = OpenMagicka.Tests.Gameplay.CharacterTests.World();
        var wizard = OpenMagicka.Tests.Gameplay.CharacterTests.Spawn(world);
        foreach (var element in new[] { Elements.Shield, Elements.Earth })
        {
            wizard.Conjure(element);
        }
        wizard.CastType = CastType.Force;
        wizard.CastSpell(fromStaff: true);
        OpenMagicka.Tests.Gameplay.CharacterTests.Run(world, 0.2f);
        var barrier = world.Entities.Entities.OfType<OpenMagicka.Gameplay.Spells.Effects.Barrier>().First();

        var root = SpellVisuals.BarrierRoot(barrier);

        Assert.True(Numerics.Vector3.Distance(barrier.Ground with { Y = barrier.Ground.Y + 0.05f * (1 - barrier.Scale) }, root.Translation) < 1e-4f);
        Assert.Equal(barrier.Scale, new Numerics.Vector3(root.M21, root.M22, root.M23).Length(), 4);
        Assert.Equal(barrier.Scale, root.M22, 4);                                                       // upright
    }

    [GameContentFact]
    public void TheGames_BarrierMineAndArmourModels_LoadWithTheirBonesAndClips()
    {
        var models = AnimatedModels.Loader(new OpenMagicka.Content.ContentLoader(
            new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka));

        foreach (var (mesh, clips) in new[] { new Spell(ice: 1), new Spell(ice: 1, fire: 1), new Spell(ice: 1, lightning: 1), new Spell(ice: 1, earth: 1),
            new Spell(earth: 1, water: 1) }.SelectMany(SpellVisuals.BarrierModels).Distinct())
        {
            Assert.True(models(mesh) is { Skeleton.Bones.Count: > 0 }, mesh);
            Assert.True(models(clips) is { Clips.Count: > 0 }, clips);
        }
        Assert.All(new[] { "icebarrier_geyser", "icebarrier_tesla", "earthBarrier_volcano" }, m => Assert.True(models(Effects + m)!.Skeleton.IndexOf("effect0") >= 0, m));
        Assert.Equal(5, models(Effects + "earthBarrier_animation")!.Clips.Count);
        Assert.Contains("Take 001", models(Effects + "mine")!.Clips.Keys);
        Assert.NotNull(models(SpellVisuals.Armour));
        var armour = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka)
            .LoadUncached(SpellVisuals.Armour);
        Assert.Equal(2, ModelMeshes.ForModel(armour).Count);                                            // the earth part, the ice part: the mask's two bits
        Assert.Null(models(Effects + "nothing_here"));
    }
}
