using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb.Readers;

internal static partial class StandardReaders
{
    // Bone references are one byte when the model has fewer than 255 bones, otherwise four.
    private const int ByteBoneRefLimit = 255;

    /// <summary>XNA 3.1 model layout: docs/formats/XNB_READERS.md §1.8.</summary>
    private static ModelContent ReadModel(XnbContentReader r)
    {
        var boneCount = r.ReadCount(1 + 64);
        var names = new string?[boneCount];
        var transforms = new Microsoft.Xna.Framework.Matrix[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            names[i] = r.ReadObject<string>();
            transforms[i] = r.ReadMatrix();
        }

        var bones = new ModelBoneContent[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var parent = ReadBoneRef(r, boneCount);
            var children = new int[r.ReadCount(boneCount < ByteBoneRefLimit ? 1 : 4)];
            for (var c = 0; c < children.Length; c++)
            {
                children[c] = ReadBoneRef(r, boneCount) ?? throw new InvalidDataException("null child bone");
            }
            bones[i] = new ModelBoneContent(names[i], transforms[i], parent, children);
        }

        var declarations = new VertexDeclarationContent[r.ReadCount(1)];
        for (var i = 0; i < declarations.Length; i++)
        {
            declarations[i] = r.ReadObject<VertexDeclarationContent>()
                ?? throw new InvalidDataException("null vertex declaration");
        }

        // name, bone ref, bounds, buffers, tag and part count: at least 25 bytes per mesh
        var meshes = new ModelMeshContent[r.ReadCount(25)];
        for (var i = 0; i < meshes.Length; i++)
        {
            meshes[i] = ReadMesh(r, boneCount, declarations.Length);
        }

        var root = ReadBoneRef(r, boneCount);
        r.ReadObject(); // tag
        return new ModelContent(bones, declarations, meshes, root);
    }

    private static ModelMeshContent ReadMesh(XnbContentReader r, int boneCount, int declarationCount)
    {
        var name = r.ReadObject<string>();
        var parentBone = ReadBoneRef(r, boneCount);
        var bounds = r.ReadBoundingSphere();
        var vertexBuffer = r.ReadObject<VertexBufferContent>();
        var indexBuffer = r.ReadObject<IndexBufferContent>();
        r.ReadObject(); // tag
        var parts = new ModelMeshPartContent[r.ReadCount(6 * 4 + 2)];
        for (var i = 0; i < parts.Length; i++)
        {
            var streamOffset = ReadInt(r);
            var baseVertex = ReadInt(r);
            var numVertices = ReadInt(r);
            var startIndex = ReadInt(r);
            var primitiveCount = ReadInt(r);
            var declaration = ReadInt(r);
            if (declaration >= declarationCount)
            {
                throw new InvalidDataException($"mesh part uses declaration {declaration} of {declarationCount}");
            }
            r.ReadObject(); // tag
            var effect = r.ReadSharedResourceRef();
            parts[i] = new ModelMeshPartContent(streamOffset, baseVertex, numVertices, startIndex, primitiveCount, declaration, effect);
        }
        return new ModelMeshContent(name, parentBone, bounds, vertexBuffer, indexBuffer, parts);
    }

    /// <summary>0 = no bone, otherwise the bone index plus one.</summary>
    private static int? ReadBoneRef(XnbContentReader r, int boneCount)
    {
        var value = boneCount < ByteBoneRefLimit ? r.ReadByte() : r.ReadUInt32();
        if (value == 0)
        {
            return null;
        }
        if (value > boneCount)
        {
            throw new InvalidDataException($"bone reference {value} in a model with {boneCount} bones");
        }
        return (int)value - 1;
    }

    private static int ReadInt(XnbContentReader r)
    {
        var value = r.ReadUInt32();
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException($"model value {value} out of range");
    }
}
