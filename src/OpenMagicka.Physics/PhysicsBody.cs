using System.Numerics;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace OpenMagicka.Physics;

/// <summary>
/// A moving body of a <see cref="PhysicsWorld"/>. Its state belongs to the simulation, so it is read
/// and set in place rather than copied. Once removed, or once its world is disposed, only
/// <see cref="Shape"/> and <see cref="IsRemoved"/> may be read.
/// </summary>
public sealed class PhysicsBody
{
    private readonly PhysicsWorld owner;
    private readonly RigidBody body;

    internal PhysicsBody(PhysicsWorld owner, RigidBody body, BodyShape shape)
    {
        this.owner = owner;
        this.body = body;
        Shape = shape;
    }

    /// <summary>The engine body, checked to be alive: its data is freed on removal.</summary>
    internal RigidBody Body
    {
        get
        {
            owner.ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(IsRemoved, this);
            return body;
        }
    }

    /// <summary>The engine body without the check, for identity comparisons only.</summary>
    internal RigidBody Identity => body;

    internal bool BelongsTo(PhysicsWorld world) => ReferenceEquals(owner, world);

    public BodyShape Shape { get; private set; }

    /// <summary>Swaps the body's shape, keeping its mass, position and velocity; not during a step.</summary>
    public void Reshape(BodyShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var live = Body;
        owner.ThrowIfStepping();
        ThrowIfSuspended();
        var collider = PhysicsWorld.ToJitter(shape);
        var old = live.Shapes.ToList();
        live.AddShape(collider, MassInertiaUpdateMode.Preserve);
        live.RemoveShapes(old, MassInertiaUpdateMode.Preserve);
        live.SetActivationState(true);
        Shape = shape;
    }

    public bool IsRemoved { get; internal set; }

    /// <summary>Goes through a <see cref="StaticMesh.Passable"/> mesh (a character through a shield going down).</summary>
    public bool PassesPassableMeshes { get; set; }

    public Vector3 Position
    {
        get => Body.Position.ToNumerics();
        set => Body.Position = Vectors.ThrowIfNotFinite(value, nameof(Position)).ToJitter();
    }

    public Quaternion Orientation
    {
        get
        {
            var q = Body.Orientation;
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W) || value.LengthSquared() < 1e-12f)
            {
                throw new ArgumentOutOfRangeException(nameof(Orientation), value, "must be a finite rotation");
            }
            var unit = Quaternion.Normalize(value);
            Body.Orientation = new JQuaternion(unit.X, unit.Y, unit.Z, unit.W);
        }
    }

    /// <summary>Whether the body never moves (made <see cref="BodyOptions.Static"/>).</summary>
    public bool IsStatic => (IsSuspended ? suspendedMotion : Body.MotionType) == MotionType.Static;

    private List<Jitter2.Collision.Shapes.RigidBodyShape>? suspendedShapes;
    private MotionType suspendedMotion;
    private Vector3 suspendedVelocity, suspendedSpin;

    /// <summary>The body is out of the simulation for a while (its scene left): it touches nothing and stays where it was.</summary>
    public bool IsSuspended => suspendedShapes is not null;

    /// <summary>Takes the body out of the simulation, keeping its place, turn, velocity and settings; not during a step.</summary>
    public void Suspend()
    {
        var live = Body;
        owner.ThrowIfStepping();
        if (IsSuspended)
        {
            return;
        }
        (suspendedMotion, suspendedVelocity, suspendedSpin) = (live.MotionType, live.Velocity.ToNumerics(), live.AngularVelocity.ToNumerics());
        suspendedShapes = [.. live.Shapes];
        live.RemoveShapes(suspendedShapes, MassInertiaUpdateMode.Preserve);
        live.MotionType = MotionType.Static;
    }

    private void ThrowIfSuspended()
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("the body is suspended");
        }
    }

    /// <summary>Puts a suspended body back into the simulation as it was; not during a step.</summary>
    public void Resume()
    {
        if (IsRemoved || suspendedShapes is not { } shapes)
        {
            return;
        }
        var live = Body;
        owner.ThrowIfStepping();
        live.MotionType = suspendedMotion;
        live.AddShapes(shapes, MassInertiaUpdateMode.Preserve);
        suspendedShapes = null;
        if (suspendedMotion != MotionType.Static)
        {
            (live.Velocity, live.AngularVelocity) = (suspendedVelocity.ToJitter(), suspendedSpin.ToJitter());
        }
        live.SetActivationState(true);
    }

    /// <summary>Setting a velocity wakes the body up.</summary>
    public Vector3 Velocity
    {
        get => Body.Velocity.ToNumerics();
        set
        {
            var live = Body;
            live.Velocity = Vectors.ThrowIfNotFinite(value, nameof(Velocity)).ToJitter();
            live.SetActivationState(true);
        }
    }

    /// <summary>How fast it turns, in radians per second about each axis.</summary>
    public Vector3 AngularVelocity
    {
        get => Body.AngularVelocity.ToNumerics();
        set
        {
            var live = Body;
            live.AngularVelocity = Vectors.ThrowIfNotFinite(value, nameof(AngularVelocity)).ToJitter();
            live.SetActivationState(true);
        }
    }

    public float Mass => Body.Mass;

    /// <summary>A new mass, its inertia following (armour makes a character heavy).</summary>
    public void SetMass(float mass)
    {
        owner.ThrowIfStepping();
        ThrowIfSuspended();
        if (!(mass > 0) || !float.IsFinite(mass))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "must be positive");
        }
        Body.SetMassInertia(mass);
    }

    /// <summary>A contact's friction is the geometric mean of both surfaces' (0 slides on anything).</summary>
    public float Friction
    {
        get => Body.Friction;
        set => Body.Friction = value >= 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(Friction), value, "must not be negative");
    }

    /// <summary>Bounciness from 0 to 1.</summary>
    public float Restitution
    {
        get => Body.Restitution;
        set => Body.Restitution = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Restitution), value, "must be from 0 to 1");
    }

    public bool AffectedByGravity
    {
        get => Body.AffectedByGravity;
        set => Body.AffectedByGravity = value;
    }

    /// <summary>Hears the body's contacts during a step. A body has one listener for good: it can be set once.</summary>
    public IContactListener? Listener
    {
        get => listener;
        set
        {
            if (listener is not null && !ReferenceEquals(listener, value))
            {
                throw new InvalidOperationException("the body already has a contact listener");
            }
            listener = value;
        }
    }

    private IContactListener? listener;

    /// <summary>Whatever the game attaches to the body, such as the entity it belongs to.</summary>
    public object? Tag { get; set; }

    /// <summary>False while the body sleeps.</summary>
    public bool IsAwake => Body.IsActive;
}
