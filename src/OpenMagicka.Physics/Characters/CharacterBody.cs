using System.Numerics;

namespace OpenMagicka.Physics.Characters;

/// <summary>
/// A character's body: an upright capsule steered by gameplay (docs/engine/ARCHITECTURE.md §7.4).
/// Each tick: set <see cref="Conditions"/>, give commands, call <see cref="BeforeStep"/>, then
/// <see cref="PhysicsWorld.Step"/> (the contact rules run in it), then <see cref="AfterStep"/> for
/// what happened. The state belongs to the simulation and changes in place.
/// </summary>
public sealed partial class CharacterBody
{
    /// <summary>Time after the last ground contact during which the character still counts as on the ground.</summary>
    public const float GroundGrace = 0.2f;

    /// <summary>The ground timer after lifting off: past the grace at once.</summary>
    private const float LiftOffTimer = 0.3f;

    public const float FlyHeight = 2;

    private const float Epsilon = 1e-6f;

    private readonly PhysicsWorld world;
    private Vector3 movement;
    private Vector3 wantedDirection = -Vector3.UnitZ;
    private float speedMultiplier = 1;
    private float maxSpeed;
    private float slow = 1;
    private CharacterConditions conditions = CharacterConditions.Default;
    private Vector3 pendingVelocity;
    private Vector3 extraForce;
    private readonly List<(Vector3 Velocity, float LiftOff)> staged = [];
    private float sinceGround;
    private bool leavingGround;

