using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A barrier of a Shield spell (docs/gameplay/SPELLS.md §5.6): a solid stone or ice pillar (with
/// Earth or Ice) that stands in the way, or an elemental wall; either strikes what touches it, wears
/// away and grows the next barrier of its chain. A simulation object.
/// </summary>
public sealed partial class Barrier : Entity
{
    /// <summary>A barrier stands this far over its ground, beyond its radius.</summary>
    public const float Lift = 0.05f;

    /// <summary>The next barrier of the chain comes this long after.</summary>
    public const float ChainDelay = ShieldRules.BarrierChainInterval;

    /// <summary>It loses 10 hit points every 0.2 s.</summary>
    public const float DecayStep = 0.2f, DecayAmount = 10;

    /// <summary>A solid barrier sinks back for this long (its rising clip, 0.5 s at 2×) before it goes.</summary>
    public const float SinkTime = 0.25f;

    /// <summary>A barrier's bolt reaches this far on from whom it strikes.</summary>
    public const float BoltRange = 1;

    /// <summary>A solid barrier with Arcane or Life blasts this far as it breaks.</summary>
    public const float BlastRadius = 5;

    private readonly PhysicsWorld world;
    private readonly Vector3 ground;
    private readonly ChainStep step;
    private StaticMesh? mesh;
    private float chainTimer = ChainDelay;
    private bool chained;
    private float decayTimer;
    private float effectTime;
    private float iceTime;
    private float sinkTimer;
    private bool broken;

    private Barrier(PhysicsWorld world, Character? owner, Vector3 ground, Vector3 facing, float scale, Spell spell, Barriers rules,
        IReadOnlyList<DamageEntry> damage, BarrierHits hits, ChainStep received)
    {
        this.world = world;
        (Owner, this.ground, Facing, Scale, Spell, ContactDamage, Hits) = (owner, ground, facing, scale, spell, damage, hits);
        (IsSolid, HitPoints, Armour) = (rules.Solid, rules.Hp, rules.Armor);
        MaxHitPoints = HitPoints;
        BarrierRadius = rules.SegmentRadius * scale;
        step = received.Spent();
        (effectTime, iceTime) = EffectTimes(spell);
    }

    public Character? Owner { get; }

    public Spell Spell { get; }

    public Vector3 Facing { get; }

    /// <summary>The ground point it stands on.</summary>
    public Vector3 Ground => ground;

    public float Scale { get; }

    /// <summary>Earth or Ice: it stands in the way; otherwise an elemental wall.</summary>
    public bool IsSolid { get; }

    public float HitPoints { get; private set; }

    public float MaxHitPoints { get; }

    /// <summary>Taken off each blow (Ice).</summary>
    public int Armour { get; }

    /// <summary>What a touch deals.</summary>
    public IReadOnlyList<DamageEntry> ContactDamage { get; }

    /// <summary>What it deals, all its magnitudes together.</summary>
    public override float Danger => ContactDamage.Sum(d => d.Magnitude);

    /// <summary>Its cast's shared memory of whom it struck.</summary>
    public BarrierHits Hits { get; }

    public float BarrierRadius { get; }

    /// <summary>Whether a touch strikes now: always for an elemental wall, while its effect lasts for a solid one.</summary>
    public bool IsStriking => !IsSolid || effectTime > 0;

    public override Vector3 Position => ground + Vector3.UnitY * (BarrierRadius + Lift);

    public override float Radius => BarrierRadius;

    public override bool IsDamageable => true;

    public override bool IsDead => HitPoints <= 0;

    public override bool IsRemovable => broken && sinkTimer <= 0;

    /// <summary>Ends it (a new barrier or mine on its spot).</summary>
    public void Kill() => HitPoints = MathF.Min(HitPoints, 0);

    /// <summary>
    /// Raises a barrier on the ground: barriers of other casts on its spot break and mines there go
    /// off; then it strikes (a solid one, with its rising blow) everyone near but its owner once.
    /// </summary>
    public static Barrier Raise(EntityManager manager, PhysicsWorld world, Character? owner, Vector3 ground, Vector3 facing, float scale, Spell spell,
        IReadOnlyList<DamageEntry> damage, BarrierHits hits, ChainStep received)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(hits);
        if (ShieldRules.For(spell, CastType.Force) is not Barriers rules)
        {
            throw new ArgumentException("not a barrier spell", nameof(spell));
        }
        var radius = rules.SegmentRadius * scale;
        ShieldChain.Clear(manager, ground, radius * ShieldRules.BarrierClearFactor, hits);
        var barrier = new Barrier(world, owner, ground, facing, scale, spell, rules, damage, hits, received);
        manager.Add(barrier);
        var rising = rules.Solid ? SpellDamage.For(spell, SpellType.Projectile, CastType.Area) : [];
        foreach (var entity in manager.Query(ground, radius * ShieldRules.BarrierSpawnHitFactor))
        {
            if (entity.IsDamageable && !ReferenceEquals(entity, barrier) && !ReferenceEquals(entity, owner) && !hits.Contains(entity)
                && !(entity is Barrier other && ReferenceEquals(other.Hits, hits)))
            {
                entity.Damage(rising, owner, ground);
                hits.Add(entity);
            }
        }
        return barrier;
    }

    public override void Update(float dt)
    {
        if (IsDead)
        {
            if (broken)
            {
                sinkTimer -= dt;
            }
            else
            {
                Break();                                                      // killed or struck down since its last frame
            }
            return;
        }
        effectTime -= dt;
        iceTime -= dt;
        if (iceTime < 0 && (Spell.Element & ElementSets.Physical) == Elements.Ice)
        {
            HitPoints = 0;                                                    // ice spikes melt away
        }
        Chain(dt);
        Hits.Tick(this, dt);
        decayTimer -= dt;
        if (decayTimer < 0)
        {
            HitPoints -= DecayAmount;
            decayTimer += DecayStep;
        }
        if (!IsDead && IsStriking)
        {
            StrikeTouching();
        }
        if (IsDead)
        {
            Break();
        }
    }

    /// <summary>
    /// Strikes what touches it, once per <see cref="BarrierHits.Memory"/>: with Lightning a bolt that
    /// chains on <see cref="BoltRange"/>, else its contact damage.
    /// </summary>
    private void StrikeTouching()
    {
        if (Manager is not { } manager)
        {
            return;
        }
        var reach = IsSolid ? 2 * BarrierRadius : BarrierRadius;
        var touching = manager.Query(Position - Vector3.UnitY * BarrierRadius, reach).Union(manager.Query(Position, BarrierRadius));
        foreach (var entity in touching.ToList())
        {
            if (!entity.IsDamageable || entity is Barrier || entity is Character { IsEthereal: true } || Hits.Contains(entity))
            {
                continue;
            }
            if ((Spell.Element & Elements.Lightning) != 0)
            {
                var from = Position + Vector3.UnitY * (IsSolid ? BarrierRadius : 1);
                LightningBolts.StrikeAt(manager, Owner, this, from, entity, BoltRange, ContactDamage, Hits);
                continue;
            }
            entity.Damage(ContactDamage, Owner, Position);
            Hits.Add(entity);
        }
    }

    private void Chain(float dt)
    {
        if (chained || !step.Continues)
        {
            return;
        }
        chainTimer -= dt;
        if (chainTimer >= 0)
        {
            return;
        }
        chained = true;
        if (Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity
            || ShieldChain.NextGround(manager, world, Position, ground + step.Next + Vector3.UnitY * ShieldChain.ProbeUp) is not { } next)
        {
            return;
        }
        ShieldChain.Clear(manager, next, 1.5f * BarrierRadius, Hits);
        Raise(manager, world, Owner, next, Vector3.Transform(Facing, step.Turn), Scale, Spell, ContactDamage, Hits, step);
    }

    /// <summary>A solid barrier's sound as it breaks: its ice and earth's, its ice's, or its earth's.</summary>
    public static string? BreakSound(Spell spell) => (spell.Ice > 0, spell.Earth > 0) switch
    {
        (true, true) => "spell_ice_earth_barrier_break",
        (true, false) => "spell_ice_barrier_break",
        (false, true) => "spell_earth_barrier_break",
        _ => null,
    };

    /// <summary>A solid barrier with Arcane or Life blasts as it breaks and goes at once; another solid one sinks first; a wall goes at once.</summary>
    private void Break()
    {
        broken = true;
        Hits.Leave(this);                                                     // the next barrier standing keeps the hit list's time
        if (!IsSolid)
        {
            return;
        }
        if ((Spell.Element & ElementSets.Beams) != 0)
        {
            if (Manager is { } manager && !world.IsDisposed)
            {
                foreach (var cue in SpellSounds.Blast(Spell))
                {
                    manager.RaiseCue(this, new Animation.SoundCue(cue, Animation.SoundBank.Spells, Position, Vector3.UnitZ));
                }
                Blasts.Full(manager, world, Owner, this, BlastRadius, Position, ContactDamage, source: this);
            }
            return;
        }
        if (BreakSound(Spell) is { } breaking)
        {
            Manager?.RaiseCue(this, new Animation.SoundCue(breaking, Animation.SoundBank.Spells, Position, Vector3.UnitZ));
        }
        sinkTimer = SinkTime;
    }

    /// <summary>A solid barrier stands in the world as a ball characters and missiles meet.</summary>
    protected internal override void OnAdded()
    {
        Hits.Join(this);
        if (IsSolid && !world.IsDisposed && !world.IsStepping)
        {
            var (vertices, triangles) = Shapes.Ball(Position, BarrierRadius);
            mesh = world.AddStaticMesh(0, vertices, triangles, isLevel: false);
            mesh.Owner = this;
        }
    }

    protected internal override void OnRemoved()
    {
        Hits.Leave(this);
        if (mesh is not null && !world.IsDisposed && !world.IsStepping)
        {
            world.RemoveStaticMesh(mesh);
        }
        mesh = null;
    }
}
