using Microsoft.Xna.Framework;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Magicka;

/// <summary>
/// The geometry and placement data of one level scene (Levels/**/*.xnb). Names are kept as
/// strings; the original turns them into hashes on load. Layout: docs/formats/XNB_READERS.md §4.
/// </summary>
public sealed record LevelModelContent(
    BiTreeModelContent? Geometry,
    IReadOnlyList<AnimatedLevelPartContent> AnimatedParts,
    IReadOnlyList<LevelLightContent> Lights,
    IReadOnlyList<VisualEffectPlacement> Effects,
    IReadOnlyList<PhysicsEntityPlacement> PhysicsEntities,
    IReadOnlyList<LiquidContent> Liquids,
    IReadOnlyList<ForceFieldContent> ForceFields,
    IReadOnlyList<CollisionMeshContent?> CollisionMeshes,
    CollisionMeshContent? CameraMesh,
    IReadOnlyList<TriggerAreaContent> TriggerAreas,
    IReadOnlyList<LocatorContent> Locators,
    NavMeshContent NavMesh);

/// <summary>Surface material; also the index of a level's static collision mesh.</summary>
public enum CollisionMaterial : byte
{
    Generic,
    Gravel,
    Grass,
    Wood,
    Snow,
    Stone,
    Mud,
    Reflect,
    Water,
    Lava,
}

public enum LevelLightType
{
    Point = 0,
    Directional = 1,
    Spot = 2,
}

public enum LightVariationType
{
    None,
    Sine,
    Flicker,
    Candle,
    Strobe,
}

[Flags]
public enum MovementProperties : byte
{
    Default = 0,
    Water = 1,
    Jump = 2,
    Fly = 4,
    Dynamic = 0x80,
}

/// <summary>Range, attenuation, cutoff and sharpness are stored for every light type.</summary>
public sealed record LevelLightContent(
    string Name,
    Vector3 Position,
    Vector3 Direction,
    LevelLightType Type,
    LightVariationType Variation,
    float Range,
    bool UseAttenuation,
    float CutoffAngle,
    float Sharpness,
    Vector3 DiffuseColor,
    Vector3 AmbientColor,
    float SpecularAmount,
    float VariationSpeed,
    float VariationAmount,
    int ShadowMapSize,
    bool CastShadows);

/// <summary>A particle effect from Content/Effects placed at a position facing <see cref="Forward"/>.</summary>
public sealed record VisualEffectPlacement(string Id, Vector3 Position, Vector3 Forward, float Range, string Effect);

/// <summary>A physics object; <see cref="Template"/> is loaded from Data/PhysicsEntities.</summary>
public sealed record PhysicsEntityPlacement(Matrix Transform, string Template);

/// <summary>Water or lava: <see cref="Effect"/> is a <see cref="RenderDeferredLiquidEffectContent"/> or a <see cref="LavaEffectContent"/>.</summary>
public sealed record LiquidContent(
    object Effect,
    VertexBufferContent? VertexBuffer,
    IndexBufferContent? IndexBuffer,
    VertexDeclarationContent? Declaration,
    int VertexStride,
    int NumVertices,
    int PrimitiveCount,
    bool HasCollision,
    bool Freezable,
    bool AutoFreeze);

/// <summary>A translucent force field (5 in the levels of the Steam release).</summary>
public sealed record ForceFieldContent(
    Vector3 Color,
    float Width,
    float AlphaPower,
    float AlphaFalloffPower,
    float MaxRadius,
    float RippleDistortion,
    float MapDistortion,
    bool VertexColorEnabled,
    ExternalReference? DisplacementMap,
    float Ttl,
    VertexBufferContent? VertexBuffer,
    IndexBufferContent? IndexBuffer,
    VertexDeclarationContent? Declaration,
    int VertexStride,
    int NumVertices,
    int PrimitiveCount);

public readonly record struct CollisionTriangle(int I0, int I1, int I2);

public sealed record CollisionMeshContent(IReadOnlyList<Vector3> Vertices, IReadOnlyList<CollisionTriangle> Triangles);

/// <summary>An oriented box; the stored position is a corner of the box, not its centre.</summary>
public sealed record TriggerAreaContent(string Name, Vector3 Corner, Vector3 SideLengths, Quaternion Orientation)
{
    public Vector3 Center => Corner + Vector3.Transform(SideLengths / 2, Orientation);
}

public sealed record LocatorContent(string Name, Matrix Transform, float Radius);

/// <summary>
/// Neighbours are triangle indices; null where the file stores 0xFFFF (no neighbour). In all
/// levels every other value is a valid triangle index.
/// </summary>
public sealed record NavTriangle(
    ushort A,
    ushort B,
    ushort C,
    ushort? NeighbourA,
    ushort? NeighbourB,
    ushort? NeighbourC,
    float CostAB,
    float CostBC,
    float CostCA,
    MovementProperties Movement);

public sealed record NavMeshContent(IReadOnlyList<Vector3> Vertices, IReadOnlyList<NavTriangle> Triangles);

/// <summary>
/// A pair of flags per mesh of an animated part (assumed: visible, casts shadows; not verified).
/// In the levels: (true, true) 1222 times, (false, true) 9, (true, false) 1.
/// </summary>
public sealed record MeshSetting(string MeshName, bool Visible, bool CastShadows);

/// <summary>A level light that follows an animated part.</summary>
public sealed record PartLightAttachment(string LightName, Matrix LocalTransform);

public sealed record PartCollisionContent(CollisionMaterial Material, CollisionMeshContent Mesh);

/// <summary>
/// A moving piece of a level (door, platform, bridge) with its own model, animation, liquids,
/// locators, effects, attached lights, collision, nav mesh and child parts. Model part effects
/// are shared resources of the level file.
/// </summary>
public sealed record AnimatedLevelPartContent(
    string Name,
    bool AffectShields,
    ModelContent? Model,
    IReadOnlyList<MeshSetting> MeshSettings,
    IReadOnlyList<LiquidContent> Liquids,
    IReadOnlyList<LocatorContent> Locators,
    float AnimationDuration,
    IReadOnlyList<AnimationKeyframe> Animation,
    IReadOnlyList<VisualEffectPlacement> Effects,
    IReadOnlyList<PartLightAttachment> Lights,
    PartCollisionContent? Collision,
    NavMeshContent? NavMesh,
    IReadOnlyList<AnimatedLevelPartContent> Children);
