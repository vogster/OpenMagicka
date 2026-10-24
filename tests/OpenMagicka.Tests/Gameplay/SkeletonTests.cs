using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Items;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The skeleton's pose and where held items are (ENTITIES_AI.md §4.3).</summary>
public class SkeletonTests
{
    private static BonePose At(float x, float y = 0, float z = 0) => BonePose.Identity with { Translation = new Vector3(x, y, z) };

    /// <summary>A root, a hand above it (1 m up) and a tip ahead of the hand.</summary>
    internal static Skeleton Arm() => new([
        new SkeletonBone("root", -1, BonePose.Identity, Matrix4x4.Identity),
        new SkeletonBone("hand", 0, At(0, 1), Matrix4x4.CreateTranslation(0, -1, 0)),
        new SkeletonBone("tip", 1, At(0, 0, -1), Matrix4x4.CreateTranslation(0, -1, 1)),
    ]);

    private static AnimationClip Clip(string name, float duration, params (string Bone, Keyframe[] Keys)[] channels) =>
        new(name, duration, channels.ToDictionary(c => c.Bone, c => (IReadOnlyList<Keyframe>)c.Keys));

    private static BonePose[] Sample(Skeleton skeleton, AnimationClock clock)
    {
        var poses = new BonePose[skeleton.Bones.Count];
        skeleton.Sample(clock, poses);
        return poses;
    }

    [Fact]
    public void AChannel_LerpsBetweenKeys_AndHoldsTheLastWhenNotLooping()
    {
        var swing = Clip("swing", 2, ("hand", [new Keyframe(0, At(0, 1)), new Keyframe(1, At(2, 1))]));
        var clock = new AnimationClock();

        clock.Start(swing, loop: false);
        clock.Update(0.5f);
        Assert.Equal(1, Sample(Arm(), clock)[1].Translation.X, 4);

        clock.Update(1);                                                          // past the last key
        Assert.Equal(2, Sample(Arm(), clock)[1].Translation.X, 4);
        Assert.Equal(At(0, 0, -1), Sample(Arm(), clock)[2]);                      // no channel: the bind pose
    }

    [Fact]
    public void ALoopingChannel_HeadsBackToTheFirstKey_OverTheTimeLeft()
    {
        var swing = Clip("swing", 2, ("hand", [new Keyframe(0, At(0, 1)), new Keyframe(1, At(2, 1))]));
        var clock = new AnimationClock();

        clock.Start(swing, loop: true);
        clock.Update(1.5f);

        Assert.Equal(1, Sample(Arm(), clock)[1].Translation.X, 4);               // halfway from 2 back to 0
    }

    [Fact]
    public void ACrossfade_BlendsTowardsTheIncomingFirstKey()
    {
        var from = Clip("from", 2, ("hand", [new Keyframe(0, At(0, 1)), new Keyframe(2, At(0, 1))]));
        var into = Clip("into", 2, ("hand", [new Keyframe(0, At(4, 1)), new Keyframe(1, At(8, 1))]));
        var clock = new AnimationClock();
        clock.Start(from, loop: true);

        clock.CrossFade(into, 1, loop: false);
        clock.Update(0.25f);

        Assert.Equal(1, Sample(Arm(), clock)[1].Translation.X, 4);               // a quarter of the way to the first key (4)
    }

    [Fact]
    public void Bones_ArePlacedByTheirParents_AndSkinnedByTheirInverseBind()
    {
        var skeleton = Arm();
        var poses = new BonePose[3];
        var (placed, skinned) = (new Matrix4x4[3], new Matrix4x4[3]);
        poses[0] = BonePose.Identity;
        poses[1] = At(0, 1);
        poses[2] = At(0, 0, -1);

        skeleton.Place(poses, Matrix4x4.CreateTranslation(5, 0, 0), placed, skinned);

        Assert.Equal(new Vector3(5, 1, -1), placed[2].Translation);
        Assert.Equal(new Vector3(5, 0, 0), skinned[2].Translation);               // at its bind pose: only the root's move
    }