    public CharacterBody(PhysicsWorld world, Vector3 position, CharacterShape shape)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shape);
        shape.Validate();
        this.world = world;
        Shape = shape;
        Body = world.AddBody(new CapsuleShape(shape.Radius, shape.Length), position, shape.Mass,
            BodyOptions.LockRotation | BodyOptions.NeverSleeps | BodyOptions.Undamped);
        Body.Friction = 0;
        Body.Restitution = 0;
        Body.AffectedByGravity = !shape.Flies;
        Body.Listener = new Contacts(this);
        Body.PassesPassableMeshes = true;
        Body.FeelsLiquids = true;
        MaxSpeed = shape.Speed;
        LastPositionOnGround = position;
    }

    public PhysicsBody Body { get; }

    public CharacterShape Shape { get; private set; }

    /// <summary>From the centre of the capsule down to the feet (negative).</summary>
    public float HeightOffset => -(Shape.Radius + 0.5f * Shape.Length);

    /// <summary>The character's state as gameplay sees it; set at the start of each tick, before commands.</summary>
    public CharacterConditions Conditions
    {
        get => conditions;
        set => conditions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Horizontal, at most 1 long; a non-zero value also sets <see cref="WantedDirection"/>.</summary>
    public Vector3 Movement
    {
        get => movement;
        set
        {
            var flat = Flat(Vectors.ThrowIfNotFinite(value, nameof(Movement)));
            var length = flat.Length();
            if (float.IsInfinity(length))
            {
                (flat, length) = (Unit(flat), 1);
            }
            if (length * length > Epsilon)
            {
                wantedDirection = flat / length;
                movement = length > 1 ? wantedDirection : flat;
            }
            else
            {
                movement = Vector3.Zero;
                IsMoving = false;
            }
        }
    }

    /// <summary>The horizontal direction to face; a zero value is ignored.</summary>
    public Vector3 WantedDirection
    {
        get => wantedDirection;
        set
        {
            var flat = Flat(Vectors.ThrowIfNotFinite(value, nameof(WantedDirection)));
            if (flat.LengthSquared() > Epsilon)
            {
                wantedDirection = Unit(flat);
            }
        }
    }

    /// <summary>The horizontal direction the character faces.</summary>
    public Vector3 Facing { get; private set; } = -Vector3.UnitZ;

    /// <summary>Turns at once to face a horizontal direction (and wants it); a zero value is ignored.</summary>
    public void Face(Vector3 direction)
    {
        var flat = Flat(Vectors.ThrowIfNotFinite(direction, nameof(direction)));
        if (flat.LengthSquared() > Epsilon)
        {
            Facing = wantedDirection = Unit(flat);
        }
    }

    public float MaxSpeed
    {
        get => maxSpeed;
        set => maxSpeed = NonNegative(value, nameof(MaxSpeed));
    }

    /// <summary>Clamped to 0..100.</summary>
    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = float.IsNaN(value) ? throw new ArgumentOutOfRangeException(nameof(SpeedMultiplier), value, "not a number") : Math.Clamp(value, 0, 100);
    }

    /// <summary>A factor on steering for the coming tick only.</summary>
    public float Slow
    {
        get => slow;
        set => slow = NonNegative(value, nameof(Slow));
    }

    public bool AllowMove { get; set; } = true;

    public bool AllowRotate { get; set; } = true;

    /// <summary>Face away from <see cref="WantedDirection"/>.</summary>
    public bool RunBackward { get; set; }

    public bool IsTouchingGround { get; private set; } = true;

    public bool IsPushed { get; private set; }

    public bool IsJumping { get; private set; }

    public bool IsMoving { get; private set; }

    /// <summary>The material of the level under the feet (a liquid's while in it); null until the first ground contact.</summary>
    public int? GroundMaterial { get; private set; }

    /// <summary>A body it never collides with (its gripper or its victim, OM-89).</summary>
    public PhysicsBody? IgnoredBody { get; set; }

    /// <summary>The body is in a liquid (it felt one in the last step; OM-88).</summary>
    public bool IsInWater { get; private set; }

    /// <summary>How deep its feet are in the liquid (its surface over them), as the last step in one found it: kept after it leaves, as the original's.</summary>
    public float WaterDepth { get; private set; }

    /// <summary>The material of the liquid it is in (water or lava), as the last step in one found it.</summary>
    public int? LiquidMaterial { get; private set; }

    /// <summary>It stands on ice (a liquid's frozen triangles that slip): it slides, as on grease.</summary>
    public bool IsOnIce { get; private set; }

    /// <summary>On the ground, and not on ice (the minds' sense of standing).</summary>
    public bool IsTouchingSolidGround => IsTouchingGround && !IsOnIce;

    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    public Vector3 LastPositionOnGround { get; private set; }

    /// <summary>An impulse: changes the velocity by impulse / mass at the next step.</summary>
    public void Push(Vector3 impulse) => PushVelocity(impulse / Body.Mass, liftOff: 0.5f);

    /// <summary>A direct velocity change at the next step.</summary>
    public void PushVelocity(Vector3 velocity) => PushVelocity(velocity, liftOff: 2.7f);

    private void PushVelocity(Vector3 velocity, float liftOff)
    {
        if (Conditions.IsEntangled)
        {
            return;
        }
        Vectors.ThrowIfNotFinite(velocity, nameof(velocity));
        if (world.IsStepping)
        {
            staged.Add((velocity, liftOff));                                   // a push from a contact: the body changes once the step is over
            return;
        }
        pendingVelocity += velocity;
        IsPushed = true;
        leavingGround |= pendingVelocity.Y > liftOff;
    }

    /// <summary>No push any more (a gripper about to lose its grip, OM-89): what was given and staged goes too.</summary>
    public void CancelPush()
    {
        (pendingVelocity, IsPushed, leavingGround) = (Vector3.Zero, false, false);
        staged.Clear();
    }

    /// <summary>Pushes given during the step, taken once it is over.</summary>
    private void TakeStagedPushes()
    {
        foreach (var (velocity, liftOff) in staged)
        {
            pendingVelocity += velocity;
            IsPushed = true;
            leavingGround |= pendingVelocity.Y > liftOff;
        }
        staged.Clear();
    }

    /// <summary>
    /// A new capsule (a crouch), its bottom kept where it was; not during a step. Growing back under a
    /// low ceiling starts inside it, as the original.
    /// </summary>
    public void Reshape(float radius, float length)
    {
        var shape = Shape with { Radius = radius, Length = length };
        shape.Validate();
        var drop = (Shape.Length - length) / 2 + (Shape.Radius - radius);
        Body.Reshape(new CapsuleShape(radius, length));
        Body.Position -= new Vector3(0, drop, 0);
        Shape = shape;
    }

    /// <summary>Jumps with the given velocity; only from the ground, and only when allowed to move.</summary>
    public bool Jump(Vector3 velocity)
    {
        if (Conditions.IsEntangled || !IsTouchingGround || leavingGround || !AllowMove)
        {
            return false;
        }
        pendingVelocity += Vectors.ThrowIfNotFinite(velocity, nameof(velocity));
        IsJumping = true;
        leavingGround = true;
        Body.Velocity = Vector3.Zero;
        return true;
    }

    /// <summary>A velocity an animation adds once, shortened by how far the body already moves along it.</summary>
    public void AddExtraForce(Vector3 force)
    {
        if (!Conditions.IsEntangled && speedMultiplier > Epsilon)
        {
            extraForce = Vectors.ThrowIfNotFinite(force, nameof(force));
        }
    }

    /// <summary>Steers the body for the coming step.</summary>
    public void BeforeStep(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        var velocity = Body.Velocity;
        stepTime = dt;
        if (Shape.Flies)
        {
            sinceGround = 0;
            if (conditions.FlyGround is { } ground)
            {
                velocity.Y += 0.2f * (ground + FlyHeight - Body.Position.Y) - 0.02f * velocity.Y;
            }
        }
        else
        {
            sinceGround += dt;
        }
        IsTouchingGround = sinceGround <= GroundGrace || conditions.IsLevitating;
        sinceGroundBeforeStep = sinceGround;
        velocity += pendingVelocity;
        pendingVelocity = Vector3.Zero;
        var moving = false;
        if (leavingGround)
        {
            (sinceGround, IsTouchingGround, leavingGround) = (LiftOffTimer, false, false);
        }
        else if (IsPushed)
        {
            velocity = Pushed(velocity, dt);
        }
        else if (IsTouchingGround)
        {
            velocity = Walk(velocity, dt, out moving);
        }
        velocity = WithExtraForce(velocity);
        if (IsInWater)
        {
            velocity *= MathF.Pow(MathF.Max(MathF.Pow(Math.Clamp(1 - WaterDepth, 0, 1), 100), WaterDamping), dt);   // the liquid holds it back, as the original's
        }
        Slow = 1;
        IsMoving = moving;
        Body.Velocity = velocity;
    }

    private bool Slippery => Conditions.IsOnGrease || Conditions.IsLevitating || IsOnIce;

    /// <summary>The least share of its speed a liquid lets a body keep each second.</summary>
    public const float WaterDamping = 0.1f;

    /// <summary>Wading: a walk's wanted speed at a depth (1 − depth / 2, kept within a half and all), on the ground in a liquid.</summary>
    public static float Wading(float depth) => Math.Clamp(1 - 0.5f * depth, 0.5f, 1);

    private Vector3 Pushed(Vector3 velocity, float dt)
    {
        if (IsTouchingGround)
        {
            if (Conditions.IsKnockedDown)
            {
                IsPushed = false;
                velocity = Vector3.Zero;
            }
            else
            {
                velocity *= MathF.Pow(Slippery ? 0.1f : 0.02f, dt);
                velocity.Y = MathF.Min(velocity.Y, 0);
                IsPushed = velocity.LengthSquared() >= MaxSpeed * MaxSpeed * 0.33f;
            }
        }
        var away = -Flat(velocity);
        if (away.LengthSquared() > Epsilon)
        {
            Facing = Turn(Facing, Vector3.Normalize(away), Conditions.TurnSpeed * dt);
        }
        return velocity;
    }

    private Vector3 Walk(Vector3 velocity, float dt, out bool moving)
    {
        if (!Slippery)
        {
            (velocity.X, velocity.Z) = (0, 0);
        }
        if (!Shape.Flies && !IsJumping && velocity.Y > 0)
        {
            velocity.Y *= 0.8f;
        }
        (IsJumping, IsPushed) = (false, false);
        var change = Flat(movement * (MaxSpeed * speedMultiplier * (IsInWater ? Wading(WaterDepth) : 1)) - velocity);
        if (Slippery)
        {
            change *= MathF.Min(dt, 1);
        }
        change *= Slow;
        moving = false;
        if (AllowMove)
        {
            velocity += change;
            moving = Flat(velocity).LengthSquared() >= Epsilon;
        }
        if (AllowRotate)
        {
            Facing = Turn(Facing, RunBackward ? -wantedDirection : wantedDirection, Conditions.TurnSpeed * dt);
        }
        return velocity;
    }

    private Vector3 WithExtraForce(Vector3 velocity)
    {
        if (extraForce.LengthSquared() <= Epsilon)
        {
            return velocity;
        }
        var force = extraForce;
        if (velocity.LengthSquared() > Epsilon)
        {
            var length = force.Length();
            force *= Math.Clamp(1 - Vector3.Dot(force / length, velocity / length), 0, 1);
        }
        extraForce = Vector3.Zero;
        return velocity + force;
    }

    /// <summary>Turns <paramref name="share"/> of the way (clamped to 0..1) as a normalised quaternion blend.</summary>
    internal static Vector3 Turn(Vector3 facing, Vector3 target, float share)
    {
        var blended = Quaternion.Lerp(Yaw(facing), Yaw(target), Math.Clamp(share, 0, 1));
        var turned = Flat(Vector3.Transform(-Vector3.UnitZ, blended));
        return turned.LengthSquared() > Epsilon ? Vector3.Normalize(turned) : facing;
    }

    /// <summary>The rotation about Y that turns −Z (forward) to <paramref name="forward"/>.</summary>
    private static Quaternion Yaw(Vector3 forward) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-forward.X, -forward.Z));

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    /// <summary>A unit vector along <paramref name="v"/>, also when its length overflows.</summary>
    private static Vector3 Unit(Vector3 v)
    {
        var largest = MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));
        return Vector3.Normalize(v / largest);
    }

    private static float NonNegative(float value, string name) =>
        value >= 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must not be negative");
}
