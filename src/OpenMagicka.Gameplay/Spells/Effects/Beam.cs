using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>A beam for the renderer: where it runs and how far, this frame.</summary>
public sealed record BeamCue(Beam Beam, Vector3 From, Vector3 To) : AnimationCue;

/// <summary>
/// One stretch of an arcane or life beam: it grows, is cut by what it meets, reflects, merges with
/// or explodes against other beams, and damages what it rests on (docs/gameplay/SPELLS.md §5.4).
/// Reflections and merges are child beams. A simulation object.
/// </summary>
public sealed class Beam : Entity
{
    public const float Thickness = 0.25f;

    /// <summary>The level material that reflects beams.</summary>
    public const int ReflectMaterial = 7;

    private readonly PhysicsWorld world;
    private readonly List<(Entity Owner, IReadOnlyList<DamageEntry> Damage)> owners = [];
    private readonly List<Beam> parents = [];
    private Vector3 position;
    private Vector3 direction;
    private float damageTimer = CastRules.TickInterval;
    private bool dead;
    private bool locked;
    private bool exploding;
    private float countdown;
    private Vector3 explosionAt;
    private Beam? companion;

    private Beam(PhysicsWorld world, Vector3 position, Vector3 direction, bool arcane, Elements extra, int depth)
    {
        (this.world, this.position, this.direction, IsArcane, ExtraElements, Depth) = (world, position, Normalized(direction), arcane, extra, depth);
    }

    public override Vector3 Position => position;

    public Vector3 Direction => direction;

    public float Length { get; private set; }

    /// <summary>Its end is cut short by what it meets (an entity, the level): the hit's loop sounds there.</summary>
    public bool IsHitting { get; private set; }

    public bool IsArcane { get; }

    /// <summary>What it carries besides Arcane and Life.</summary>
    public Elements ExtraElements { get; private set; }

    public int Depth { get; }

    public Beam? Child { get; private set; }

    public IReadOnlyList<Beam> Parents => parents;

    /// <summary>Whose damage it carries.</summary>
    public IEnumerable<Entity> Owners => owners.Select(o => o.Owner);

    /// <summary>Its colour (SPELLS.md §14): its spell's; a merged beam's is halfway between its parents', a bounced one's its parent's.</summary>
    public Vector3 Colour { get; private set; }

    /// <summary>How long it has been alive: its picture's waves run with it.</summary>
    public float Age { get; private set; }

    /// <summary>The helices its picture winds round it: 4 for a first beam, else 2 and 2 for each caster whose damage it carries.</summary>
    public int Rings => parents.Count == 0 ? 4 : 2 + 2 * owners.Select(o => o.Owner).Distinct(ReferenceEqualityComparer.Instance).Count();

    public bool IsExploding => exploding;