    [Fact]
    public void AScaledPose_LeavesItsTranslation()
    {
        var pose = new BonePose(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));

        var m = pose.ToMatrix();

        Assert.Equal(new Vector3(1, 2, 3), m.Translation);
        Assert.Equal(2, m.M11);
    }

    [Fact]
    public void ABoneBeforeItsParent_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new Skeleton([
            new SkeletonBone("child", 1, BonePose.Identity, Matrix4x4.Identity),
            new SkeletonBone("root", -1, BonePose.Identity, Matrix4x4.Identity)]));

    [Fact]
    public void ACharactersBones_FollowItsPositionAndFacing()
    {
        using var world = World();
        var animations = new CharacterAnimations(Clips().Sets, Arm());
        var goblin = Animated(world, at: new Vector3(3, 0.9f, 0), clips: animations);
        Run(world, 0.2f);

        Assert.Equal(new Vector3(3, 1, -1), goblin.BoneWorld(2).Translation, new Tolerance(0.02f));   // feet at 0, the tip ahead (−Z)
        goblin.Body.Movement = Vector3.UnitX;
        Run(world, 1);
        goblin.Body.Movement = Vector3.Zero;
        Run(world, 0.5f);

        var tip = goblin.BoneWorld(2).Translation - goblin.BoneWorld(1).Translation;
        Assert.Equal(Vector3.UnitX, tip, new Tolerance(0.05f));                    // turned to face +X
    }

    [Fact]
    public void AHeldItemsTip_IsPlacedAtTheEndOfTheFrame()
    {
        using var world = World();
        var goblin = Animated(world, clips: new CharacterAnimations(Clips().Sets, Arm()));
        var sword = new Item(new ItemTemplate("sword", WeaponClass.SlashMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1)));
        goblin.Equip(0, sword, "hand");
        Run(world, 0.1f);
        var before = sword.Tip;

        goblin.Body.Body.Position += new Vector3(2, 0, 0);
        world.Tick(Tick);

        Assert.Equal(before, sword.LastTip);
        Assert.Equal(before.X + 2, sword.Tip.X, 1);
        Assert.Equal(1, sword.Tip.Y, 1);                                            // the hand is 1 m up
    }

    [Fact]
    public void AnItemOnABoneTheSkeletonLacks_IsRejected_BeforeTheBodyExists()
    {
        using var world = World();
        var animations = new CharacterAnimations(Clips().Sets, Arm());
        var template = Goblin with { Equipment = [new EquipmentTemplate(0, "tail", Vector3.Zero, new ItemTemplate("sword"))] };
        var bodies = world.Physics.CastSegment(new Vector3(0, 0.9f, -2), new Vector3(0, 0.9f, 2));

        Assert.Throws<ArgumentException>(() => Animated(world, template, clips: animations));
        Assert.Equal(bodies, world.Physics.CastSegment(new Vector3(0, 0.9f, -2), new Vector3(0, 0.9f, 2)));
        Assert.Throws<ArgumentException>(() => Animated(world, clips: animations, at: new Vector3(5, 0.9f, 0)).Equip(0, new Item(new ItemTemplate("x")), "tail"));
    }

    [Fact]
    public void ARotatedParent_TurnsItsChild()
    {
        var skeleton = new Skeleton([
            new SkeletonBone("root", -1, BonePose.Identity, Matrix4x4.Identity),
            new SkeletonBone("child", 0, At(0, 0, -1), Matrix4x4.Identity)]);
        var poses = new[] { BonePose.Identity with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }, At(0, 0, -1) };
        var (placed, skinned) = (new Matrix4x4[2], new Matrix4x4[2]);

        skeleton.Place(poses, Matrix4x4.Identity, placed, skinned);

        Assert.Equal(new Vector3(-1, 0, 0), placed[1].Translation, new Tolerance(1e-5f));   // −Z turned a quarter about +Y
    }

    [Theory]
    [InlineData(0.5f, 0)]                                                       // between: the first key holds the start
    [InlineData(1f, 1)]                                                         // exactly on a key
    [InlineData(-1f, 0)]                                                        // before them all
    [InlineData(9f, 2)]                                                         // after them all
    public void TheKeySearch_FindsTheKeyAtOrBefore(float time, int expected)
    {
        Keyframe[] keys = [new(0, At(0)), new(1, At(1)), new(2, At(2))];

        var pose = Skeleton.ChannelAt(keys, time, 3, looping: false);

        Assert.Equal(expected == 0 && time == 0.5f ? 0.5f : expected, pose.Translation.X, 4);
    }

    [Fact]
    public void OneKey_AndALastKeyAtTheEnd_HoldStill()
    {
        Assert.Equal(At(4), Skeleton.ChannelAt([new Keyframe(0.5f, At(4))], 2, 3, looping: true));
        Assert.Equal(At(2), Skeleton.ChannelAt([new Keyframe(0, At(0)), new Keyframe(3, At(2))], 3, 3, looping: true));   // no gap back to the first
    }

    [Fact]
    public void AnInstantCrossfade_TakesTheIncomingBindPose_WithoutAChannel()
    {
        var from = Clip("from", 2, ("hand", [new Keyframe(0, At(3, 1))]));
        var into = new AnimationClip("into", 2);
        var clock = new AnimationClock();
        clock.Start(from, loop: true);

        clock.CrossFade(into, 0, loop: false);

        Assert.Equal(At(0, 1), Sample(Arm(), clock)[1]);                         // a zero blend is all the way: the bind pose
    }

    [Fact]
    public void BadSkeletonsAndKeys_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Skeleton([new SkeletonBone("root", 0, BonePose.Identity, Matrix4x4.Identity)]));
        Assert.Throws<ArgumentException>(() => new Skeleton([
            new SkeletonBone("root", -1, BonePose.Identity, Matrix4x4.Identity), new SkeletonBone("loose", -1, BonePose.Identity, Matrix4x4.Identity)]));
        Assert.Throws<ArgumentException>(() => Clip("empty", 1, ("hand", [])));
        Assert.Throws<ArgumentException>(() => Clip("backwards", 1, ("hand", [new Keyframe(1, At(0)), new Keyframe(0, At(0))])));
        Assert.Throws<ArgumentException>(() => Clip("nan", 1, ("hand", [new Keyframe(0, At(float.NaN))])));
        Assert.Throws<ArgumentException>(() => Clip("flat", 1, ("hand", [new Keyframe(0, BonePose.Identity with { Rotation = default })])));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arm().Sample(new AnimationClock(), new BonePose[2]));
        var twice = new Skeleton([new SkeletonBone("a", -1, BonePose.Identity, Matrix4x4.Identity), new SkeletonBone("A", 0, BonePose.Identity, Matrix4x4.Identity)]);
        Assert.Equal(0, twice.IndexOf("a"));                                       // the first of a name
    }

    [Fact]
    public void ATurnedSlot_AndAScaledModel_PlaceTheItem()
    {
        using var world = World();
        var big = Goblin with { ModelScale = 2 };
        var goblin = Animated(world, big, clips: new CharacterAnimations(Clips().Sets, Arm()));
        var sword = new Item(new ItemTemplate("sword", WeaponClass.SlashMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1)));

        goblin.Equip(0, sword, "hand");
        Assert.Equal(sword.Tip, sword.LastTip);                                   // just taken in hand: no sweep
        Assert.Equal(2, goblin.BoneWorld(1).Translation.Y - (goblin.Position.Y + goblin.Body.HeightOffset), 2);   // bones scale with the model
        Assert.Equal(goblin.Position.Z - 1, sword.Tip.Z, 2);                       // Ry(π) turns the tip ahead; the item keeps its size

        goblin.Equip(0, sword, "hand", new Vector3(0, MathF.PI / 2, 0));
        Assert.Equal(goblin.Position.X - 1, sword.Tip.X, 2);                       // a quarter more about Y
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Spawn(Goblin with { ModelScale = 0 }, new Vector3(5, 0.9f, 0)));
    }

    private sealed class Tolerance(float within) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= within;

        public int GetHashCode(Vector3 v) => 0;
    }
}
