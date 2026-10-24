using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.AI;

/// <summary>What a level's script tells a character to be about (ENTITIES_AI.md §7.2).</summary>
public enum Order { None, Idle, Attack, Defend, Flee, Wander, Panic }

/// <summary>What makes a character take its reaction order: being attacked, or a foe coming near.</summary>
[Flags]
public enum ReactTo { None = 0, Attack = 1, Proximity = 2 }

public enum AlertMode { None, Discover, Danger }

/// <summary>
/// A non-player character's mind (ENTITIES_AI.md §7.2–7.4): its order and reactions, its grudges,
/// the targets it is after and the abilities it means to use, and a stack of AI states that act on
/// them. The level's <see cref="AiManager"/> runs it, a share of the minds each frame.
/// </summary>
public sealed partial class Agent
{
    public const float MaxGrudge = 500, GrudgeDecay = 25;

    private readonly List<(Character Attacker, float Grudge)> grudges = [];
    private readonly List<(Entity Target, float Age)> targets = [];
    private readonly List<(IAiState State, float Age)> states = [];
    private readonly float[] cooldowns;
    private float sinceUpdate;

    public Agent(Character owner, AiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(profile);
        (Owner, Profile) = (owner, profile);
        Abilities = [.. profile.Abilities.Select((template, index) => NpcAbility.For(template, index))];
        cooldowns = new float[Abilities.Count];
        states.Add((AiStates.Idle, 0));
        WanderAngle = (float)owner.NextRandom() * MathF.Tau;
    }

    public Character Owner { get; }

    public AiProfile Profile { get; }

    public IReadOnlyList<NpcAbility> Abilities { get; }

    /// <summary>How far it notices: its template's, unless something (a summoning) set its own.</summary>
    public float AlertRadius
    {
        get => alertRadius ?? Profile.AlertRadius;
        set => alertRadius = value;
    }

    private float? alertRadius;

    public Order Order { get; private set; } = Order.Attack;

    public ReactTo ReactsTo { get; private set; }

    public Order ReactionOrder { get; private set; }

    /// <summary>The script trigger a reaction fires (0 for none); the triggers come with OM-43.</summary>
    public int ReactionTrigger { get; private set; }

    /// <summary>Raised when a reaction or a scripted event fires a trigger (the level's triggers answer, OM-43).</summary>
    public event Action<Agent, int>? Triggered;

    public AlertMode AlertMode { get; internal set; }

    /// <summary>The script's priority target, by its script name (0 for none), looked up each time it is wanted.</summary>
    public int PriorityTargetId { get; private set; }

    public Entity? PriorityTarget => PriorityTargetId == 0 ? null : Owner.Manager?.FindByUniqueId(PriorityTargetId);

    /// <summary>The ability the script favours against the priority target (-1 for none).</summary>
    public int PriorityAbility { get; private set; } = -1;

    public float WanderAngle { get; internal set; }

    /// <summary>The target on top of the stack.</summary>
    public Entity? CurrentTarget => targets.Count > 0 ? targets[^1].Target : null;

    /// <summary>How long the current target has been the target.</summary>
    public float TargetAge => targets.Count > 0 ? targets[^1].Age : 0;

    public NpcAbility? NextAbility { get; internal set; }

    /// <summary>The ability under way (while its clip plays).</summary>
    public NpcAbility? BusyAbility { get; internal set; }

    public IAiState State => states[^1].State;

    /// <summary>How long the top state has been on top.</summary>
    public float StateAge => states[^1].Age;

    public float Cooldown(int ability) => cooldowns[ability];

    internal void StartCooldown(NpcAbility ability) => cooldowns[ability.Index] = ability.Template.Cooldown;

    /// <summary>The grudge it bears an attacker (0 for none).</summary>
    public float GrudgeAgainst(Entity attacker)
    {
        foreach (var (who, grudge) in grudges)
        {
            if (ReferenceEquals(who, attacker))
            {
                return grudge;
            }
        }
        return 0;
    }

    public IEnumerable<(Character Attacker, float Grudge)> Grudges => grudges;

    /// <summary>The wait before the next element is chanted (1 at first, as the original).</summary>
    public float ChantCooldown { get; internal set; } = 1;

    private float blockTimer;

    /// <summary>Raises its guard for a second.</summary>
    internal void Block() => (blockTimer, Owner.IsBlocking) = (1, true);

    /// <summary>An ability is over: its clip ended or another state took over.</summary>
    internal void Done(NpcAbility? only = null)
    {
        if (only is null || ReferenceEquals(BusyAbility, only))
        {
            BusyAbility = null;
        }
    }

    /// <summary>Time passes for the mind each frame; it is spent when the mind next thinks.</summary>
    internal void Pass(float dt)
    {
        sinceUpdate += dt;
        ChantCooldown -= dt;
        blockTimer -= dt;
        if (blockTimer <= 0 && Owner.IsBlocking)
        {
            Owner.IsBlocking = false;
        }
        for (var i = 0; i < cooldowns.Length; i++)
        {
            cooldowns[i] -= dt;                                                // as the original: the abilities cool every frame
        }
    }

