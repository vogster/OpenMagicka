using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// A frog's tongue (OM-89, docs/engine/ENTITIES_AI.md §5.5), as the original's: a falling tip shot from its owner's mouth
/// that turns back at its full length, on the level, on a blow, or once it catches a character; it draws its catch back to
/// the mouth with it (no grip: the catch keeps its own state, only its place is the tip's) and dies there. Not a grip: it
/// lets go when the catch dies, is gripped, burns or is greased. Ours: one it let go it never catches again (the
/// original's, still on its tip, is caught the next step), it carries its catch without letting it fall, and it goes when
/// its frog dies or after <see cref="Lifetime"/>.
/// </summary>
public sealed class FrogTongue : Entity
{
    public const float ShootSpeed = 60, DragSpeed = 30, ReturnSpeed = 80, TipRadius = 0.5f, Mass = 4;

    /// <summary>Ours: a tongue whose way back is blocked goes after this long.</summary>
    public const float Lifetime = 5;

    /// <summary>A shot's way down is halved and turned up, as the original's (not made unit again).</summary>
    public const float Droop = -0.5f;

    /// <summary>Beyond this (the gaps along X and Z added) a catch is drawn back level.</summary>
    public const float LevelPull = 2;

    /// <summary>The effect where the tip meets the level.</summary>
    public const string HitEffect = "generic_dust";

    private readonly PhysicsWorld world;
    private readonly float maxLengthSquared;
    private Vector3 lastPosition;
    private bool dead;
    private bool struckLevel;
    private float age;
    private Character? letGo;

    /// <param name="maxLength">How far it reaches from the mouth.</param>
    public FrogTongue(PhysicsWorld world, Character owner, float maxLength)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (!float.IsFinite(maxLength))
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "must be finite");
        }
        maxLengthSquared = maxLength * maxLength;
        var mouth = owner.MouthFrame;
        var way = -new Vector3(mouth.M31, mouth.M32, mouth.M33);             // the mouth's forward
        way = way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) : owner.Body.Facing;
        way.Y *= Droop;
        if ((way with { Y = 0 }).LengthSquared() < 1e-6f)
        {
            way = owner.Body.Facing;                                          // ours: no upright frame for a shot straight up or down
        }
        (Start, End) = (mouth.Translation, mouth.Translation);
        Orientation = Matrix4x4.CreateWorld(Start, way, Vector3.UnitY);
        lastPosition = Start;
        Body = world.AddBody(new SphereShape(TipRadius), Start, Mass, BodyOptions.Fast);
        (Body.Friction, Body.Restitution, Body.Tag, Body.Velocity) = (0.8f, 0.333f, this, way * ShootSpeed);
        Body.Listener = new Contacts(this);
    }

    public PhysicsBody Body { get; }

    public Character Owner { get; }

    /// <summary>Whom it holds on its tip.</summary>
    public Character? Catch { get; private set; }

    public bool PullingBack { get; private set; }

    /// <summary>How fast it comes back.</summary>
    public float Speed { get; private set; }

    /// <summary>The mouth this frame, and the tip.</summary>
    public Vector3 Start { get; private set; }

    public Vector3 End { get; private set; }

    /// <summary>Its frame as shot (along the shot, upright): both its ends are drawn turned so.</summary>
    public Matrix4x4 Orientation { get; }

    public override Vector3 Position => Body.IsRemoved || world.IsDisposed ? lastPosition : Body.Position;

    public override Vector3 Velocity => Body.IsRemoved || world.IsDisposed ? Vector3.Zero : Body.Velocity;

    public override float Radius => TipRadius;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    public override bool IsDamageable => true;

    protected internal override IEnumerable<PhysicsBody> Bodies => [Body];

    /// <summary>A blow that hurts makes it let go and come back fast.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if (damage.Attack.HasFlag(AttackProperties.Damage) && damage.Amount > 0)
        {
            (PullingBack, Speed) = (true, ReturnSpeed);
            Drop();
        }
        return DamageResult.None;
    }

    public void Kill() => dead = true;

    public override void Update(float dt)
    {
        if (dead || Body.IsRemoved || world.IsDisposed)
        {
            return;
        }
        age += dt;
        if (Owner.Manager is null || Owner.IsDead || age > Lifetime)
        {
            dead = true;                                                      // ours: its frog gone or dead, or stuck too long
            return;
        }
        if (struckLevel)
        {
            struckLevel = false;
            Manager?.RaiseCue(this, new WorldEffectCue(HitEffect, Body.Position, -Vector3.UnitZ));   // the tip's own forward: it never turns
        }
        (Start, End) = (Owner.MouthFrame.Translation, Body.Position);
        Body.AngularVelocity = Vector3.Zero;
        if (!PullingBack)
        {
            if (Vector3.DistanceSquared(Start, End) >= maxLengthSquared)
            {
                (PullingBack, Speed) = (true, Catch is null ? ReturnSpeed : DragSpeed);
            }
            return;
        }
        var back = Start - End;
        var gap = back.LengthSquared();
        if (Catch is not null && MathF.Abs(back.X) + MathF.Abs(back.Z) > LevelPull)
        {
            back.Y = 0;
        }
        if (gap <= 1)
        {
            dead = true;
            return;
        }
        Body.Velocity = back.LengthSquared() > 1e-12f ? Vector3.Normalize(back) * Speed : Vector3.Zero;
        if (Catch is { } held)
        {
            if (held.IsDead || held.IsGripped || held.Manager is null || held.Statuses.Has(StatusEffects.Burning) || held.Statuses.Has(StatusEffects.Greased))
            {
                Drop();
            }
            else
            {
                held.Body.Body.Position = Body.Position - new Vector3(0, TipRadius + held.Body.HeightOffset, 0);
                held.Body.Body.Velocity = Vector3.Zero;                       // ours: carried, it gathers no fall
            }
        }
    }

    /// <summary>What it does on touching something, during the physics step.</summary>
    private ContactResponse OnContact(in Contact contact)
    {
        if (dead || contact.Liquid)
        {
            return ContactResponse.Ignore;
        }
        if (contact.OtherBody?.Tag is Character character)
        {
            if (Catch is not null || ReferenceEquals(character, Owner) || ReferenceEquals(character, letGo))
            {
                return ContactResponse.Ignore;                                // ours: it never knocks its own frog
            }
            (PullingBack, Catch, Speed) = (true, character, DragSpeed);
            return ContactResponse.Collide;
        }
        if (contact.OtherMesh is { Owner: null })
        {
            (PullingBack, Speed, struckLevel) = (true, ReturnSpeed, true);
            return ContactResponse.Collide;
        }
        return contact.OtherBody?.Tag is PhysicsProp { IsBoss: true } ? ContactResponse.Ignore : ContactResponse.Collide;
    }

    private void Drop() => (letGo, Catch) = (Catch ?? letGo, null);

    protected internal override void OnRemoved()
    {
        if (!world.IsDisposed && !Body.IsRemoved)
        {
            lastPosition = Body.Position;
            world.Remove(Body);
        }
    }

    private sealed class Contacts(FrogTongue owner) : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact) => owner.OnContact(contact);
    }
}
