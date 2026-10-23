using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Xna;

/// <summary>
/// XNA 3.1 model. Unlike XNA 4.0 the vertex declarations form a table on the model, and the
/// vertex and index buffers belong to the mesh; each part selects a declaration by index.
/// Bone indices are 0-based; <c>null</c> means "no bone".
/// </summary>
public sealed record ModelContent(
    IReadOnlyList<ModelBoneContent> Bones,
    IReadOnlyList<VertexDeclarationContent> VertexDeclarations,
    IReadOnlyList<ModelMeshContent> Meshes,
    int? RootBone);

public sealed record ModelBoneContent(string? Name, Matrix Transform, int? Parent, IReadOnlyList<int> Children);

public sealed record ModelMeshContent(
    string? Name,
    int? ParentBone,
    BoundingSphere Bounds,
    VertexBufferContent? VertexBuffer,
    IndexBufferContent? IndexBuffer,
    IReadOnlyList<ModelMeshPartContent> Parts);

/// <summary>
/// A draw call of a mesh. The vertex stride is the stride of
/// <see cref="ModelContent.VertexDeclarations"/>[<see cref="VertexDeclarationIndex"/>].
/// <see cref="Effect"/> points into the file's shared resources.
/// </summary>
public sealed record ModelMeshPartContent(
    int StreamOffset,
    int BaseVertex,
    int NumVertices,
    int StartIndex,
    int PrimitiveCount,
    int VertexDeclarationIndex,
    SharedResourceRef? Effect);
