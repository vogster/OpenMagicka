using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Props;

/// <summary>A prop's carried effect plays on it (the renderer follows the prop's transform).</summary>
public sealed record PropEffectCue(PhysicsProp Prop, PropEffect Effect) : AnimationCue;

/// <summary>A prop's carried effects stop (it is gone).</summary>
public sealed record PropEffectsEndCue(PhysicsProp Prop) : AnimationCue;

/// <summary>A broken prop's pieces fly apart from where it stood (debris for the renderer, frozen ones shattering as ice).</summary>
public sealed record GibsCue(IReadOnlyList<PropGib> Gibs, Vector3 Position, float Radius, string TrailEffect, bool Frozen) : AnimationCue;

/// <summary>
/// A level's physics prop (docs/engine/ENTITIES_AI.md §8.4): a crate, a barrel, a door. Its body is its
/// template's box: static unless it moves, pushed by characters only when pushable, and when it is not
/// solid entities pass through it. Each entity that touches it sets off its hit events, once a quarter
/// of a second. It carries its template's effects. A damageable one takes damage and breaks
/// (<c>PhysicsProp.Damage.cs</c>).
/// </summary>
public sealed partial class PhysicsProp : Entity
{
    /// <summary>How long an entity that touched it is not heard again.</summary>
    public const float HitMemory = 0.25f;

    /// <summary>The smallest side a box may have (a template with no box still needs a body).</summary>
    private const float Thinnest = 0.01f;

    private readonly PhysicsWorld world;
    private readonly Dictionary<Entity, float> hits = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entity> touched = [];
    private readonly List<Entity> forgotten = [];
    private readonly object touchedLock = new();                              // the physics steps on one thread now; kept should it ever not
    private readonly Quaternion boxTurn;
    private EntityManager? level;
    private Vector3 lastPosition;
    private Quaternion lastTurn;
    private float restingTimer = 1;
    private bool dead, started;

