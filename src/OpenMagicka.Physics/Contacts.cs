using System.Numerics;

namespace OpenMagicka.Physics;

/// <summary>How a body is set up when it is added.</summary>
[Flags]
public enum BodyOptions
{
    None = 0,

    /// <summary>Keeps the body upright, as for a character.</summary>
    LockRotation = 1,

    /// <summary>Continuous collision for bodies that move far in one tick (projectiles).</summary>
    Fast = 2,

    /// <summary>The body never falls asleep, so it keeps hearing its contacts while it stands still.</summary>
    NeverSleeps = 4,

    /// <summary>No velocity is lost to damping: whoever drives the body sets its velocity.</summary>
    Undamped = 8,
}

/// <summary>One contact of a body during a step, seen from that body.</summary>
/// <param name="Normal">Unit length, pointing from the other shape towards this body: the way the contact pushes it.</param>
/// <param name="Point">The contact point on this body.</param>
/// <param name="Penetration">Signed depth: positive while the shapes overlap.</param>
/// <param name="OtherBody">The other body, or null when it is a static mesh.</param>
/// <param name="OtherMesh">The static mesh touched, or null when it is a body.</param>
public readonly record struct Contact(Vector3 Normal, Vector3 Point, float Penetration, PhysicsBody? OtherBody, StaticMesh? OtherMesh);

/// <summary>What a body's listener decides about one contact.</summary>
public enum ContactResponse
{
    /// <summary>The contact pushes both bodies apart.</summary>
    Collide,

    /// <summary>This body is not stopped; the other one is still pushed away.</summary>
    PassThrough,

    /// <summary>Neither body is pushed: they pass through each other.</summary>
    Ignore,
}

/// <summary>
/// Hears a body's contacts during <see cref="PhysicsWorld.Step"/>, before the solver runs. It must
/// not add or remove bodies or meshes, and it may set velocities only for the next step.
/// </summary>
public interface IContactListener
{
    ContactResponse OnContact(PhysicsBody body, in Contact contact);
}
