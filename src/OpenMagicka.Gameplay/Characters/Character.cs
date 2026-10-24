using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Physics;
using OpenMagicka.Physics.Characters;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>
/// A character's core (docs/engine/ENTITIES_AI.md §2): hit points, statuses, resistances, the
/// damage path and the per-frame timers, around a physics body. Animation, the state machine,
/// spells and items come on top (OM-33 and later). A simulation object: it changes in place.
/// </summary>
public sealed partial class Character : Entity
{
    /// <summary>How long a lightning hit slows the character.</summary>
    public const float ZapTime = 0.1f;

    /// <summary>The last attacker is forgotten this long after the last hit, while no status is active.</summary>
    public const float AttackerMemory = 8;

    /// <summary>A corpse can go once its timer is below this.</summary>
    public const float CorpseRemoval = -10;

    public const float MaxEntanglement = 20;

    private readonly PhysicsWorld world;
    private float immortalTime;
    private float zapTimer = -1;
    private float stunTimer;
    private float undieTimer = float.NaN;
    private float corpseTimer;
    private float lastAttackerTimer;
    private readonly Random random;
    private Vector3 lastPosition;

    /// <param name="players">The players in the game, for an enemy's resistances.</param>
    /// <param name="random">Picks the corpse time (20 to 40 s) and the death clip.</param>
    /// <param name="animations">The template's animations; without them there is no state machine and death is immediate.</param>
    public Character(CharacterTemplate template, PhysicsWorld world, Vector3 position, int players = 1,
        Random? random = null, Animation.CharacterAnimations? animations = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        skeleton = animations?.Skeleton;
        if (skeleton is not null && template.Equipment?.FirstOrDefault(e => skeleton.IndexOf(e.Bone) < 0) is { } loose)
        {
            throw new ArgumentException($"{template.Id} has no bone {loose.Bone} to hold an item", nameof(template));   // before the body is in the world
        }
        (poses, placed, skinned) = skeleton is null ? ([], [], []) : (new BonePose[skeleton.Bones.Count], new Matrix4x4[skeleton.Bones.Count], new Matrix4x4[skeleton.Bones.Count]);
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Template = template;
        Body = new CharacterBody(world, position, new CharacterShape(template.Radius, template.Length, template.Mass, template.Speed, template.Flies));
        Body.Body.Tag = this;
        Defences = new Defences(template.ResistancesFor(players));
        (HitPoints, MaxHitPoints, undying) = (template.MaxHitPoints, template.MaxHitPoints, template.Undying);
        IsEthereal = template.IsEthereal;
        this.random = random ?? Random.Shared;
        corpseTimer = 20 + 20 * (float)this.random.NextDouble();
        EquipFrom(template);
        Mind = AI.AiManager.For(this);
        foreach (var aura in template.Auras ?? [])
        {
            AddAura(aura);
        }
        if (animations is not null)
        {
            Animator = new Animation.CharacterAnimator(this, animations, EquippedClasses());
            State = States.CharacterStates.Idle;
            State.Enter(this);
        }
        root = RootMatrix();
        UpdatePose();
        PlaceItems();                                                         // bones and items have a place before the first frame
    }

    public CharacterTemplate Template { get; }

    public CharacterBody Body { get; }

    public float HitPoints { get; private set; }

    /// <summary>The template's, changed for a frame by hit point buffs.</summary>
    public float MaxHitPoints { get; private set; }

    public StatusSet Statuses { get; private set; } = StatusSet.Empty;

    /// <summary>Template (scaled for the players), an avatar's slot-1 item and the resistance buffs (each kept by its source).</summary>
    public Defences Defences { get; private set; }

    public ISelfShield? SelfShield { get; set; }

    /// <summary>The game's gore setting: overkill by damage needs it.</summary>
    public bool BloodAndGore { get; set; } = true;

    /// <summary>A corpse that stays (for the level script) is never removed.</summary>
    public bool RemoveAfterDeath { get; set; } = true;

    /// <summary>Where the body is; once the body has left the physics world, where it was last.</summary>
    public override Vector3 Position => Body.Body.IsRemoved ? lastPosition : Body.Body.Position;

    public override float Radius => Template.Radius;

    public override Vector3 Velocity => Body.Body.IsRemoved ? Vector3.Zero : Body.Body.Velocity;

    /// <summary>The physics world the body is in (a melee sweep tests the level in it).</summary>
    internal PhysicsWorld World => world;

