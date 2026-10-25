using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A mine of a Shield spell with Arcane or Life (docs/gameplay/SPELLS.md §5.6): it arms, goes off
/// half a second after something steps on it, and grows the next mine of its chain. A simulation object.
/// </summary>
public sealed class SpellMine : Entity
{
    /// <summary>It stands this far over its ground and is met within this of its centre.</summary>
    public const float Lift = 0.1f, TouchRadius = 0.9f, MineRadius = 1;

    /// <summary>Its rising clip (2.167 s played at 3×) arms it.</summary>
    public const float ArmTime = 2.167f / 3;

    /// <summary>A touch lights a fuse this long; a blow, a shorter one.</summary>
    public const float Fuse = 0.5f, DamageFuse = 0.1f;

    public const float BlastRadius = 3;

    /// <summary>It goes off when its owner is farther than this.</summary>
    public const float OwnerRange = 30;

    /// <summary>The next mine of the chain comes this long after.</summary>
    public const float ChainDelay = ShieldRules.MineChainInterval;

    private readonly PhysicsWorld world;
    private readonly Vector3 ground;
    private readonly ChainStep step;
    private float chainTimer = ChainDelay;
    private bool chained;
    private float armTimer = ArmTime;
    private float fuse;
    private bool dead;

    public SpellMine(PhysicsWorld world, Character? owner, Vector3 ground, Vector3 facing, Spell spell, ChainStep received)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        (Owner, this.ground, Facing, Spell) = (owner, ground, facing, spell);
        step = received.Spent();
        BlastDamage = SpellDamage.For(spell, SpellType.Shield, CastType.Force);
    }

    public Character? Owner { get; }

    public Spell Spell { get; }

    public Vector3 Facing { get; }

    /// <summary>What its blast deals.</summary>
    public IReadOnlyList<DamageEntry> BlastDamage { get; }

    public override Vector3 Position => ground + Vector3.UnitY * Lift;

    public override float Radius => MineRadius;

    public override bool IsDamageable => true;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    public bool IsArmed => armTimer <= 0;

    /// <summary>Whether its fuse burns.</summary>
    public bool IsLit => fuse > 0;

    /// <summary>A blow that carries Damage or Status, of any strength, lights a short fuse.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if (dead || damage.Amount * damage.Magnitude == 0 || (damage.Attack & (AttackProperties.Damage | AttackProperties.Status)) == 0)
        {
            return DamageResult.None;
        }
        fuse = DamageFuse;
        return DamageResult.Damaged;
    }

    /// <summary>Goes without a blast (Nullify).</summary>
    public void Kill() => dead = true;

    /// <summary>Whether it went off (not merely went).</summary>
    public bool HasGoneOff { get; private set; }

    /// <summary>Goes off: a full blast around it, sparing itself.</summary>
    public void Detonate()
    {
        if (dead)
        {
            return;
        }
        (dead, HasGoneOff) = (true, true);
        if (Manager is { } manager && !world.IsDisposed)
        {
            Blasts.Full(manager, world, Owner, this, BlastRadius, Position, BlastDamage, source: this);
        }
    }

    /// <summary>A mine about to go off is to be fled at all costs.</summary>
    public override float Danger => fuse > 0 ? 10000 : 1;

    public override void Update(float dt)
    {
        if (dead)
        {
            return;
        }
        Chain(dt);
        armTimer -= dt;
        if (IsArmed && fuse <= 0 && Touched())
        {
            fuse = Fuse;
        }
        if (fuse > 0)
        {
            fuse -= dt;
            if (fuse <= 0)
            {
                Detonate();
                return;
            }
        }
        if (Owner is not null && Vector3.DistanceSquared(Owner.Position, Position) > OwnerRange * OwnerRange)
        {
            Detonate();
        }
    }

    /// <summary>
    /// Whether a character (not ethereal nor levitating) or a missile touches it. Its owner too: as
    /// the original, the owner's hold lasts only until the mine is armed, so it never holds.
    /// </summary>
    private bool Touched() =>
        Manager is { } manager && manager.Query(Position, TouchRadius).Any(e =>
            e is Missile || e is Character { IsEthereal: false, Body.Conditions.IsLevitating: false });

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
            || ShieldChain.NextGround(manager, world, Position, Position + step.Next + Vector3.UnitY * ShieldChain.ProbeUp) is not { } next)
        {
            return;
        }
        Lay(manager, world, Owner, next, Vector3.Transform(Facing, step.Turn), Spell, step);
    }

    /// <summary>Lays a mine on the ground, clearing its spot first.</summary>
    public static SpellMine Lay(EntityManager manager, PhysicsWorld world, Character? owner, Vector3 ground, Vector3 facing, Spell spell, ChainStep received)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ShieldChain.Clear(manager, ground, MineRadius, hits: null);
        var mine = new SpellMine(world, owner, ground, facing, spell, received);
        manager.Add(mine);
        return mine;
    }
}