    /// <summary>
    /// Thinks (§7.3): grudges fade, the states, targets and leader age, it reacts to an attacker or
    /// a foe near, the top state acts, and the next ability readies.
    /// </summary>
    internal void Think()
    {
        if (Owner.IsDead || Owner.IsPlayer)
        {
            sinceUpdate = 0;
            return;
        }
        var dt = sinceUpdate;
        Fade(dt);
        LeaderAge += dt;
        for (var i = 0; i < targets.Count; i++)
        {
            targets[i] = (targets[i].Target, targets[i].Age + dt);
        }
        for (var i = 0; i < states.Count; i++)
        {
            states[i] = (states[i].State, states[i].Age + dt);
        }
        if (Provoked())
        {
            React();
        }
        State.Execute(this, dt);
        NextAbility?.Update(this, dt);
        sinceUpdate = 0;
    }

    public float LeaderAge { get; internal set; }

    /// <summary>
    /// Whether it reacts: an attacker it bears a grudge (when it reacts to attacks), else a foe of
    /// none of its factions within the alert radius that it can see (when it reacts to nearness).
    /// </summary>
    private bool Provoked()
    {
        if ((ReactsTo & ReactTo.Attack) != 0 && grudges.Any(g => g.Grudge > 0))
        {
            return true;
        }
        if ((ReactsTo & ReactTo.Proximity) == 0 || Owner.Manager is not { } manager)
        {
            return false;
        }
        var seen = false;
        foreach (var other in manager.Query(Owner.Position, AlertRadius).OfType<Character>())
        {
            if ((Owner.Factions & other.Factions) == 0 && (!other.IsInvisible || Owner.Template.CanSeeInvisible))
            {
                seen = true;
                Alarm();
            }
        }
        return seen;
    }

    /// <summary>
    /// Takes the reaction order (when there is one) and fires the reaction trigger. As the original, it
    /// does so each time it thinks while provoked (a trigger's own repeat rules keep it to once).
    /// </summary>
    internal void React()
    {
        if (ReactionOrder != Order.None)
        {
            Order = ReactionOrder;
        }
        if (ReactionTrigger != 0)
        {
            Triggered?.Invoke(this, ReactionTrigger);
        }
    }

    /// <summary>A foe was seen: the alert turns to danger when the reaction will change the order.</summary>
    internal void Alarm()
    {
        if (ReactionOrder != Order.None && ReactionOrder != Order)
        {
            AlertMode = AlertMode.Danger;
        }
    }

    /// <summary>
    /// The script's order (§7.10). A new order drops every state down to idle (letting go of the
    /// target of an attack); the events start from the first.
    /// </summary>
    public void SetOrder(Order order, ReactTo reactTo = ReactTo.None, Order reaction = Order.None, int priorityTarget = 0,
        int priorityAbility = -1, int reactionTrigger = 0, IReadOnlyList<AiEvent>? events = null)
    {
        if (Order != order)
        {
            while (State != AiStates.Idle)
            {
                if (State == AiStates.Attack)
                {
                    ReleaseTarget();
                }
                PopState();
            }
        }
        (Order, ReactsTo, ReactionOrder, ReactionTrigger) = (order, reactTo, reaction, reactionTrigger);
        (Events, EventIndex) = (events is null ? null : [.. events], 0);
        if (events is not null)
        {
            EventDelay = 0;                                                    // as the original: kept otherwise
        }
        (PriorityTargetId, PriorityAbility) = (priorityTarget, priorityAbility);
    }

    /// <summary>The order's scripted events (its own copy: a reversing loop turns it around).</summary>
    public AiEvent[]? Events { get; private set; }

    public int EventIndex { get; internal set; }

    public float EventDelay { get; internal set; }

    public void PushState(IAiState state)
    {
        if (state == State)
        {
            return;
        }
        State.Exit(this);
        states.Add((state, 0));
        state.Enter(this);
    }

    public void PopState()
    {
        if (states.Count == 1)
        {
            return;                                                            // idle stays at the bottom
        }
        State.Exit(this);
        states.RemoveAt(states.Count - 1);
        State.Enter(this);
    }

    public void AddTarget(Entity target, NpcAbility? ability)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(target, CurrentTarget))
        {
            targets.Add((target, 0));
            NextAbility = ability;
        }
    }

    public void ChangeTarget(Entity? target, NpcAbility? ability)
    {
        if (target is null)
        {
            return;
        }
        if (targets.Count == 0)
        {
            AddTarget(target, ability);
            return;
        }
        targets[^1] = (target, 0);
        NextAbility = ability;
    }

    public void ReleaseTarget()
    {
        if (targets.Count > 0)
        {
            targets.RemoveAt(targets.Count - 1);
        }
        NextAbility = null;
    }
}
