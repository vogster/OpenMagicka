using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// A thrown missile: a small falling box that fires its events on what it meets
/// (docs/engine/ENTITIES_AI.md §8.1.4). A simulation object: it changes in place.
/// </summary>
public sealed class Missile : Entity
{
    public const float Mass = 4;

    /// <summary>How long a struck thing is left alone (it stops the missile again meanwhile).</summary>
    public const float StrikeMemory = 0.25f;

    /// <summary>How long a thing the missile went through (overkilled, pierced) is left alone.</summary>
    public const float PassMemory = 5;

    /// <summary>After a level collision, the next counts only this much later.</summary>
    public const float CollisionPause = 0.15f;

    public const float LowestY = -50;

    /// <summary>Velocity-based damage scales by speed over this.</summary>
    public const float FullSpeed = 50;

    private readonly PhysicsWorld world;
    private readonly Dictionary<Entity, float> struck = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Entity, float> passed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RemoveEvent, int> bouncesLeft = new(ReferenceEqualityComparer.Instance);
    private readonly float radius;
    private bool dead;
    private bool collided;
    private float collisionTimer;
    private float collisionSpeed;
    private Vector3? pendingVelocity;
    private Vector3 lastPosition;
    private bool started;
    private float damageTaken;
    private Spells.Elements damageElements;
    private Vector3 lastKnownTarget;

