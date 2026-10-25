using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// A piece of debris (a broken prop's gib): its model at a scale, flying off from where the prop broke and
/// tumbling, a box the size of its model's bounding sphere, for 10 to 20 s; then it goes. Characters pass
/// through it. A frozen prop's pieces are ice and leave no trail. A simulation object; the renderer
/// draws it and its trail.
/// </summary>
public sealed class Gib : Entity
{
    /// <summary>Its life: this long and up to as long again.</summary>
    public const float MinLife = 10;

    private readonly PhysicsWorld world;
    private Vector3 lastPosition;

    /// <param name="model">The model's asset.</param>
    /// <param name="centre">The model's bounding sphere's centre, in the model (the body is there).</param>
    /// <param name="radius">That sphere's radius, at scale 1.</param>
    public Gib(PhysicsWorld world, string model, float mass, float scale, Vector3 centre, float radius, Vector3 position, Vector3 velocity, Vector3 spin,
        float life, string? trail, bool frozen)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentException.ThrowIfNullOrEmpty(model);
        if (!(mass > 0) || !float.IsFinite(mass) || !(scale > 0) || !float.IsFinite(scale) || !(radius > 0) || !float.IsFinite(radius) || !float.IsFinite(life))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), "the mass, scale, radius and life must be finite, the first three positive");
        }
        (Model, Scale, Centre, Life, Trail, IsFrozen, lastPosition) = (model, scale, centre * scale, life, frozen ? null : trail, frozen, position);
        Body = world.AddBody(new BoxShape(new Vector3(radius * scale)), position, mass);
        (Body.Tag, Body.Velocity, Body.AngularVelocity) = (this, velocity, spin);
        Body.Listener = new Contacts();
    }

    /// <summary>
    /// The pieces of a prop broken at a point (its radius, at least 0.2): each flies off a random way (three
    /// times as far up as across, and always up) at its mass × 1.25, spinning against that way at 5, for
    /// 10 to 20 s. The original takes the speed from the mass of the cached piece it reuses; ours from the
    /// piece's own.
    /// </summary>
    public static Vector3 Launch(Random random, float radius, float mass, out Vector3 spin)
    {
        ArgumentNullException.ThrowIfNull(random);
        radius = MathF.Max(radius, 0.2f);
        var (around, down) = (MathF.Tau * random.NextSingle(), MathF.Acos(2 * random.NextSingle() - 1));
        var way = new Vector3(radius * MathF.Cos(around) * MathF.Sin(down), 3 * radius * MathF.Sin(around) * MathF.Sin(down), radius * MathF.Cos(down));
        way = way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) : Vector3.UnitY;
        spin = -way * 5;
        var velocity = way * mass * 1.25f;
        return velocity with { Y = MathF.Abs(velocity.Y) };
    }

    public PhysicsBody Body { get; }

    public string Model { get; }

    public float Scale { get; }

    /// <summary>The model's centre (scaled) from the body's.</summary>
    public Vector3 Centre { get; }

    /// <summary>How long it has left.</summary>
    public float Life { get; private set; }

    /// <summary>The effect it trails (none for ice).</summary>
    public string? Trail { get; }

    public bool IsFrozen { get; }

    public override Vector3 Position => Body.IsRemoved || world.IsDisposed ? lastPosition : Body.Position;

    public override float Radius => Body.Shape is BoxShape box ? box.Size.X / 2 : 0.1f;

    public override bool IsDead => Life <= 0;

    public override bool IsRemovable => Life <= 0;

    protected internal override IEnumerable<PhysicsBody> Bodies => [Body];

    /// <summary>Where its model is drawn: scaled, turned with the body, its centre on the body's.</summary>
    public Matrix4x4 Transform
    {
        get
        {
            var turn = Body.IsRemoved || world.IsDisposed ? Quaternion.Identity : Body.Orientation;
            var at = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(turn);
            at.Translation = Position - Vector3.Transform(Centre, turn);
            return at;
        }
    }

    public override void Update(float dt)
    {
        Life -= dt;
        if (!Body.IsRemoved && !world.IsDisposed)
        {
            lastPosition = Body.Position;
            Life = lastPosition.Y <= Missile.LowestY ? 0 : Life;              // fell out of the level
        }
    }

    protected internal override void OnRemoved()
    {
        if (!Body.IsRemoved && !world.IsDisposed)
        {
            world.Remove(Body);
        }
    }

    private sealed class Contacts : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact) =>
            contact.OtherBody?.Tag is Character or Gib ? ContactResponse.Ignore : ContactResponse.Collide;
    }
}
