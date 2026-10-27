using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>A level's moving parts drawn where they are now (OM-74): the batches name their parts, the frame moves them and the lights on them.</summary>
public class PartDrawingTests
{
    private static AnimationKeyframe Key(float time, Vector3 at) => new(time, new BonePose(at, Quaternion.Identity, Vector3.One));

    private static AnimatedLevelPartContent Part(string name, Vector3 rest, IReadOnlyList<AnimatedLevelPartContent>? children = null,
        IReadOnlyList<PartLightAttachment>? lights = null) => new(name, true, null, [], [], [], 2, [Key(0, rest), Key(2, rest + new Vector3(0, 10, 0))],
        [], lights ?? [], null, null, children ?? []);

    private static LevelLightContent Light(string name, LevelLightType type, Vector3 at) =>
        new(name, at, -Vector3.UnitZ, type, LightVariationType.None, 5, true, MathHelper.PiOver4, 1, Vector3.One, Vector3.Zero, 0, 0, 0, 0, false);

    /// <summary>A door with a handle under it, then a lift: the door 0, the handle 1, the lift 2.</summary>
    private static IReadOnlyList<AnimatedLevelPartContent> Parts(IReadOnlyList<PartLightAttachment>? liftLights = null) =>
        [Part("door", new Vector3(10, 0, 0), [Part("handle", new Vector3(1, 0, 0))]), Part("lift", new Vector3(0, 0, 5), lights: liftLights)];

    [Fact]
    public void TheParts_AreNumberedEachThenItsChildren_WithTheirRestPoses()
    {
        var parts = Parts();

        Assert.Equal(["door", "handle", "lift"], LevelParts.Order(parts).Select(p => p.Name));
        Assert.Equal([new Vector3(10, 0, 0), new Vector3(11, 0, 0), new Vector3(0, 0, 5)], LevelParts.Rests(parts).Select(r => r.Translation));
    }

    [Fact]
    public void AFrame_MovesEachPartFromItsRest_ToWhereItIsNow()
    {
        var rests = LevelParts.Rests(Parts());
        var unrests = rests.Select(GpuLevel.Unrest).ToArray();
        Matrix[] now = [Matrix.CreateTranslation(10, 4, 0), rests[1], Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(0, 0, 5)];
        var moves = new Matrix[3];

        LevelParts.Moves(unrests, now, moves);

        Assert.Equal(new Vector3(10, 4, 0), Vector3.Transform(new Vector3(10, 0, 0), moves[0]));      // a batch of the door at rest, where the door is now
        Assert.Equal(Matrix.Identity, moves[1]);
        Assert.True(Vector3.Distance(new Vector3(0, 0, 4), Vector3.Transform(new Vector3(1, 0, 5), moves[2])) < 1e-4f);   // turned round the lift
        LevelParts.Moves(unrests, null, moves);
        Assert.All(moves, m => Assert.Equal(Matrix.Identity, m));                                       // no parts in the frame: all at rest
    }

    [Fact]
    public void ALightOnAPart_GoesWithIt_PointsByPosition_SpotsByPositionAndFacing()
    {
        var parts = Parts([new PartLightAttachment("Lamp", Matrix.CreateTranslation(0, 2, 0)),
            new PartLightAttachment("Beam", Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(1, 0, 0)),
            new PartLightAttachment("nowhere", Matrix.Identity), new PartLightAttachment("SUN", Matrix.CreateRotationX(-MathHelper.PiOver2))]);
        var level = new LevelModelContent(null, parts, [Light("Sun", LevelLightType.Directional, Vector3.Zero), Light("Lamp", LevelLightType.Point, new Vector3(0, 2, 5)),
            Light("Beam", LevelLightType.Spot, new Vector3(1, 0, 5))], [], [], [], [], [], null, [], [], new NavMeshContent([], []));
        var lit = new FrameLights(SceneLighting.PointLights(level.Lights), SceneLighting.SpotLights(level.Lights));

        var riding = PartLight.Of(level);
        Assert.Equal([new PartLight(2, PartLightKind.Point, 0, Matrix.CreateTranslation(0, 2, 0)), new PartLight(2, PartLightKind.Spot, 0, riding[1].Local),
            new PartLight(2, PartLightKind.Sun, 0, Matrix.CreateRotationX(-MathHelper.PiOver2))], riding);
        var placed = PartLight.Place(lit, riding, [Matrix.Identity, Matrix.Identity, Matrix.CreateTranslation(0, 3, 5)]);

        Assert.Equal(new Vector3(0, 5, 5), placed.Points[0].Position);
        Assert.Equal(new Vector3(1, 3, 5), placed.Spots[0].Position);
        Assert.True(Vector3.Distance(-Vector3.UnitX, placed.Spots[0].Direction) < 1e-4f, placed.Spots[0].Direction.ToString());   // forward turned a quarter
        Assert.True(Vector3.Distance(-Vector3.UnitY, placed.Sun(SceneLighting.Sun(level.Lights)).Direction) < 1e-4f);    // the sun turned to shine down
        Assert.Same(lit, PartLight.Place(lit, riding, null));
        Assert.Equal(SceneLighting.Sun(level.Lights), lit.Sun(SceneLighting.Sun(level.Lights)));
    }

