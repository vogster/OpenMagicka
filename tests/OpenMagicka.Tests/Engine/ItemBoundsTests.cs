using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Characters;

namespace OpenMagicka.Tests.Engine;

/// <summary>An item's bounds from its model (ENTITIES_AI.md §8.1.5).</summary>
public class ItemBoundsTests
{
    /// <summary>A model whose first part reads <paramref name="count"/> vertices of position then a 4-byte pad.</summary>
    private static ModelContent Model(float[][] points, int count, int declaration = 0, VertexElementFormatXna31 format = VertexElementFormatXna31.Vector3)
    {
        var data = points.SelectMany(p => p.SelectMany(BitConverter.GetBytes).Concat(new byte[4])).ToArray();
        var elements = new VertexDeclarationContent([new VertexElementContent(0, 0, format, 0, VertexElementUsageXna31.Position, 0),
            new VertexElementContent(0, 12, VertexElementFormatXna31.Single, 0, VertexElementUsageXna31.TextureCoordinate, 0)]);
        var part = new ModelMeshPartContent(0, 0, count, 0, 0, declaration, null);
        return new ModelContent([], [elements], [new ModelMeshContent("m", null, new BoundingSphere(), new VertexBufferContent(data), null, [part])], null);
    }

    [Fact]
    public void TheFirstVertices_AtTheStride_GiveTheBounds()
    {
        var bounds = ItemTemplates.Bounds(Model([[1, 2, 3], [-1, 0, 5], [9, 9, 9]], count: 2))!.Value;

        Assert.Equal((new System.Numerics.Vector3(-1, 0, 3), new System.Numerics.Vector3(1, 2, 5)), (bounds.Min, bounds.Max));   // the third is not read
    }

    [Fact]
    public void NoModel_OrNoVertices_GiveNone()
    {
        Assert.Null(ItemTemplates.Bounds(null));
        Assert.Null(ItemTemplates.Bounds(Model([[1, 2, 3]], count: 0)));
        Assert.Null(ItemTemplates.Bounds(new ModelContent([], [], [], null)));
    }

    [Fact]
    public void BadModels_FailOnLoad()
    {
        Assert.Throws<InvalidDataException>(() => ItemTemplates.Bounds(Model([[1, 2, 3]], count: 2)));                 // more vertices than the buffer holds
        Assert.Throws<InvalidDataException>(() => ItemTemplates.Bounds(Model([[1, 2, 3]], count: 1, declaration: 1)));
        Assert.Throws<InvalidDataException>(() => ItemTemplates.Bounds(Model([[1, 2, 3]], count: 1, format: VertexElementFormatXna31.Vector4)));
    }
}
