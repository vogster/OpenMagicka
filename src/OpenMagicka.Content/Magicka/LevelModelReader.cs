using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

/// <summary>Magicka's LevelModelReader. Layout: docs/formats/XNB_READERS.md §4.</summary>
internal static partial class LevelModelReader
{
    private const int CollisionSlots = 10;
    /// <summary>Animated parts nest at most 4 deep in the 132 levels: 4x headroom.</summary>
    public const int MaxPartDepth = 16;

    // name, flag, model id, eight u32 counts or floats, two flags, child count
    private const int MinPartSize = 1 + 1 + 1 + 4 * 8 + 2;

    public static LevelModelContent Read(XnbContentReader r)
    {
        var geometry = r.ReadObject<BiTreeModelContent>();
        var parts = r.ReadList(MinPartSize, x => ReadAnimatedPart(x, depth: 0));
        var lights = r.ReadList(1 + 24 + 8 + 13 + 24 + 12 + 5, ReadLight);
        var effects = r.ReadList(2 + 28, ReadEffectPlacement);
        var physics = r.ReadList(64 + 1, x => new PhysicsEntityPlacement(x.ReadMatrix(), x.ReadString()));
        var liquids = r.ReadList(4 + 12 + 3, ReadLiquid);
        var forceFields = r.ReadList(41 + 3 + 12, ReadForceField);

        var collision = new CollisionMeshContent?[CollisionSlots];
        for (var slot = 0; slot < CollisionSlots; slot++)
        {
            collision[slot] = r.ReadBoolean() ? ReadCollisionMesh(r) : null;
        }
        var camera = r.ReadBoolean() ? ReadCollisionMesh(r) : null;

        var triggers = r.ReadList(1 + 40, x => new TriggerAreaContent(x.ReadString(), x.ReadVector3(), x.ReadVector3(), x.ReadQuaternion()));
        var locators = r.ReadList(1 + 68, ReadLocator);
        var navMesh = ReadNavMesh(r);
        return new LevelModelContent(geometry, parts, lights, effects, physics, liquids, forceFields,
            collision, camera, triggers, locators, navMesh);
    }

    private static LevelLightContent ReadLight(XnbContentReader r)
    {
        var name = r.ReadString();
        var position = r.ReadVector3();
        var direction = r.ReadVector3();
        var type = (LevelLightType)r.ReadInt32();
        var variation = (LightVariationType)r.ReadInt32();
        // The original refuses to load other light types (Custom = 10).
        if (!Enum.IsDefined(type))
        {
            throw new InvalidDataException($"light '{name}' has unsupported type {(int)type}");
        }
        return new LevelLightContent(name, position, direction, type, variation,
            Range: r.ReadSingle(),
            UseAttenuation: r.ReadBoolean(),
            CutoffAngle: r.ReadSingle(),
            Sharpness: r.ReadSingle(),
            DiffuseColor: r.ReadVector3(),
            AmbientColor: r.ReadVector3(),
            SpecularAmount: r.ReadSingle(),
            VariationSpeed: r.ReadSingle(),
            VariationAmount: r.ReadSingle(),
            ShadowMapSize: r.ReadInt32(),
            CastShadows: r.ReadBoolean());
    }

    private static VisualEffectPlacement ReadEffectPlacement(XnbContentReader r) =>
        new(r.ReadString(), r.ReadVector3(), r.ReadVector3(), r.ReadSingle(), r.ReadString());

    private static LiquidContent ReadLiquid(XnbContentReader r)
    {
        var effect = r.ReadObject();
        if (effect is not (RenderDeferredLiquidEffectContent or LavaEffectContent))
        {
            throw new InvalidDataException($"liquid with effect {effect?.GetType().Name ?? "null"}");
        }
        return new LiquidContent(effect,
            VertexBuffer: r.ReadObject<VertexBufferContent>(),
            IndexBuffer: r.ReadObject<IndexBufferContent>(),
            Declaration: r.ReadObject<VertexDeclarationContent>(),
            VertexStride: r.ReadNonNegative(),
            NumVertices: r.ReadNonNegative(),
            PrimitiveCount: r.ReadNonNegative(),
            HasCollision: r.ReadBoolean(),
            Freezable: r.ReadBoolean(),
            AutoFreeze: r.ReadBoolean());
    }

    private static ForceFieldContent ReadForceField(XnbContentReader r) => new(
        Color: r.ReadVector3(),
        Width: r.ReadSingle(),
        AlphaPower: r.ReadSingle(),
        AlphaFalloffPower: r.ReadSingle(),
        MaxRadius: r.ReadSingle(),
        RippleDistortion: r.ReadSingle(),
        MapDistortion: r.ReadSingle(),
        VertexColorEnabled: r.ReadBoolean(),
        DisplacementMap: r.ReadExternalReference(),
        Ttl: r.ReadSingle(),
        VertexBuffer: r.ReadObject<VertexBufferContent>(),
        IndexBuffer: r.ReadObject<IndexBufferContent>(),
        Declaration: r.ReadObject<VertexDeclarationContent>(),
        VertexStride: r.ReadNonNegative(),
        NumVertices: r.ReadNonNegative(),
        PrimitiveCount: r.ReadNonNegative());

    private static LocatorContent ReadLocator(XnbContentReader r) => new(r.ReadString(), r.ReadMatrix(), r.ReadSingle());
}
