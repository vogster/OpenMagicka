using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
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
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Template = template;
        Body = new CharacterBody(world, position, new CharacterShape(template.Radius, template.Length, template.Mass, template.Speed, template.Flies));
        Body.Body.Tag = this;
        Defences = new Defences(template.ResistancesFor(players));
        HitPoints = template.MaxHitPoints;
        IsEthereal = template.IsEthereal;
        this.random = random ?? Random.Shared;
        corpseTimer = 20 + 20 * (float)this.random.NextDouble();
        EquipFrom(template);
        if (animations is not null)
        {
            Animator = new Animation.CharacterAnimator(this, animations, EquippedClasses());
            State = States.CharacterStates.Idle;
            State.Enter(this);
        }
    }

    public CharacterTemplate Template { get; }

    public CharacterBody Body { get; }

    public float HitPoints { get; private set; }

    public float MaxHitPoints => Template.MaxHitPoints;

    public StatusSet Statuses { get; private set; } = StatusSet.Empty;

    /// <summary>Template (scaled for the players), an avatar's slot-1 item (kept by the slots) and resistance buffs.</summary>
    public Defences Defences { get; private set; }

    /// <summary>The resistance buffs on the character now; the template's and the slot-1 item's stay.</summary>
    public void SetResistanceBuffs(IReadOnlyList<Resistance>? buffs) => Defences = Defences with { Buffs = buffs };

    public ISelfShield? SelfShield { get; set; }

    /// <summary>The game's gore setting: overkill by damage needs it.</summary>
    public bool BloodAndGore { get; set; } = true;

    /// <summary>A corpse that stays (for the level script) is never removed.</summary>
    public bool RemoveAfterDeath { get; set; } = true;

    /// <summary>Where the body is; once the body has left the physics world, where it was last.</summary>
    public override Vector3 Position => Body.Body.IsRemoved ? lastPosition : Body.Body.Position;

    public override float Radius => Template.Radius;

    public override Vector3 Velocity => Body.Body.IsRemoved ? Vector3.Zero : Body.Body.Velocity;

    public override bool IsDead => Dead;

    private bool Dead { get; set; }

    public override bool IsRemovable => Dead && corpseTimer < CorpseRemoval && RemoveAfterDeath;

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

    /// <summary>Kills (a kill plane, a script): at once without a state machine, through the death clip with one.</summary>
    public void Kill()
    {
        RemoveInvisibility();
        HitPoints = MathF.Min(HitPoints, 0);
        DieUnlessDying();
    }

    /// <summary>Shatters: no corpse, no revival.</summary>
    public void OverKill()
    {
        RemoveInvisibility();
        if (!IsOverkilled)
        {
            IsOverkilled = true;
            HitPoints = -MaxHitPoints - 1000;
            corpseTimer = -100;
        }
        DieUnlessDying();
    }

    /// <summary>With a state machine, the dead state dies when the death clip ends; without, now.</summary>
    private void DieUnlessDying()
    {
        if (State is null)
        {
            Die();
        }
    }

    internal void Die()
    {
        if (Dead)
        {
            return;
        }
        undieTimer = Template.Undying && !IsOverkilled
            ? (Template.UndieTime < float.Epsilon ? 0.5f : Template.UndieTime)       // as the original: 0.5 s only when none is given
            : float.NaN;
        Dead = true;
        IsKnockedDown = false;
        ReleaseEntanglement();
    }

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
            TurnSpeed = frozen ? 0 : Template.TurnSpeed,
        };
    }

    /// <summary>The character's own frame (§2.5), after physics.</summary>
    public override void Update(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        HitPoints = MathF.Min(HitPoints, MaxHitPoints);
        immortalTime -= dt;
        CountDownActionTimers(dt);
        BlockSlot = -1;                                                       // a block action sets it again within its window
        Animate(dt);
        UpdateLooks(dt);
        Body.SpeedMultiplier = 1;
        SelfShield?.Update(this, dt);
        if (State is not null)
        {
            ChangeState(State.Update(this, dt));
        }
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
        if (HitPoints <= 0 && !Dead)
        {
            DieUnlessDying();
        }
    }

    private void Afterlife(float dt)
    {
        if (!float.IsNaN(undieTimer) && !IsOverkilled)
        {
            undieTimer -= dt;
            if (undieTimer <= 0)
            {
                (Dead, HitPoints) = (false, Template.UndieHitPoints);                // the timer stays spent: the dead state sees a revival
            }
        }
        else
        {
            corpseTimer -= dt;
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
        Body.SpeedMultiplier = MathF.Min(Body.SpeedMultiplier, Statuses.SpeedFactor());
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
