using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Physics;
using Shapes = OpenMagicka.Gameplay.Spells.Effects.Shapes;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Wave: six rock pillars burst up one after another in a line ahead, tossing up whoever stands on
/// them, each once per cast (MAGICKS.md §4.10). With no floor ahead it does nothing.
/// </summary>
public sealed class Wave() : Magick(MagickType.Wave, SweepClip)
{
    /// <summary>The pillars stand this far apart, and the chain's range spends this much on each (six in all).</summary>
    public const float Step = 1.5f, Spacing = 2.5f, Range = 15;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity)
        {
            return true;
        }
        var facing = caster.Body.Facing;
        var probe = caster.Position + facing * caster.Radius + Vector3.UnitY * 2;
        if (ShieldChain.Ground(caster.World, probe, ShieldChain.FirstProbeDown) is not { } ground)
        {
            return true;                                                      // as the original: cast, but no pillars
        }
        var tossed = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        RockPillar.Raise(manager, caster.World, caster, ground + facing * Step * 2, facing, tossed, new ChainStep(facing * Step, Quaternion.Identity, Range, Spacing));
        return true;
    }
}

/// <summary>
/// A rock pillar of a wave: a solid ball (radius 1.25) for its rising clip's length, then gone; the
/// next rises 0.1 s after it. Nothing breaks it. A simulation object.
/// </summary>
public sealed class RockPillar : Entity
{
    public const float PillarRadius = 1.25f, Lift = 0.05f, ChainDelay = 0.1f, Toss = 10, HeavyMass = 3000;

    /// <summary>Its rising clip, <c>rockpillar_animation</c> (0.833 s; the timer runs in real time, not at the clip's 2×).</summary>
    public const float Lifetime = 0.833f;

    /// <summary>A touch: Earth, a knockback of nothing (the toss is the blow).</summary>
    public static readonly IReadOnlyList<DamageEntry> Touch = [new(AttackProperties.Knockback | AttackProperties.Damage, Elements.Earth, 0, 1)];

    private readonly PhysicsWorld world;
    private readonly Vector3 ground;
    private readonly ChainStep step;
    private readonly HashSet<Entity> tossed;
    private StaticMesh? mesh;
    private float chainTimer = ChainDelay;
    private bool chained;
    private float life = Lifetime;

    private RockPillar(PhysicsWorld world, Character owner, Vector3 ground, Vector3 facing, HashSet<Entity> tossed, ChainStep received)
    {
        (this.world, Owner, this.ground, Facing, this.tossed) = (world, owner, ground, facing, tossed);
        step = received.Spent();
    }

    public Character Owner { get; }

    public Vector3 Facing { get; }

    /// <summary>Whom the wave's pillars have tossed; each only once.</summary>
    public IReadOnlyCollection<Entity> Tossed => tossed;

    public override Vector3 Position => ground + Vector3.UnitY * (PillarRadius + Lift);

    public override float Radius => PillarRadius;

    public override bool IsDead => life <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Raises a pillar: other casts' barriers on its spot break and mines go off.</summary>
    public static RockPillar Raise(EntityManager manager, PhysicsWorld world, Character owner, Vector3 ground, Vector3 facing, HashSet<Entity> tossed, ChainStep received)
    {
        ShieldChain.Clear(manager, ground, PillarRadius * ShieldRules.BarrierClearFactor, hits: null);
        var pillar = new RockPillar(world, owner, ground, facing, tossed, received);
        manager.Add(pillar);
        return pillar;
    }

    public override void Update(float dt)
    {
        life -= dt;
        if (IsDead || Manager is not { } manager)
        {
            return;
        }
        Chain(manager, dt);
        foreach (var entity in manager.Query(Position - Vector3.UnitY * PillarRadius, 2 * PillarRadius).Union(manager.Query(Position, PillarRadius)).ToList())
        {
            if (entity is not Character { IsEthereal: false, IsDead: false } character || !character.Body.IsTouchingGround || tossed.Contains(character))
            {
                continue;
            }
            character.Damage(Touch, Owner, Position);
            tossed.Add(character);
            if (!character.IsLevitating && !character.IsImmortal && character.Body.Body.Mass < HeavyMass)
            {
                character.Body.PushVelocity(Vector3.UnitY * Toss);
            }
        }
    }

    private void Chain(EntityManager manager, float dt)
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
        if (manager.Entities.Count < EntityManager.Capacity
            && ShieldChain.NextGround(manager, world, Position, ground + step.Next + Vector3.UnitY * ShieldChain.ProbeUp) is { } next)
        {
            ShieldChain.Clear(manager, next, 1.5f * PillarRadius, hits: null);
            Raise(manager, world, Owner, next, Facing, tossed, step);
        }
    }

    protected internal override void OnAdded()
    {
        if (!world.IsDisposed && !world.IsStepping)
        {
            var (vertices, triangles) = Shapes.Ball(Position, PillarRadius);
            mesh = world.AddStaticMesh(0, vertices, triangles, isLevel: false);
            mesh.Owner = this;
        }
    }

    protected internal override void OnRemoved()
    {
        if (mesh is not null && !world.IsDisposed && !world.IsStepping)
        {
            world.RemoveStaticMesh(mesh);
        }
        mesh = null;
    }
}
