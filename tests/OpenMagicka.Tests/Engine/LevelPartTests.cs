using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Particles;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class LevelPartTests
{
    private const int Stride = 60;

    private static readonly RenderDeferredEffectContent Material = new(1, 0, false, false, null,
        new RenderDeferredLayer(false, false, Vector3.One, 0, 0, 0, 0, 0, new ExternalReference("door"), null, null), null);

    /// <summary>A mesh of 10 vertices and 12 indices; one part draws vertices 4..7 with indices 6..11.</summary>
    private static ModelContent Model(string meshName = "door_mesh", int? effect = 0)
    {
        var vertices = new byte[10 * Stride];
        for (var i = 0; i < 10; i++)
        {
            vertices[i * Stride] = (byte)i;              // tag each vertex by its index
        }
        var indices = new byte[12 * 2];
        for (var i = 0; i < 12; i++)
        {
            indices[i * 2] = (byte)(i % 4);            // relative to the part's base vertex: 0..3
        }
        var part = new ModelMeshPartContent(StreamOffset: Stride, BaseVertex: 3, NumVertices: 4, StartIndex: 6, PrimitiveCount: 2,
            VertexDeclarationIndex: 0, Effect: effect is { } e ? new SharedResourceRef(e) : null);
        var mesh = new ModelMeshContent(meshName, 0, new BoundingSphere(Vector3.Zero, 1), new VertexBufferContent(vertices),
            new IndexBufferContent(true, indices), [part]);
        return new ModelContent([new ModelBoneContent("root", Matrix.Identity, null, [])], [GeometryConversionTests.LevelDeclaration()], [mesh], 0);
    }

    private static AnimatedLevelPartContent Part(string name, Vector3 offset, ModelContent? model, bool visible = true,
        IReadOnlyList<AnimatedLevelPartContent>? children = null) => new(
        name, false, model, model is null ? [] : [new MeshSetting("door_mesh", visible, true)], [], [], 1,
        [new AnimationKeyframe(0, new BonePose(offset, Quaternion.Identity, Vector3.One)),
         new AnimationKeyframe(1, new BonePose(offset + new Vector3(0, 50, 0), Quaternion.Identity, Vector3.One))],
        [], [], null, null, children ?? []);

    [Fact]
    public void APartsMeshes_AreDrawnAtItsRestPose_WithTheirOwnSliceOfTheMeshBuffers()
    {
        var batch = Assert.Single(LevelParts.Batches([Part("door", new Vector3(10, 0, 0), Model())], [Material]));

        Assert.Equal(Matrix.CreateTranslation(10, 0, 0), batch.World);   // the first keyframe, not the second
        Assert.Equal((4, 0, 2), (batch.VertexCount, batch.StartIndex, batch.PrimitiveCount));
        Assert.Equal([4, 5, 6, 7], Enumerable.Range(0, 4).Select(i => (int)batch.VertexData[i * Stride]));   // stream offset 1 + base vertex 3
        Assert.Equal([2, 3, 0, 1, 2, 3], Enumerable.Range(0, 6).Select(i => (int)batch.IndexData[i * 2]));   // indices 6..11
        Assert.Equal("door", batch.DiffuseTexture?.Path);
        Assert.Equal(ContainmentType.Contains, batch.Bounds.Contains(new Vector3(10, 0, 0)));
    }

    [Fact]
    public void AChild_IsPlacedInItsRotatedParentsSpace()
    {
        var parent = Part("door", new Vector3(10, 0, 0), null) with
        {
            Animation = [new AnimationKeyframe(0, new BonePose(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2), Vector3.One))],
            Children = [Part("handle", new Vector3(5, 0, 0), Model())],
        };

        var world = Assert.Single(LevelParts.Batches([parent], [Material])).World;

        var origin = Vector3.Transform(Vector3.Zero, world);              // (5,0,0) turned to (0,0,-5), then moved by the parent
        Assert.True(Vector3.Distance(new Vector3(10, 0, -5), origin) < 1e-4f, origin.ToString());
    }

    [Fact]
    public void Bounds_GrowWithTheScaleOfThePose()
    {
        var scaled = Part("door", Vector3.Zero, Model()) with
        {
            Animation = [new AnimationKeyframe(0, new BonePose(Vector3.Zero, Quaternion.Identity, new Vector3(1, 3, 1)))],
        };

        var bounds = Assert.Single(LevelParts.Batches([scaled], [Material])).Bounds;

        Assert.True(bounds.Max.Y >= 3, $"radius 1 scaled by 3 must reach 3, got {bounds.Max.Y}");
    }

    [Fact]
    public void APartWhoseRestPoseIsNotFinite_IsSkippedWithItsChildren()
    {
        var broken = Part("energy_ring", Vector3.Zero, Model()) with
        {
            Animation = [new AnimationKeyframe(0, new BonePose(new Vector3(float.NaN), Quaternion.Identity, Vector3.One))],
            Children = [Part("child", Vector3.Zero, Model())],
        };

        Assert.Empty(LevelParts.Batches([broken], [Material]));
    }

    [Fact]
    public void IndicesBeyondThePartsVertices_AreInvalid()
    {
        var model = Model();
        var mesh = model.Meshes[0];
        var badIndices = mesh with { IndexBuffer = new IndexBufferContent(true, Enumerable.Repeat((byte)9, 24).ToArray()) };   // index 2313

        Assert.Throws<InvalidDataException>(() => LevelParts.Batches([Part("door", Vector3.Zero, model with { Meshes = [badIndices] })], [Material]));
    }

    [Fact]
    public void ChildParts_AreRelativeToTheirParent()
    {
        var child = Part("handle", new Vector3(0, 5, 0), Model());
        var batches = LevelParts.Batches([Part("door", new Vector3(10, 0, 0), null, children: [child])], [Material]);

        Assert.Equal(Matrix.CreateTranslation(10, 5, 0), Assert.Single(batches).World);
    }

    [Fact]
    public void HiddenMeshes_AndPartsWithoutAModel_AreNotDrawn()
    {
        Assert.Empty(LevelParts.Batches([Part("door", Vector3.Zero, Model(), visible: false)], [Material]));
        Assert.Empty(LevelParts.Batches([Part("door", Vector3.Zero, null)], [Material]));
    }

    [Fact]
    public void AMeshMissingFromTheSettings_OrABadEffectReference_IsInvalid_LikeInTheOriginal()
    {
        Assert.Throws<InvalidDataException>(() => LevelParts.Batches([Part("door", Vector3.Zero, Model("other_mesh"))], [Material]));
        Assert.Throws<InvalidDataException>(() => LevelParts.Batches([Part("door", Vector3.Zero, Model(effect: 5))], [Material]));
    }

    [Fact]
    public void RestPose_ComposesScaleRotationAndTranslation()
    {
        var pose = new BonePose(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2), new Vector3(2));

        var matrix = LevelParts.PoseMatrix(pose);

        var point = Vector3.Transform(Vector3.UnitX, matrix);              // scaled to 2, turned to -Z, moved
        Assert.True(Vector3.Distance(new Vector3(1, 2, 1), point) < 1e-4f, point.ToString());
    }

    [Fact]
    public void APartsEffects_StartAtItsRestPose_AsChildrenOfIt()
    {
        var effect = new VisualEffectPlacement("smoke", new Vector3(1, 2, 0), Vector3.Forward, 0, "chimney_smoke");
        var child = Part("lid", new Vector3(0, 3, 0), null) with { Effects = [effect] };
        var door = Part("door", new Vector3(10, 0, 0), null, children: [child]);

        var placed = Assert.Single(LevelParts.Effects([door]));

        Assert.Equal("chimney_smoke", placed.Effect);
        Assert.Equal(new Vector3(11, 5, 0), placed.World.Translation);
        Assert.Equal(Vector3.Forward, placed.World.Forward);
    }

    [Fact]
    public void APartsEffect_TurnsWithThePart()
    {
        var turned = new BonePose(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2), Vector3.One);
        var door = Part("door", Vector3.Zero, null) with
        {
            Animation = [new AnimationKeyframe(0, turned)],
            Effects = [new VisualEffectPlacement("smoke", new Vector3(1, 0, 0), Vector3.Forward, 0, "chimney_smoke")],
        };

        var world = Assert.Single(LevelParts.Effects([door])).World;

        // The effect's own place first, then the part's: (1, 0, 0) turned a quarter about Y is (0, 0, -1).
        Assert.True(Vector3.Distance(new Vector3(10, 0, -1), world.Translation) < 1e-4f, $"{world.Translation}");
        Assert.True(Vector3.Distance(Vector3.Left, world.Forward) < 1e-4f, $"{world.Forward}");
    }

    [Fact]
    public void APartWhoseRestPoseIsNotFinite_StartsNoEffects_NorDoItsChildren()
    {
        var child = Part("lid", Vector3.Zero, null) with { Effects = [new VisualEffectPlacement("a", Vector3.Zero, Vector3.Forward, 0, "x")] };
        var broken = Part("ring", new Vector3(float.NaN, 0, 0), null, children: [child]) with
        {
            Effects = [new VisualEffectPlacement("b", Vector3.Zero, Vector3.Forward, 0, "y")],
        };

        Assert.Empty(LevelParts.Effects([broken]));
    }

    [Fact]
    public void ALevelsOwnEffect_IsPlacedAlongItsForward()
    {
        var placed = PlacedEffect.At(new VisualEffectPlacement("fire", new Vector3(1, 2, 3), Vector3.Right, 0, "torch_fire"), Matrix.Identity);

        Assert.Equal((new Vector3(1, 2, 3), Vector3.Right), (placed.World.Translation, placed.World.Forward));
    }
}