    /// <param name="transform">Where the prop is (its template's origin), turned with it (scale left out).</param>
    public PhysicsProp(PhysicsWorld world, PropTemplate template, Matrix4x4 transform)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Template = template ?? throw new ArgumentNullException(nameof(template));
        template.Validate();
        if (!Matrix4x4.Decompose(transform, out _, out var turn, out var origin) || !float.IsFinite(origin.X + origin.Y + origin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(transform), transform, "must be a finite placement");
        }
        boxTurn = Quaternion.Normalize(template.Box.Orientation);
        var sides = Vector3.Max(template.Box.Sides, new Vector3(Thinnest));
        Radius = template.Box.Sides.Length() / 2;
        var centre = origin + Vector3.Transform(template.Box.Centre, turn);
        Body = world.AddBody(new BoxShape(sides), centre, template.Mass, template.Movable ? BodyOptions.None : BodyOptions.Static);
        try
        {
            Body.Orientation = Quaternion.Concatenate(boxTurn, turn);
            (Body.Tag, Body.AffectedByGravity, Body.Listener) = (this, template.Movable, new Contacts(this));
        }
        catch
        {
            world.Remove(Body);
            throw;
        }
        (lastPosition, lastTurn) = (centre, Body.Orientation);
        Events = new EventGroups(template.Events);
        HitPoints = template.MaxHitPoints;
    }

    public PropTemplate Template { get; }

    public PhysicsBody Body { get; }

    /// <summary>Its event groups, with their own state (a fired once-only group stays fired).</summary>
    public EventGroups Events { get; }

    public override Vector3 Position => Body.IsRemoved || world.IsDisposed ? lastPosition : Body.Position;

    public override Vector3 Velocity => Body.IsRemoved || world.IsDisposed || Body.IsStatic ? Vector3.Zero : Body.Velocity;

    public override float Radius { get; }

    /// <summary>Where its template's origin is now (its box's place and turn undone): what the renderer draws at.</summary>
    public Matrix4x4 Transform
    {
        get
        {
            var turn = Quaternion.Concatenate(Quaternion.Inverse(boxTurn), Body.IsRemoved || world.IsDisposed ? lastTurn : Body.Orientation);
            var at = Matrix4x4.CreateFromQuaternion(turn);
            at.Translation = Position - Vector3.Transform(Template.Box.Centre, turn);
            return at;
        }
    }

    public override bool IsPhysicsObject => true;

    protected internal override IEnumerable<PhysicsBody> Bodies => [Body];

    public override bool IsDamageable => Template.IsDamageable;

    public override bool IsDead => dead || (Template.IsDamageable && HitPoints <= 0);

    public override bool IsRemovable => IsDead;

    /// <summary>It has lain still for a second.</summary>
    public bool IsResting => restingTimer < 0;

    /// <summary>Gone: a plain one at once; a damageable one breaks first (its death trigger, events and pieces).</summary>
    public void Kill()
    {
        if (Template.IsDamageable)
        {
            HitPoints = 0;
            Break();
        }
        dead = true;
    }

    /// <summary>A push or a pull throws a prop that moves; one that stands stays.</summary>
    public override bool Impulse(Vector3 from, float elevation, float power, float distance)
    {
        if (IsDead || !Template.Movable || Body.IsRemoved || world.IsDisposed)
        {
            return false;
        }
        var velocity = DamageRules.PushVelocity(from, Position, elevation, power, distance, Body.Mass, MathF.Abs(world.Settings.Gravity.Y));
        if (velocity.LengthSquared() <= 1e-6f)
        {
            return false;
        }
        Body.Velocity += velocity;
        return true;
    }

    /// <summary>A boss fight's prop: its fight's spawn makes it again on a checkpoint's replay, so the checkpoint does not keep it.</summary>
    public bool IsBoss { get; set; }

    /// <summary>How long it still shows highlighted (a script's highlightPhys); not above 0, not.</summary>
    public float Highlighted { get; private set; } = -1;

    public void Highlight(float time) => Highlighted = float.IsFinite(time) ? time : throw new ArgumentOutOfRangeException(nameof(time), time, "must be finite");

    public override void Update(float dt)
    {
        Highlighted = MathF.Max(-1, Highlighted - dt);
        if (!started)
        {
            started = true;
            foreach (var effect in Template.Effects)
            {
                Manager?.RaiseCue(this, new PropEffectCue(this, effect));
            }
        }
        if (hits.Count > 0)
        {
            forgotten.Clear();
            forgotten.AddRange(hits.Keys);
            foreach (var entity in forgotten)
            {
                if ((hits[entity] -= dt) <= 0)
                {
                    hits.Remove(entity);
                }
            }
        }
        Entity[] touching;
        lock (touchedLock)
        {
            touching = [.. touched];
            touched.Clear();
        }
        foreach (var entity in touching)
        {
            if (!IsDead && entity.Manager is not null && !entity.IsDead && hits.TryAdd(entity, HitMemory))
            {
                Fire(new EventContext(EventTrigger.Hit | EventTrigger.Collision), entity);
            }
        }
        restingTimer = Velocity.LengthSquared() > 1e-6f ? 1 : restingTimer - dt;
        UpdateStatuses(dt);
        if (!Body.IsRemoved && !world.IsDisposed)
        {
            (lastPosition, lastTurn) = (Body.Position, Body.Orientation);
        }
    }

    protected internal override void OnAdded()
    {
        if (Body.IsRemoved)
        {
            throw new InvalidOperationException("a removed prop cannot stand in the level again; make a new one");
        }
        level = Manager;
    }

    /// <summary>Its body leaves the world, and its effects end (told through the level it stood in: the manager has let go of it).</summary>
    protected internal override void OnRemoved()
    {
        if (Template.Effects.Count > 0)
        {
            level?.RaiseCue(this, new PropEffectsEndCue(this));
        }
        level = null;
        if (!world.IsDisposed && !Body.IsRemoved)
        {
            (lastPosition, lastTurn) = (Body.Position, Body.Orientation);
            world.Remove(Body);
        }
    }

    /// <summary>Its event groups the context meets, acting through the prop's own rules.</summary>
    private DamageResult Fire(in EventContext context, Entity? target) => Events.Fire(context, (e, at) => Run(e, at, target));

    private DamageResult Run(ItemEvent e, Vector3? at, Entity? target)
    {
        var where = at ?? Position;
        switch (e)
        {
            case DamageEvent damage when target is not null:
                return target.Damage(damage.Damage, this, Position);
            case SplashEvent splash when Manager is { } manager:
                return Splash.Hit(manager, this, this, where, splash.Radius, splash.Damage);
            case SoundEvent sound:
                Manager?.RaiseCue(this, new SoundCue(sound.Cue, sound.Banks, where, -Vector3.UnitZ, sound.Magnitude));
                return DamageResult.None;
            case EffectEvent effect:
                Manager?.RaiseCue(this, new WorldEffectCue(effect.Effect, where, Vector3.UnitY));
                return DamageResult.None;
            case CameraShakeEvent shake:
                Manager?.RaiseCue(this, new CameraShakeCue(where, shake.Magnitude, shake.Time));
                return DamageResult.None;
            case RemoveEvent:
                Kill();                                                       // as the original: a removal kills it (its death and pieces too)
                return DamageResult.None;
            default:
                return DamageResult.None;
        }
    }

    /// <summary>Heard during the physics step: an entity touching it is noted, and passes through when it is not solid.</summary>
    private sealed class Contacts(PhysicsProp prop) : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact)
        {
            if (contact.OtherBody?.Tag is not Entity entity)
            {
                return ContactResponse.Collide;                                // the level
            }
            lock (prop.touchedLock)
            {
                prop.touched.Add(entity);
            }
            if (!prop.Template.Solid)
            {
                return ContactResponse.Ignore;
            }
            return prop.Template.Movable && !prop.Template.Pushable && entity is Character ? ContactResponse.PassThrough : ContactResponse.Collide;
        }
    }
}