    [Fact]
    public void ThePlaces_GiveWhereEachPartIsNow_InTheBatchesOrder()
    {
        var places = new ScriptPlaces([], [], Parts());
        places.Part("door")!.Play(withChildren: true, from: -1, to: -1, speed: 1, loop: false, resume: false);
        places.UpdateParts(1);
        var placements = new List<Numerics.Matrix4x4>();

        places.Placements(placements);

        Assert.Equal([new Numerics.Vector3(10, 5, 0), new Numerics.Vector3(11, 10, 0), new Numerics.Vector3(0, 0, 5)], placements.Select(m => m.Translation));
    }

    [Fact]
    public void AHighlightedPart_GlowsForItsTime_AHighlightOfNoTimeForOneFrame_InTheBatchesOrder()
    {
        var places = new ScriptPlaces([], [], Parts());
        var glowing = new List<bool>();
        places.Part("door/handle")!.Highlight(0.5f);
        places.UpdateParts(0.25f);
        places.Highlights(glowing);
        Assert.Equal([false, true, false], glowing);                             // only the handle, not its parent
        places.UpdateParts(0.25f);
        places.UpdateParts(0.25f);
        places.Highlights(glowing);
        Assert.True(glowing[1]);                                                 // its time, and the frame it ran out in
        places.UpdateParts(0.25f);
        places.Highlights(glowing);
        Assert.Equal([false, false, false], glowing);

        places.Part("lift")!.Highlight(0);
        places.UpdateParts(1 / 60f);
        places.Highlights(glowing);
        Assert.True(glowing[2]);                                                 // sampled before its step, as the original's
        places.UpdateParts(1 / 60f);
        places.Highlights(glowing);
        Assert.False(glowing[2]);
    }

    [Fact]
    public void TheHighlight_RedrawsOnlyTheGlowingPartsDeferredMeshes()
    {
        GpuBatch Batch(int part, bool deferred) => new(null!, null!, 0, 0, 0, default, Matrix.Identity, null,
            deferred ? new OpenMagicka.Content.PolygonHead.RenderDeferredEffectContent(1, 0, false, false, null, new(false, false, default, 0, 0, 0, 0, 0, null, null, null), null) : null, null, Vector3.One, false, true, part);
        GpuBatch[] batches = [Batch(-1, true), Batch(0, true), Batch(0, false), Batch(1, true), Batch(5, true)];

        Assert.Equal([batches[1]], DeferredRenderer.Highlighted(batches, [true, false]));
    }

    [Fact]
    public void APartANamesakeReplaced_StaysAtItsRest()
    {
        var places = new ScriptPlaces([], [], [Part("door", new Vector3(10, 0, 0)), Part("Door", new Vector3(-3, 0, 0))]);
        places.Part("door")!.Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
        places.UpdateParts(2);
        var placements = new List<Numerics.Matrix4x4>();

        places.Placements(placements);

        Assert.Equal([new Numerics.Vector3(10, 0, 0), new Numerics.Vector3(-3, 10, 0)], placements.Select(m => m.Translation));
    }
}