    /// <summary>
    /// Within its radius of the capsule's axis, which runs up from half its length below the centre;
    /// never when ethereal. The hit is on the capsule, towards the segment's nearest point.
    /// </summary>
    public override bool SegmentIntersect(Segment segment, float radius, out Vector3 hit)
    {
        if (!(radius >= 0) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "must not be negative");
        }
        hit = default;
        if (IsEthereal)
        {
            return false;
        }
        var axis = Axis;
        var reach = radius + Body.Shape.Radius;
        if (Segment.DistanceSquared(axis, segment, out var s, out var t) > reach * reach)
        {
            return false;
        }
        var towards = segment.At(t) - axis.At(s);
        hit = axis.At(s) + (towards.LengthSquared() > 1e-12f ? Vector3.Normalize(towards) * Body.Shape.Radius : Vector3.Zero);
        return true;
    }

    /// <summary>The capsule's axis, up from half its length below the centre.</summary>
    private Segment Axis => new(Position - new Vector3(0, Body.Shape.Length / 2, 0), new Vector3(0, Body.Shape.Length, 0));

    /// <summary>
    /// The capsule's outward direction where the segment enters it, or from the centre towards
    /// <paramref name="fallback"/> when it does not (a deflected shot).
    /// </summary>
    internal Vector3 EntryNormal(Segment segment, Vector3 fallback)
    {
        if (CapsuleEntry(segment) is not { } inside)
        {
            return fallback - Position;
        }
        var entry = segment.At(inside);
        Segment.DistanceSquared(Axis, new Segment(entry, Vector3.Zero), out var s, out _);
        return entry - Axis.At(s);
    }

    /// <summary>Where along the segment it enters the capsule (never when ethereal).</summary>
    public override float? Entry(Segment segment) => IsEthereal ? null : CapsuleEntry(segment);

    private float? CapsuleEntry(Segment segment)
    {
        var radius = Body.Shape.Radius;
        Segment.DistanceSquared(Axis, segment, out _, out var closest);
        if (Distance(segment.At(closest)) > radius)
        {
            return null;
        }
        var (outside, inside) = (0f, closest);                                 // the distance falls along the segment up to its closest point
        if (Distance(segment.At(0)) <= radius)
        {
            inside = 0;
        }
        for (var i = 0; i < 32 && inside > 0; i++)
        {
            var middle = (outside + inside) / 2;
            (outside, inside) = Distance(segment.At(middle)) > radius ? (middle, inside) : (outside, middle);
        }
        return inside;

        float Distance(Vector3 point) => MathF.Sqrt(Segment.DistanceSquared(Axis, new Segment(point, Vector3.Zero), out _, out _));
    }

    public override bool IsDead => Dead;

    /// <summary>Its mind (an NPC whose template has one); a wizard's goes unused.</summary>
    public AI.Agent? Mind { get; private set; }

    /// <summary>The elements it holds ready; a wizard charging a spell, 10.</summary>
    public override float Danger => IsPlayer && State == States.CharacterStates.Charge ? 10 : SpellQueue.Count;

    private bool Dead { get; set; }

    public override bool IsRemovable => (Dead && corpseTimer < CorpseRemoval && RemoveAfterDeath) || (Dead && discarded);

    public override bool IsDamageable => true;

    public bool IsOverkilled { get; private set; }

    public bool IsImmortal => immortalTime > 0;

    public bool IsEthereal { get; set; }

    public bool IsStunned => stunTimer > 0 && !Dead;

    public bool IsZapped => zapTimer >= 0;

    public bool IsKnockedDown { get; private set; }

    /// <summary>Set by the last hit's Hit result; the state machine plays the hit reaction and clears it.</summary>
    public bool IsHit { get; set; }

    /// <summary>A freeze stopped the cast; the spell system reads and clears it.</summary>
    public bool CastInterrupted { get; set; }

    public float Entanglement { get; private set; }

    public bool IsEntangled => Entanglement > float.Epsilon;

    public bool CanBeKnockedDown => Template.CanBeKnockedDown;

    public Entity? LastAttacker { get; private set; }

    /// <summary>Immune to damage for this long.</summary>
    public void MakeImmortal(float seconds) => immortalTime = MathF.Max(immortalTime, Finite(seconds, nameof(seconds)));

    /// <summary>Falls over regardless of the tolerance (when the character can fall at all, and lives).</summary>
    public void KnockDown()
    {
        if (CanBeKnockedDown && !Dead)
        {
            IsKnockedDown = true;
            Body.Body.Velocity = Body.Body.Velocity with { X = 0, Z = 0 };
        }
    }

    /// <summary>Falls over if the magnitude reaches the template's tolerance.</summary>
    public void KnockDown(float magnitude)
    {
        if (Finite(magnitude, nameof(magnitude)) >= Template.KnockdownTolerance)
        {
            KnockDown();
        }
    }

    /// <summary>The knockdown trigger is spent (the knockdown state's call).</summary>
    public void StandUp() => IsKnockedDown = false;

    /// <summary>Vines hold the character; not the immortal, not the burning.</summary>
    public void Entangle(float magnitude)
    {
        Finite(magnitude, nameof(magnitude));
        if (!IsImmortal && !Statuses.Has(StatusEffects.Burning))
        {
            Entanglement = MathF.Min(Entanglement + MathF.Max(0, magnitude), MaxEntanglement);
        }
    }

    /// <summary>Damage weakens the vines; fire, or more than is left, breaks them.</summary>
    public void DecreaseEntanglement(float magnitude, Spells.Elements element)
    {
        Finite(magnitude, nameof(magnitude));
        if (Entanglement <= 0)
        {
            return;
        }
        var left = Entanglement - magnitude;
        Entanglement = (element & Spells.Elements.Fire) != 0 || left < 0 ? 0 : left;
    }

    public void ReleaseEntanglement() => Entanglement = 0;

    /// <summary>
    /// The body's conditions for the coming physics step, from the character's state; what others set
    /// on the body (grease underfoot, levitation, the nav-mesh height for flyers) is kept.
    /// </summary>
    public void PrepareStep()
    {
        var frozen = Statuses.Has(StatusEffects.Frozen);
        Body.Conditions = Body.Conditions with
        {
            IsAlive = !Dead,
            IsPlayer = IsPlayer,
            IsEntangled = IsEntangled,
            IsKnockedDown = IsKnockedDown,
            CanBeKnockedDown = CanBeKnockedDown,
            IsIntangible = IsIntangible,
            IsLevitating = IsLevitating,
            TurnSpeed = frozen ? 0 : Template.TurnSpeed * castTurn,
            FlyGround = Body.Shape.Flies && Manager?.NavMesh?.Nearest(Position) is { } below ? below.Point.Y : Body.Conditions.FlyGround,
        };
    }

    /// <summary>The character's own frame (§2.5), after physics.</summary>
    public override void Update(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        RunAuras(dt);
        RunBuffs(dt);
        HitPoints = MathF.Min(HitPoints, MaxHitPoints);
        root = RootMatrix();
        immortalTime -= dt;
        CountDownActionTimers(dt);
        BlockSlot = -1;                                                       // a block action sets it again within its window
        Animate(dt);
        UpdateLooks(dt);
        Body.SpeedMultiplier = 1;
        SelfShield?.Update(this, dt);
        SyncShieldMass();
        UpdateMagicks(dt);
        UpdateMind(dt);
        RunScriptedEvents(dt);
        CountDownMeleeBoost(dt);
        if (State is not null)
        {
            ChangeState(State.Update(this, dt));
        }
        UpdateCasting(dt);
        if (zapTimer >= 0)
        {
            if (float.IsFinite(Template.ZapModifier))
            {
                Body.SpeedMultiplier = Template.ZapModifier;
                AnimationSpeed *= Template.ZapModifier;
            }
            zapTimer -= dt;
        }
        stunTimer = MathF.Max(0, stunTimer - dt);
        if (Dead)
        {
            Afterlife(dt);
        }
        else
        {
            Body.SpeedMultiplier = Passives.Speed(this, Body.SpeedMultiplier);    // as the original's held items, before the statuses
        }
        HitPoints = DamageRules.Regenerate(HitPoints, MaxHitPoints, Template.Regeneration, dt, Statuses);
        TickStatuses(dt);
        ScaleAnimation();
        if (Statuses.Has(StatusEffects.Frozen))
        {
            Body.SpeedMultiplier = 0;
            Body.Movement = Vector3.Zero;
            AnimationSpeed = 0;
        }
        if (LastAttacker is not null && !ReferenceEquals(LastAttacker.Manager, Manager))
        {
            LastAttacker = null;                                              // it left the level
        }
        if (Target is not null && !ReferenceEquals(Target.Manager, Manager))
        {
            Target = null;
        }
        if (LastAttacker is not null && Statuses.Active == StatusEffects.None)
        {
            lastAttackerTimer -= dt;
            LastAttacker = lastAttackerTimer < 0 ? null : LastAttacker;
        }
        UpdateBloat(dt);
        if (HitPoints <= 0 && !Dead && !IsBloating)
        {
            DieUnlessDying();
        }
        foreach (var slot in equipment)
        {
            if (slot.Item is { IsAttached: true } item)
            {
                item.Tick(dt);                                                // a released item is left as it was
            }
        }
        PlaceItems();
        if (!Dead)
        {
            RunItemAuras(dt);                                                 // as the original: held items do nothing for a corpse
            RunPassives(dt);
            FireGuns(dt);
        }
    }

    private void TickStatuses(float dt)
    {
        if (Dead)
        {
            Statuses = Statuses.Cleared();
            return;
        }
        var tick = Statuses.Update(dt, HitPoints, MaxHitPoints);
        Statuses = tick.Set;
        foreach (var damage in tick.Damage)
        {
            DamageOverTime(damage.Amount, damage.Element);
        }
        UpdatePanic(dt);
        if (Statuses.SpeedCap() is { } cap)
        {
            Body.SpeedMultiplier = MathF.Min(Body.SpeedMultiplier, cap);
        }
    }

    /// <summary>A character's body is spent once it leaves: a removed character cannot come back.</summary>
    protected internal override void OnAdded()
    {
        if (Body.Body.IsRemoved)
        {
            throw new InvalidOperationException("a removed character cannot be added again; spawn a new one");
        }
    }

    /// <summary>The body leaves the physics world with the character (nothing to do once that world is gone).</summary>
    protected internal override void OnRemoved()
    {
        StopSpell();                                                          // its spell ends with it
        if (world.IsDisposed || Body.Body.IsRemoved)
        {
            return;
        }
        lastPosition = Body.Body.Position;
        world.Remove(Body.Body);
    }

    private static float Finite(float value, string name) =>
        float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must be finite");
}
