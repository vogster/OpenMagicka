using Microsoft.Xna.Framework;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class ModelMeshesTests
{
    private static ModelMeshContent Mesh(int? bone) => new("mesh", bone, new BoundingSphere(Vector3.Zero, 1),
        new VertexBufferContent(new byte[3 * 60]), new IndexBufferContent(true, new byte[6]),
        [new ModelMeshPartContent(0, 0, 3, 0, 1, 0, new SharedResourceRef(0))]);

    /// <summary>Bone 0 at (10,0,0); bone 1, its child, a further (0,5,0); the mesh hangs on bone 1.</summary>
    private static ModelContent Model() => new(
        [
            new ModelBoneContent("root", Matrix.CreateTranslation(10, 0, 0), null, [1]),
            new ModelBoneContent("arm", Matrix.CreateTranslation(0, 5, 0), 0, []),
        ],
        [GeometryConversionTests.LevelDeclaration()], [Mesh(1)], 0);

    private static IReadOnlyList<LevelBatch> Batches(object root, object effect) => ModelMeshes.ForModel(root, [effect]);

    private static readonly RenderDeferredEffectContent Deferred = new(1, 0, false, false, null,
        new RenderDeferredLayer(false, false, Vector3.One, 0, 0, 0, 0, 0, new ExternalReference("crate"), null, null), null);

    [Fact]
    public void AModelsMeshes_SitAtTheAbsoluteTransformOfTheirBone()
    {
        Assert.Equal([Matrix.CreateTranslation(10, 0, 0), Matrix.CreateTranslation(10, 5, 0)], ModelMeshes.AbsoluteBoneTransforms(Model()));

        var batch = Assert.Single(Batches(Model(), Deferred));

        Assert.Equal(Matrix.CreateTranslation(10, 5, 0), batch.World);
        Assert.Equal("crate", batch.DiffuseTexture?.Path);
    }

    [Fact]
    public void ASkinnedModel_IsDrawnInItsBindPose_WithItsDiffuseMap()
    {
        var material = new SkinnedModelBasicEffectContent(0, 0, new Vector3(0.5f), 0, 0, 1, false, true, false, false, false, false, false,
            ContentRef<Texture2DContent>.FromExternal(new ExternalReference("wizard_skin")), default, default, default, default, default);
        var skinned = new SkinnedModelContent(Model(), [], []);

        var batch = Assert.Single(Batches(skinned, material));

        Assert.Equal(Matrix.Identity, batch.World);                       // vertices are already in model space
        Assert.Equal(("wizard_skin", new Vector3(0.5f)), (batch.DiffuseTexture?.Path, batch.DiffuseColor));
    }

    [Fact]
    public void ADisabledDiffuseMap_IsNotUsed()
    {
        var material = new SkinnedModelBasicEffectContent(0, 0, Vector3.One, 0, 0, 1, false, false, false, false, false, false, false,
            ContentRef<Texture2DContent>.FromExternal(new ExternalReference("unused")), default, default, default, default, default);

        Assert.Null(Assert.Single(Batches(new SkinnedModelContent(Model(), [], []), material)).DiffuseTexture);
    }

    [Fact]
    public void ALongBoneChain_DoesNotOverflowTheStack()
    {
        var bones = Enumerable.Range(0, 200_000).Select(i => new ModelBoneContent($"b{i}", Matrix.Identity, i == 0 ? null : i - 1, [])).ToArray();

        Assert.Equal(200_000, ModelMeshes.AbsoluteBoneTransforms(Model() with { Bones = bones }).Length);
    }

    [Fact]
    public void OtherMaterials_GiveTheirDiffuseTextureAndBlending()
    {
        var basic = new BasicEffectContent(new ExternalReference("barrel"), new Vector3(0.2f), Vector3.Zero, Vector3.Zero, 0, 1, false);
        var additive = new AdditiveEffectContent(Vector3.One, false, true, new ExternalReference("glow"));
        var meshNoBone = Model() with { Meshes = [Mesh(null)] };

        Assert.Equal(("barrel", false), Single(basic));
        Assert.Equal(("glow", true), Single(additive));
        Assert.Equal(Matrix.Identity, Assert.Single(Batches(meshNoBone, Deferred)).World);
        Assert.Empty(Batches(new SkinnedModelContent(null, [], []), Deferred));

        (string?, bool) Single(object effect)
        {
            var batch = Assert.Single(Batches(Model(), effect));
            return (batch.DiffuseTexture?.Path, batch.Additive);
        }
    }

    [Fact]
    public void BoneCycles_AndFilesThatAreNotModels_AreInvalid()
    {
        var cyclic = Model() with
        {
            Bones = [new ModelBoneContent("a", Matrix.Identity, 1, [1]), new ModelBoneContent("b", Matrix.Identity, 0, [0])],
        };

        Assert.Throws<InvalidDataException>(() => ModelMeshes.AbsoluteBoneTransforms(cyclic));
        Assert.Throws<InvalidDataException>(() => ModelMeshes.AbsoluteBoneTransforms(Model() with { Bones = [new ModelBoneContent("self", Matrix.Identity, 0, [])] }));
        Assert.Throws<InvalidDataException>(() => ModelMeshes.AbsoluteBoneTransforms(Model() with { Bones = [new ModelBoneContent("orphan", Matrix.Identity, 7, [])] }));
        Assert.Throws<InvalidDataException>(() => Batches(Model() with { Meshes = [Mesh(-1)] }, Deferred));
        Assert.Throws<InvalidDataException>(() => Batches("text", Deferred));
    }
}