    public Missile(PhysicsWorld world, Entity? owner, Vector3 position, Vector3 velocity, float radius, IReadOnlyList<EventGroup> events,
        Entity? target = null, float homing = 0, float danger = 2, bool facesVelocity = true)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(events);
        if (!(radius > 0) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be positive");
        }
        foreach (var (value, name) in new[] { (homing, nameof(homing)), (danger, nameof(danger)), (velocity.X, nameof(velocity)), (velocity.Y, nameof(velocity)),
            (velocity.Z, nameof(velocity)), (position.X, nameof(position)), (position.Y, nameof(position)), (position.Z, nameof(position)) })
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");                // before the body is in the world
            }
        }
        this.radius = radius;
        lastPosition = position;
        (Owner, Target, Homing, Danger, FacesVelocity) = (owner, target, homing, danger, facesVelocity);
        Events = new EventGroups(events);
        Piercing = events.SelectMany(g => g.Events).OfType<DamageEvent>().Any(d => d.Damage.Attack.HasFlag(AttackProperties.Piercing));
        Body = world.AddBody(new BoxShape(new Vector3(2 * radius)), position, Mass, BodyOptions.Fast);
        (Body.Friction, Body.Restitution, Body.Tag, Body.Velocity) = (0.8f, 0.1f, this, velocity);
        Body.Listener = new Contacts(this);
        SpawnSpeed = velocity.Length();
    }

    public PhysicsBody Body { get; }

    /// <summary>Who threw it: it passes through them, and strikes as them.</summary>
    public Entity? Owner { get; }

    /// <summary>What it homes in on.</summary>
    public Entity? Target { get; }

    /// <summary>How hard it turns towards its target; 0 when it never did or gave up.</summary>
    public float Homing { get; private set; }

    /// <summary>The target falling this far behind (dot of the headings) ends homing.</summary>
    public float HomingTolerance { get; init; } = -0.75f;

    /// <summary>How much the AI fears it.</summary>
    public float Danger { get; }

    public bool FacesVelocity { get; }

    public EventGroups Events { get; }

    public bool Piercing { get; }

    public float SpawnSpeed { get; }

    public float TimeAlive { get; private set; }

    /// <summary>Where the body is; once it has left the physics world, where it was last.</summary>
    public override Vector3 Position => Body.IsRemoved || world.IsDisposed ? lastPosition : Body.Position;

    public override Vector3 Velocity => Body.IsRemoved || world.IsDisposed ? Vector3.Zero : Body.Velocity;

    public override float Radius => radius;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    public override bool IsDamageable => true;

    /// <summary>A push throws it: its velocity becomes the ballistic one for its mass.</summary>
    public override bool Impulse(Vector3 from, float elevation, float power, float distance)
    {
        if (dead || Body.IsRemoved || world.IsDisposed)
        {
            return false;
        }
        var velocity = Combat.DamageRules.PushVelocity(from, Position, elevation, power, distance, Body.Mass, MathF.Abs(world.Settings.Gravity.Y));
        if (velocity.LengthSquared() <= 1e-6f)
        {
            return false;
        }
        Body.Velocity = velocity;
        return true;
    }

    /// <summary>Its Default events, once it is in the level (once only).</summary>
    public void Start()
    {
        if (started)
        {
            return;
        }
        started = true;
        if (Target is { } target)
        {
            lastKnownTarget = target.Position + new Vector3(0, target.Radius, 0);
        }
        Fire(new EventContext(EventTrigger.Default), null);
    }

    /// <summary>Damage only counts towards its Damaged events (a character's boosts included).</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        damage = Character.Boosted(damage, attacker);
        if (damage.Amount != 0)
        {
            (damageTaken, damageElements) = (damageTaken + damage.Amount, damageElements | damage.Element);
        }
        return DamageResult.None;
    }

    public void Kill()
    {
        if (!dead)
        {
            dead = true;
            Manager?.RaiseCue(this, new ItemEffectEndCue(this));
        }
    }

    /// <summary>A missile that ended does nothing more (OpenMagicka's choice: no second blast or late effect in its last frame).</summary>
    public override void Update(float dt)
    {
        if (dead)
        {
            return;
        }
        Forget(struck, dt);
        Forget(passed, dt);
        TimeAlive += dt;
        if (pendingVelocity is { } velocity)
        {
            (Body.Velocity, pendingVelocity) = (velocity, null);
        }
        Steer(dt);
        var context = new EventContext(EventTrigger.Timer, Time: TimeAlive);
        if (collided)
        {
            if (collisionTimer <= float.Epsilon)
            {
                context = context with { Trigger = context.Trigger | EventTrigger.Collision, Threshold = collisionSpeed };
            }
            collisionTimer += dt;
            if (collisionTimer >= CollisionPause)
            {
                (collided, collisionTimer) = (false, 0);
            }
        }
        collisionSpeed = 0;
        if (damageTaken != 0)
        {
            context = context with { Trigger = context.Trigger | EventTrigger.Damaged, HitPoints = (int)damageTaken, Elements = damageElements };
        }
        Fire(context, null);                                                  // a prop it hit will be the target, with props
        (damageTaken, damageElements) = (0, Spells.Elements.None);
        if (Position.Y <= LowestY)
        {
            Kill();
        }
    }

    /// <summary>Turns towards the target's last-known place (raised by its radius); gives up once it falls 139° behind.</summary>
    private void Steer(float dt)
    {
        if (Homing <= 0)
        {
            return;
        }
        if (Target is { IsDead: false, Manager: not null } target)
        {
            lastKnownTarget = target.Position + new Vector3(0, target.Radius, 0);
        }
        var velocity = Body.Velocity;
        var towards = lastKnownTarget - Body.Position;
        if (velocity.LengthSquared() <= 1e-6f || towards.LengthSquared() <= 1e-12f)
        {
            return;
        }
        var (heading, wanted) = (Vector3.Normalize(velocity), Vector3.Normalize(towards));
        if (Vector3.Dot(heading, wanted) <= HomingTolerance)
        {
            Homing = 0;
            return;
        }
        var axis = Vector3.Cross(heading, wanted);
        if (axis.LengthSquared() > 1e-12f)
        {
            Body.Velocity = Vector3.Transform(velocity, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), dt * 10 * Homing));
        }
    }

    private static void Forget(Dictionary<Entity, float> memory, float dt)
    {
        if (memory.Count == 0)
        {
            return;
        }
        foreach (var entity in memory.Keys.ToList())
        {
            memory[entity] -= dt;
            if (memory[entity] <= 0)
            {
                memory.Remove(entity);
            }
        }
    }

    private DamageResult Fire(in EventContext context, Entity? target) => Events.Fire(context, (e, at) => Run(e, at, target));

    private DamageResult Run(ItemEvent e, Vector3? at, Entity? target)
    {
        switch (e)
        {
            case DamageEvent damage when target is not null:
                return target.Damage(Scaled(damage.Damage, damage.VelocityBased), Owner, Position);
            case SplashEvent splash when Manager is { } manager:
                return Splash.Hit(manager, Owner, this, at ?? Position, splash.Radius, splash.Damage);
            case DamageOwnerEvent damage when Owner is { Manager: not null }:
                return Owner.Damage(Scaled(damage.Damage, damage.VelocityBased), Owner, Position);
            case SoundEvent sound:
                Manager?.RaiseCue(this, new SoundCue(sound.Cue, sound.Banks, at ?? Position, Heading, sound.Magnitude));
                return DamageResult.None;
            case EffectEvent effect:
                Manager?.RaiseCue(this, new ItemEffectCue(effect.Effect, at ?? Position, effect.WorldAlign ? Vector3.UnitY : Heading, effect.Follow && !dead, this));
                return DamageResult.None;
            case RemoveEvent remove:
                var left = bouncesLeft.TryGetValue(remove, out var count) ? count : remove.Bounces;
                if (left <= 0)
                {
                    Kill();
                }
                bouncesLeft[remove] = left - 1;
                return DamageResult.None;
            default:
                return DamageResult.None;
        }
    }

    private DamageEntry Scaled(DamageEntry damage, bool velocityBased) =>
        velocityBased ? damage with { Amount = damage.Amount * Body.Velocity.Length() / FullSpeed } : damage;

    /// <summary>Along its velocity when it faces it, else along its own turned forward.</summary>
    private Vector3 Heading => Body.IsRemoved || world.IsDisposed ? -Vector3.UnitZ
        : FacesVelocity && Body.Velocity.LengthSquared() > 1e-12f ? Vector3.Normalize(Body.Velocity) : Vector3.Transform(-Vector3.UnitZ, Body.Orientation);

    /// <summary>What the missile does on touching something, during the physics step (§8.1.4).</summary>
    private ContactResponse OnContact(in Contact contact)
    {
        if (dead)
        {
            return ContactResponse.Ignore;
        }
        if (contact.OtherMesh is not null)
        {
            if (!collided)
            {
                collided = true;
                collisionSpeed = MathF.Max(collisionSpeed, Body.Velocity.LengthSquared());
            }
            return ContactResponse.Collide;
        }
        if (contact.OtherBody?.Tag is not Entity entity)
        {
            collided = true;
            collisionSpeed = MathF.Max(collisionSpeed, Body.Velocity.LengthSquared());
            return ContactResponse.Collide;
        }
        if (ReferenceEquals(entity, Owner) || entity.IsDead || !entity.IsDamageable || entity is Character { IsEthereal: true }
            || (entity is Missile other && ReferenceEquals(other.Owner, Owner)) || passed.ContainsKey(entity))
        {
            return ContactResponse.Ignore;
        }
        if (struck.ContainsKey(entity))
        {
            return entity is IShield || !Piercing ? ContactResponse.Collide : ContactResponse.Ignore;
        }
        var result = Fire(new EventContext(EventTrigger.Hit), entity);
        if (result.HasFlag(DamageResult.OverKilled) || result.HasFlag(DamageResult.Pierced))
        {
            if (!result.HasFlag(DamageResult.Pierced))
            {
                var speed = Body.Velocity.Length();
                var away = Body.Position - entity.Position;
                pendingVelocity = (away.LengthSquared() > 1e-12f ? Vector3.Normalize(away) * speed * 0.3f : Vector3.Zero) + Body.Velocity * 0.9f;
            }
            passed[entity] = PassMemory;
            return ContactResponse.Ignore;
        }
        struck[entity] = StrikeMemory;
        return entity is IShield || result.HasFlag(DamageResult.Deflected) || result.HasFlag(DamageResult.Hit)
            ? ContactResponse.Collide
            : ContactResponse.Ignore;
    }

    /// <summary>A missile's body is spent once it leaves: a removed missile cannot come back.</summary>
    protected internal override void OnAdded()
    {
        if (Body.IsRemoved)
        {
            throw new InvalidOperationException("a removed missile cannot be added again; throw a new one");
        }
    }

    /// <summary>The body leaves the physics world with the missile.</summary>
    protected internal override void OnRemoved()
    {
        if (!world.IsDisposed && !Body.IsRemoved)
        {
            lastPosition = Body.Position;
            world.Remove(Body);
        }
    }

    private sealed class Contacts(Missile owner) : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact) => owner.OnContact(contact);
    }
}