    public override float Radius => 0;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    /// <summary>The first beam of a cast, from its caster.</summary>
    public static Beam? Fire(Character caster, Spell spell, IReadOnlyList<DamageEntry> damage)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity)
        {
            return null;
        }
        var beam = new Beam(caster.World, caster.CastSource, caster.Body.Facing, (spell.Element & Elements.Arcane) != 0, spell.Element & BeamRules.ExtraElements, 0);
        beam.owners.Add((caster, damage));
        beam.Colour = SpellColours.Of(spell);
        manager.Add(beam);
        return beam;
    }

    /// <summary>The cast moves its first beam with the caster (a locked chain stays).</summary>
    internal void Aim(Vector3 from, Vector3 along)
    {
        if (!locked)
        {
            (position, direction) = (from, Normalized(along));
        }
    }

    public void Kill() => dead = true;

    public override void Update(float dt)
    {
        if (dead || world.IsDisposed || Manager is not { } manager)
        {
            return;
        }
        Age += dt;
        if ((parents.Count > 0 && parents.All(p => p.dead))
            || (parents.Count == 0 && owners.Count > 0 && !ReferenceEquals(owners[0].Owner.Manager, manager)))
        {
            Kill();                                                           // orphaned, or its caster left the level
            return;
        }
        if (parents.Count > 1)
        {
            position = parents.Aggregate(Vector3.Zero, (sum, p) => sum + p.position + p.direction * p.Length) / parents.Count;
            direction = Normalized(parents.Aggregate(Vector3.Zero, (sum, p) => sum + p.direction));
        }
        if (exploding)
        {
            manager.RaiseCue(this, new BeamCue(this, position, position + direction * Length));
            countdown -= dt;
            if (countdown <= 0)
            {
                Explode(manager);
            }
            return;
        }
        if (locked)
        {
            manager.RaiseCue(this, new BeamCue(this, position, position + direction * Length));
            return;
        }
        Length = MathF.Min(Length + dt * BeamRules.GrowthPerSecond, BeamRules.MaxLength);
        var (entity, at) = Nearest(manager);
        if (entity is not null)
        {
            Length = Vector3.Distance(position, at);
        }
        else
        {
            damageTimer = CastRules.TickInterval;
        }
        var met = entity is not null;
        var (reflects, normal, wallAt) = Wall(ref met, ref entity);
        IsHitting = met;
        var crossing = Cross(manager, ref met, ref entity);
        if (!crossing && Child is { } child && entity is not IShield && !Bubbled(entity) && !reflects)   // as the original, a beam joined at its start goes this way too
        {
            child.Kill();
            Child = null;
        }
        if (entity is not null && !entity.IsDead)
        {
            Rest(manager, entity, at, dt);
        }
        else if (reflects && !crossing)
        {
            damageTimer = CastRules.TickInterval;
            Reflect(manager, wallAt, normal);
        }
        manager.RaiseCue(this, new BeamCue(this, position, position + direction * Length));
    }

    /// <summary>The nearest damageable thing the beam touches, and where.</summary>
    private (Entity? Entity, Vector3 At) Nearest(EntityManager manager)
    {
        var segment = new Segment(position, direction * Length);
        Entity? nearest = null;
        var at = position;
        var best = float.MaxValue;
        foreach (var entity in manager.Query(position, Length * 1.25f, ignoreProtected: false))
        {
            if (!entity.IsDamageable || (owners.Count == 1 && ReferenceEquals(owners[0].Owner, entity)) || !entity.SegmentIntersect(segment, Thickness, out var hit))
            {
                continue;
            }
            var distance = Vector3.DistanceSquared(hit, position);
            if (distance < best && distance > 1e-6f)
            {
                (best, nearest, at) = (distance, entity, hit);
            }
        }
        return (nearest, at);
    }

    /// <summary>The level cuts the beam (a face seen from behind is looked past by 0.1); a Reflect face met from the front reflects it.</summary>
    private (bool Reflects, Vector3 Normal, Vector3 At) Wall(ref bool met, ref Entity? entity)
    {
        var end = position + direction * Length;
        if (world.CastSegment(position, end, levelOnly: true) is not { } wall)
        {
            return (false, Vector3.Zero, end);
        }
        if (Vector3.Dot(wall.Normal, direction) > 0)
        {
            if (world.CastSegment(position + direction * 0.1f, end, levelOnly: true) is not { } beyond)
            {
                return (false, Vector3.Zero, end);
            }
            wall = beyond;
        }
        (met, entity, Length) = (true, null, Vector3.Distance(position, wall.Position));
        var reflects = Vector3.Dot(wall.Normal, direction) < 0 && wall.Material == ReflectMaterial && wall.Normal.LengthSquared() > 1e-6f;
        return (reflects, wall.Normal, wall.Position);
    }

    /// <summary>Meeting another beam: explode, or merge into a child along the mean direction.</summary>
    private bool Cross(EntityManager manager, ref bool met, ref Entity? entity)
    {
        Beam? other = null;
        var atStart = false;
        foreach (var beam in manager.Entities.OfType<Beam>().ToList())
        {
            if (ReferenceEquals(beam, this) || parents.Contains(beam) || ReferenceEquals(beam, Child) || beam.parents.Contains(this) || beam.dead
                || beam.exploding || beam.locked)                             // a pair primes once
            {
                continue;
            }
            var mine = new Segment(position, direction * Length);
            var theirs = new Segment(beam.position, beam.direction * beam.Length);
            if (Segment.DistanceSquared(mine, theirs, out var t, out var u) > BeamRules.MeetingDistance || t <= 1e-6f)
            {
                continue;
            }
            var reach = atStart ? (Length - 1) / Length : 1;
            if (t <= reach && u >= 1 / Length)                                // this beam's length, as the original, though u is along the other
            {
                (entity, atStart, met, other) = (null, u * beam.Length < 1, false, beam);
                Length *= t;
            }
        }
        if (other is null)
        {
            return false;
        }
        var meeting = position + direction * Length;
        var shared = !atStart && owners.Any(o => other.owners.Any(p => ReferenceEquals(p.Owner, o.Owner)));
        if (BeamRules.Explode(shared, ExtraElements | (IsArcane ? Elements.Arcane : 0), other.ExtraElements | (other.IsArcane ? Elements.Arcane : 0), 1))
        {
            Prime(other, meeting, lockChainOf: this);
            return true;
        }
        if (!ReferenceEquals(other.Child, Child) && !ReferenceEquals(other, Child))
        {
            Child?.Kill();
            Child = null;
            if (other.Child is { } theirs && theirs.parents.Count <= 2)
            {
                theirs.Kill();
            }
        }
        if (atStart && Child is null && !other.parents.Contains(this))
        {
            foreach (var owner in owners)
            {
                if (other.owners.Any(o => ReferenceEquals(o.Owner, owner.Owner)))
                {
                    Prime(other, meeting, lockChainOf: other);
                    return true;
                }
                other.owners.Add(owner);
            }
            Child = other;
            other.parents.Add(this);
        }
        var mean = Vector3.Lerp(direction, other.direction, 0.5f);
        if (mean.Length() < 0.2f)
        {
            Prime(other, meeting, lockChainOf: this);
        }
        else if (Child is null)
        {
            var depth = Math.Max(Depth, other.Depth);
            if (depth < BeamRules.MaxChainDepth)
            {
                Spawn(manager, meeting, mean, depth + 1, other);
            }
        }
        return true;
    }

    /// <summary>Resting on something: a shield or a bubble reflects it, anything else takes the damage on the beam's beat.</summary>
    private void Rest(EntityManager manager, Entity entity, Vector3 at, float dt)
    {
        if (entity is IShield shield)
        {
            damageTimer = CastRules.TickInterval;
            Reflect(manager, at, (at - shield.Position) with { Y = 0 });
        }
        else if (Bubbled(entity) && entity is Character bubbled)
        {
            damageTimer = CastRules.TickInterval;
            Reflect(manager, at, (at - bubbled.Position) with { Y = 0 });
        }
        else if (damageTimer <= 0)
        {
            damageTimer += CastRules.TickInterval;
            foreach (var (owner, damage) in owners.ToList())
            {
                var tick = damage.Select(d => d with { Magnitude = d.Magnitude * CastRules.TickMagnitude }).ToList();
                entity.Damage(tick, owner, at);
                if (entity is Character character)
                {
                    foreach (var entry in tick.Where(d => (d.Element & ElementSets.Beams) != 0))
                    {
                        if (character.HitPoints <= 0 && !character.IsBloating)
                        {
                            character.BloatKill(entry.Element, owner);
                        }
                    }
                }
            }
        }
        damageTimer -= dt;
    }

    /// <summary>A reflection: a child along the mirrored direction, or, coming back on itself, an explosion at the first junction.</summary>
    private void Reflect(EntityManager manager, Vector3 at, Vector3 normal)
    {
        if (normal.LengthSquared() <= 1e-12f)
        {
            Child?.Kill();
            Child = null;
            return;
        }
        var n = Vector3.Normalize(normal);
        var mirrored = Normalized(Vector3.Reflect(direction, n));
        if (Vector3.Dot(mirrored, direction) < -0.99f)
        {
            Prime(null, FirstJunction(), lockChainOf: this);
            return;
        }
        if (Child is { parents.Count: > 1 })
        {
            Child.Kill();
            Child = null;
        }
        if (Child is null && Depth < BeamRules.MaxChainDepth)
        {
            Spawn(manager, at, mirrored, Depth + 1, null);
        }
        Child?.Aim(at, mirrored);
    }

    private void Spawn(EntityManager manager, Vector3 from, Vector3 along, int depth, Beam? second)
    {
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return;
        }
        var child = new Beam(world, from, along, IsArcane, ExtraElements | (second?.ExtraElements ?? 0), depth)
        {
            Colour = second is null ? Colour : Vector3.Lerp(Colour, second.Colour, 0.5f),
        };
        child.parents.Add(this);
        child.owners.AddRange(owners);
        if (second is not null)
        {
            child.parents.Add(second);
            second.Child = child;
            child.owners.AddRange(second.owners.Where(o => !child.owners.Any(c => ReferenceEquals(c.Owner, o.Owner))));
        }
        Child = child;
        manager.Add(child);
    }

    /// <summary>The explosion comes in 0.25 s; the chain from its last beam back is held still till then.</summary>
    private void Prime(Beam? with, Vector3 at, Beam lockChainOf)
    {
        (exploding, companion, countdown, explosionAt) = (true, with, BeamRules.ExplosionDelay, at);
        Length = Vector3.Distance(position, at);
        var last = lockChainOf;
        while (last.Child is { } next)
        {
            last = next;
        }
        last.LockAll();
    }

    private void Explode(EntityManager manager)
    {
        var last = this;
        while (last.Child is { } next)
        {
            last = next;
        }
        var count = owners.Count + (companion?.owners.Count ?? 0);
        var radius = BeamRules.ExplosionRadius(count);
        var blasts = owners.Concat(companion?.owners ?? []).ToList();
        last.KillAll();
        if (companion is not null)
        {
            var theirs = companion;
            while (theirs.Child is { } next)
            {
                theirs = next;
            }
            theirs.KillAll();                                                 // the pair goes up together, once
        }
        foreach (var (owner, damage) in blasts)
        {
            Blasts.Full(manager, world, owner, null, radius, explosionAt, damage, source: this);
        }
        Blasts.Full(manager, world, null, null, BeamRules.KnockbackRadius, explosionAt,
            [new DamageEntry(AttackProperties.Knockback, Elements.None, BeamRules.KnockbackStrength, BeamRules.KnockbackMagnitude)], source: this);
    }

    private Vector3 FirstJunction() => parents.Count == 1 ? parents[0].FirstJunction() : position;

    private void LockAll()
    {
        locked = true;
        foreach (var parent in parents)
        {
            parent.LockAll();
        }
    }

    private void KillAll()
    {
        dead = true;
        foreach (var parent in parents)
        {
            parent.Child = null;
            parent.KillAll();
        }
        parents.Clear();
    }

    protected internal override void OnRemoved()
    {
        Child?.Kill();
        Child = null;
        foreach (var parent in parents.Where(p => ReferenceEquals(p.Child, this)))
        {
            parent.Child = null;
        }
        parents.Clear();
    }

    private static bool Bubbled(Entity? entity) => entity is Character { SelfShield: { IsActive: true, IsBubble: true } };

    private static Vector3 Normalized(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : -Vector3.UnitZ;
}
