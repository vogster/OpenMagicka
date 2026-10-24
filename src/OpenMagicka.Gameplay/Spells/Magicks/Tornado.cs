using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Tornado: a whirlwind 4 ahead that wanders for 15 s, flinging what it touches (MAGICKS.md §5.8).</summary>
public sealed class Tornado() : Magick(MagickType.Tornado, DirectClip)
{
    public const float Ahead = 4;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return true;
        }
        manager.Add(new Whirlwind(caster, manager.Walkable(caster.Position + caster.Body.Facing * Ahead), caster.Body.Facing));
        return true;
    }
}

/// <summary>
/// The whirlwind: a column of radius 2.5 and length 6. It heads for a point 5–15 away within ±π/4 of
/// its way, picking a new one when it gets there, at a speed of |4·sin t|; it eases its height to the
/// floor below. Each thing it touches is struck at most once a second. A push sends it 10 along the
/// push. A simulation object.
/// </summary>
public sealed class Whirlwind : Entity
{
    public const float Lifetime = 15, ColumnRadius = 2.5f, Length = 6, Memory = 1, Arrive = 1;

    public const string Rumble = "magick_tornado_rumble", Effect = "magick_tornado", HitEffect = "magick_tornado_hit";

    public static readonly IReadOnlyList<DamageEntry> Blow = [new(AttackProperties.Knockback, Elements.Earth, 800, 2)];

    private readonly Dictionary<Entity, float> struck = new(ReferenceEqualityComparer.Instance);
    private readonly List<Navigation.PathNode> path = [];
    private Vector3 position;
    private Vector3 heading;
    private Vector3? goal;
    private float ttl = Lifetime;

    internal Whirlwind(Character? owner, Vector3 position, Vector3 heading)
    {
        (Owner, this.position) = (owner, position);
        var flat = heading with { Y = 0 };
        this.heading = flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
    }

    public Character? Owner { get; }

    public override Vector3 Position => position;

    public override float Radius => ColumnRadius;

    public override bool IsDead => ttl < 0;

    public override bool IsRemovable => IsDead;

    /// <summary>It dies down over a second.</summary>
    public void Kill() => ttl = MathF.Min(ttl, 1);

    /// <summary>A push sends it 10 along the push's way (thrown away from <paramref name="from"/>).</summary>
    public override bool Impulse(Vector3 from, float elevation, float power, float distance)
    {
        var away = (position - from) with { Y = 0 };
        if (away.LengthSquared() > 1e-12f)
        {
            var aim = position + Vector3.Normalize(away) * 10;
            goal = Manager?.NavMesh is { } nav ? Along(nav, aim) : Short(aim);
        }
        return true;
    }

    protected internal override void OnAdded()
    {
        Manager?.RaiseCue(this, new SoundCue(Rumble, MagickEffects.SpellsBank, position, heading));
        Manager?.RaiseCue(this, new WorldEffectCue(Effect, position, heading));
    }

    public override void Update(float dt)
    {
        ttl -= dt;
        foreach (var entity in struck.Keys.ToList())
        {
            if ((struck[entity] -= dt) <= 0)
            {
                struck.Remove(entity);
            }
        }
        if (Manager is not { } manager || Owner?.World is not { IsDisposed: false } world)
        {
            return;
        }
        Strike(manager);
        var speed = MathF.Abs(MathF.Sin(ttl) * 4);
        if (manager.NavMesh is { } nav)
        {
            var aim = Walk(nav);
            var way = aim - position;
            if (way.LengthSquared() > 1e-12f)
            {
                way = Vector3.Normalize(way);                                 // as the original: normalised with its rise, so a climb is slower
                heading = way with { Y = 0 } is var flat && flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : heading;
                position += (way with { Y = 0 }) * speed * dt;
            }
        }
        else
        {
            if (goal is not { } aim || Vector3.DistanceSquared(aim with { Y = position.Y }, position) <= Arrive * Arrive)
            {
                aim = NewGoal(world);
            }
            var way = (aim - position) with { Y = 0 };
            if (way.LengthSquared() > 1e-12f)
            {
                heading = Vector3.Normalize(way);
            }
            position += heading * speed * dt;
        }
        if (world.CastSegment(position, position - Vector3.UnitY * 3, levelOnly: true) is { } floor)
        {
            position.Y += (floor.Position.Y - position.Y) * dt;
        }
    }

