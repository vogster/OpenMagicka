using System.Numerics;

namespace OpenMagicka.Physics;

/// <summary>A triangle of a static mesh, as three vertex indices.</summary>
public readonly record struct MeshTriangle(int A, int B, int C);

/// <summary>The shape of a moving body.</summary>
public abstract record BodyShape;

public sealed record SphereShape(float Radius) : BodyShape;

/// <summary>A box of the given full size, centred on the body.</summary>
public sealed record BoxShape(Vector3 Size) : BodyShape;

/// <summary>A capsule standing along Y; <paramref name="Length"/> is the cylinder between the end caps.</summary>
public sealed record CapsuleShape(float Radius, float Length) : BodyShape;

/// <summary>The nearest hit of a segment.</summary>
/// <param name="Normal">
/// The surface normal, unit length. A segment that starts inside a body hits it at fraction 0,
/// with the normal pointing back along the segment.
/// </param>
/// <param name="Fraction">How far along the segment, from 0 at its start towards 1 at its end (excluded).</param>
/// <param name="Material">The material of the static mesh hit, or null for a body.</param>
/// <param name="Body">The body hit, or null for a static mesh.</param>
public readonly record struct SegmentHit(Vector3 Position, Vector3 Normal, float Fraction, int? Material, PhysicsBody? Body);
