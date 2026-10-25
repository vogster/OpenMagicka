using System.Numerics;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>The dispensers' models (the original's order).</summary>
public enum Dispensers { SpiderHole, GoblinTent, SpiderEggs }

/// <summary>
/// A dispenser (a level script's <c>spawnDispenser</c>): a spider hole, a goblin tent, a clutch of eggs
/// that, while active, lets out a character of one of its kinds (at random) each period, standing in a
/// fixed capsule 1.25 wide. Each kind has its count (one count shared when only one is given); a kind
/// spent drops out, and once none is left to let out, the dispenser dies. 500 hit points; each hit
/// takes its amount × magnitude, whole. Its characters attack at once, reacting to nothing, rising with
/// their "spawn" clip. A simulation object.
/// </summary>
public sealed class Dispenser : Entity
{
    public const float HitPointsMax = 500, CapsuleRadius = 1.25f, CapsuleLength = 2;

    /// <summary>It stands this far over the floor found under its place.</summary>
    public const float Lift = 1.75f;

    private readonly PhysicsWorld world;
    private readonly PhysicsBody body;
    private readonly List<string> kinds;
    private readonly List<int> counts;
    private readonly Random random;
    private readonly Vector3 position;
    private float timer;

    /// <param name="kinds">The characters' assets ("data/characters/…").</param>
    public Dispenser(PhysicsWorld world, Dispensers model, Vector3 position, Vector3 facing, IReadOnlyList<string> kinds, IReadOnlyList<int> counts,
        float period, bool active, int seed = 0)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(counts);
        if (!float.IsFinite(period) || !float.IsFinite(position.X + position.Y + position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "the period and the place must be finite");
        }
        (Model, this.position, Facing, Period, IsActive, random) = (model, position, facing, period, active, new Random(seed));
        this.kinds = [.. kinds];
        this.counts = [.. counts.Take(kinds.Count)];                          // as the original: counts past the kinds are dropped
        body = world.AddBody(new CapsuleShape(CapsuleRadius, CapsuleLength), position, 1, BodyOptions.Static);
        body.Tag = this;
    }

    public Dispensers Model { get; }

    public Vector3 Facing { get; }

    public float Period { get; }

    public bool IsActive { get; private set; }

    public float HitPoints { get; private set; } = HitPointsMax;

    public IReadOnlyList<int> Counts => counts;

    public override Vector3 Position => position;

    public override float Radius => CapsuleRadius;

    public override bool IsDamageable => true;

    public override bool IsDead => HitPoints <= 0;

    public override bool IsRemovable => IsDead;

    public PhysicsBody Body => body;

    protected internal override IEnumerable<PhysicsBody> Bodies => [body];

    /// <summary>It starts letting its characters out (a script's activateDispenser).</summary>
    public void Activate() => IsActive = true;

    public void Kill() => HitPoints = 0;

    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        var amount = MathF.Truncate(damage.Amount * damage.Magnitude);
        if (IsDead || !float.IsFinite(amount))
        {
            return DamageResult.None;
        }
        if (amount <= 0)
        {
            return DamageResult.Deflected;
        }
        HitPoints -= amount;
        return DamageResult.Damaged;
    }

    public override void Update(float dt)
    {
        if (!IsActive || IsDead || (timer -= dt) > 0)
        {
            return;
        }
        timer = Period;
        if (!LetOut())
        {
            HitPoints = 0;
        }
    }

    private bool LetOut()
    {
        if (kinds.Count == 0 || Manager is not { } manager)
        {
            return false;
        }
        if (counts.Count != 1)
        {
            for (var i = counts.Count - 1; i >= 0; i--)
            {
                if (counts[i] == 0)
                {
                    counts.RemoveAt(i);
                    kinds.RemoveAt(i);
                }
            }
        }
        if (kinds.Count == 0)
        {
            return false;
        }
        var pick = random.Next(kinds.Count);
        var count = counts.Count == 1 ? 0 : pick;
        if (count >= counts.Count || counts[count] <= 0)
        {
            return false;
        }
        if (manager.Entities.Count < EntityManager.Capacity && manager.Bestiary?.Find(kinds[pick]) is { } found)
        {
            var (template, animations) = found;
            var character = new Character(template, world, position, random: new Random(random.Next()), animations: animations);
            manager.Add(character);
            character.Body.Face(Facing);
            character.Mind?.Give(new GiveOrderSpec(Order.Attack), byName: false);
            character.GoToAnimation("spawn", 0.001f);
        }
        counts[count]--;
        return true;
    }

    protected internal override void OnRemoved()
    {
        if (!body.IsRemoved && !world.IsDisposed)
        {
            world.Remove(body);
        }
    }
}