    /// <summary>
    /// Along the nav mesh, as the original: it heads for the path's first corner; within 1 of it,
    /// that corner and the next are taken off, and it heads for the second for one frame, then for
    /// the one after. So it heads for every other corner, cutting the corners between. With none
    /// left: a new point 5–15 ahead within ±π/4 and a path to it, whose first corner it heads for
    /// one frame (without a full path, the point itself) before going on to the path's next.
    /// </summary>
    private Vector3 Walk(Navigation.NavMesh nav)
    {
        if (path.Count > 0)
        {
            var aim = path[0].Position;
            while (path.Count > 0 && Vector3.DistanceSquared(position, aim) <= Arrive * Arrive)
            {
                aim = path[0].Position;
                path.RemoveAt(0);
            }
            goal = aim;
            return aim;
        }
        var random = Owner!;
        var turn = ((float)random.NextRandom() - 0.5f) * MathF.PI / 2;
        var distance = 5 + (float)random.NextRandom() * 10;
        var next = Along(nav, position + Vector3.Transform(heading, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn)) * distance);
        goal = next;
        return next;
    }

    /// <summary>A path to the point (at the whirlwind's height): its second corner when it gets there, else the point.</summary>
    private Vector3 Along(Navigation.NavMesh nav, Vector3 aim)
    {
        aim.Y = position.Y;
        if (nav.FindPath(position, aim, Navigation.NavMovement.Default, path) && path.Count >= 2)
        {
            path.RemoveAt(0);
            aim = path[0].Position;
            path.RemoveAt(0);
        }
        return aim;
    }

    /// <summary>
    /// Without a nav mesh: the next point, 5–15 ahead within ±π/4, straight; it chooses again when a
    /// wall stands in the way.
    /// </summary>
    private Vector3 NewGoal(Physics.PhysicsWorld world)
    {
        var random = Owner!;
        for (var tries = 0; tries < 4; tries++)
        {
            var turn = ((float)random.NextRandom() - 0.5f) * MathF.PI / 2;
            var distance = 5 + (float)random.NextRandom() * 10;
            var aim = position + Vector3.Transform(heading, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn)) * distance;
            if (world.CastSegment(position + Vector3.UnitY, aim + Vector3.UnitY, levelOnly: true) is null)
            {
                goal = aim;
                return aim;
            }
        }
        heading = -heading;                                                   // boxed in: back the way it came
        goal = Short(position + heading * 5);
        return goal.Value;
    }

    /// <summary>The point, or short of the first wall on the way to it.</summary>
    private Vector3 Short(Vector3 aim)
    {
        if (Owner?.World is { IsDisposed: false } world && world.CastSegment(position + Vector3.UnitY, aim + Vector3.UnitY, levelOnly: true) is { } wall)
        {
            return position + (aim - position) * MathF.Max(0, wall.Fraction - 0.1f);
        }
        return aim;
    }

    /// <summary>Strikes what touches the column (not missiles), at the point of its surface facing it.</summary>
    private void Strike(EntityManager manager)
    {
        foreach (var entity in manager.Query(position, ColumnRadius + Length / 2))
        {
            if (!entity.IsDamageable || entity is Missile || ReferenceEquals(entity, this) || struck.ContainsKey(entity))
            {
                continue;
            }
            var off = entity.Position - position;
            var flat = off with { Y = 0 };
            if (flat.Length() > ColumnRadius + entity.Radius || MathF.Abs(off.Y) > Length / 2 + ColumnRadius + entity.Radius)
            {
                continue;
            }
            var at = position + (off.LengthSquared() > 1e-12f ? Vector3.Normalize(off) * ColumnRadius : Vector3.Zero);
            manager.RaiseCue(this, new WorldEffectCue(HitEffect, at, Vector3.UnitY));
            entity.Damage(Blow, Owner, at);
            struck[entity] = Memory;
        }
    }
}
