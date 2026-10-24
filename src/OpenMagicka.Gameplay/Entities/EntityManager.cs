using System.Numerics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// The entities of a level (docs/engine/ENTITIES_AI.md §1.3, §1.6): membership, network ids, the
/// update, removal at the end of the frame, and spatial queries through a wrapping grid. A frame
/// runs physics, AI, <see cref="RebuildGrid"/>, level parts, <see cref="Update"/>, bosses, then
/// <see cref="RemoveDead"/> (§1.5). Not thread-safe. Nothing can update, remove or clear while an
/// update or a removal runs; entities can be added at any time.
/// </summary>
/// <summary>An entity the manager let go, with the id it had, for the removal broadcast.</summary>
public readonly record struct RemovedEntity(Entity Entity, NetId Id);

public sealed class EntityManager
{
    public const int Capacity = 512;

    /// <summary>The slack a shield gives a protected query's segment test.</summary>
    public const float ShieldTolerance = 0.5f;

    /// <summary>Ids from here up are local: entities a client makes for itself, never sent.</summary>
    public const uint LocalIds = 0x8000_0000;

    private readonly List<Entity> entities = [];
    private readonly List<IShield> shields = [];
    private readonly Dictionary<NetId, Entity> byId = [];
    private readonly Dictionary<int, Entity> byName = [];
    private readonly EntityGrid grid = new();
    private uint lastId;
    private uint lastLocalId = LocalIds;
    private bool busy;

    public IReadOnlyList<Entity> Entities => entities;

    public IReadOnlyList<IShield> Shields => shields;

    /// <summary>The game the level belongs to (its players, what they unlocked); null outside one.</summary>
    public GameSession? Session { get; set; }

    /// <summary>The scene is indoors (its <c>Indoor</c> flag): no sky for the weather magicks.</summary>
    public bool Indoors { get; set; }

    /// <summary>The level's navigation; null for a level without one (then points are used as they are).</summary>
    public Navigation.NavMesh? NavMesh { get; set; }

    /// <summary>The nav mesh's nearest point a walker with these abilities may stand on, or the point itself.</summary>
    public Vector3 Walkable(Vector3 point, Navigation.NavMovement abilities = Navigation.NavMovement.Default) =>
        NavMesh?.NearestOr(point, abilities) ?? point;

    /// <summary>Where the level finds the characters it makes at run time (summons); null for none.</summary>
    public Characters.IBestiary? Bestiary { get; set; }

    /// <summary>A second scale of the level's time (Death slows it), multiplying <see cref="TimeMultiplier"/>.</summary>
    public float TimeModifier
    {
        get => timeModifier;
        set => timeModifier = value is > 0 and <= 1 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "must be in (0, 1]");
    }

    private float timeModifier = 1;

    /// <summary>How fast the level's time runs (a time warp slows it); the world's frame is scaled by it.</summary>
    public float TimeMultiplier
    {
        get => timeMultiplier;
        set => timeMultiplier = value is > 0 and <= 1 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "must be in (0, 1]");
    }

    private float timeMultiplier = 1;

    /// <summary>
    /// Presentation any entity asks for (sounds, effects, tracers), for the engine; a character's cues
    /// come on its own <c>Cue</c> too, so listen to one or the other. It can fire during the physics step.
    /// </summary>
    public event Action<Entity, Animation.AnimationCue>? Cue;

    internal void RaiseCue(Entity source, Animation.AnimationCue cue) => Cue?.Invoke(source, cue);

    /// <summary>Adds an entity on the server, which gives it the next id; an entity already in keeps its own.</summary>
    public NetId Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (ReferenceEquals(entity.Manager, this))
        {
            return entity.Id;
        }
        var id = new NetId(lastId + 1);
        if (id.Value >= LocalIds)
        {
            throw new InvalidOperationException("the server's ids are used up");
        }
        Admit(entity, id);
        lastId = id.Value;
        return id;
    }

    /// <summary>Adds an entity only this machine knows (a client's gibs and effects), with a local id.</summary>
    public NetId AddLocal(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (ReferenceEquals(entity.Manager, this))
        {
            return entity.Id;
        }
        if (lastLocalId == uint.MaxValue)
        {
            throw new InvalidOperationException("the local ids are used up");
        }
        var id = new NetId(lastLocalId + 1);
        Admit(entity, id);
        lastLocalId = id.Value;
        return id;
    }

    /// <summary>Adds an entity under the id the server gave it (a client, from a spawn message).</summary>
    public void Add(Entity entity, NetId id)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (id.IsNone || id.Value >= LocalIds)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "not a server id");
        }
        if (ReferenceEquals(entity.Manager, this) && entity.Id == id)
        {
            return;
        }
        Admit(entity, id);
        lastId = Math.Max(lastId, id.Value);
    }

    private void Admit(Entity entity, NetId id)
    {
        if (entity.Manager is not null)
        {
            throw new InvalidOperationException($"the entity is already in a manager as {entity.Id}");
        }
        if (byId.ContainsKey(id))
        {
            throw new InvalidOperationException($"{id} is taken");
        }
        if (entities.Count >= Capacity)
        {
            throw new InvalidOperationException($"the manager holds at most {Capacity} entities");
        }
        (entity.Id, entity.Manager) = (id, this);
        entities.Add(entity);
        byId.Add(id, entity);
        if (entity.UniqueId != 0)
        {
            byName[entity.UniqueId] = entity;
        }
        if (entity is IShield shield)
        {
            shields.Add(shield);
        }
        entity.OnAdded();
    }

    public bool Contains(Entity entity) => entity is not null && ReferenceEquals(entity.Manager, this);

    public Entity? Find(NetId id) => byId.GetValueOrDefault(id);

    /// <summary>The entity that last took a script name.</summary>
    public Entity? FindByUniqueId(int uniqueId) => uniqueId == 0 ? null : byName.GetValueOrDefault(uniqueId);

    /// <summary>Updates every entity, including those added during the loop.</summary>
    public void Update(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        using var _ = Busy();
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i].Update(dt);
        }
    }

    /// <summary>Takes out the entities that are removable, in order, with their ids for the broadcast.</summary>
    public IReadOnlyList<RemovedEntity> RemoveDead()
    {
        using var _ = Busy();
        var removed = entities.Where(e => e.IsRemovable).Select(e => new RemovedEntity(e, e.Id)).ToList();
        if (removed.Count > 0)
        {
            var leaving = removed.Select(r => r.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            entities.RemoveAll(leaving.Contains);
            removed.ForEach(r => Release(r.Entity));
        }
        return removed;
    }

    /// <summary>Takes everything out (a level ends).</summary>
    public void Clear()
    {
        using var _ = Busy();
        var all = entities.ToList();
        entities.Clear();
        all.ForEach(Release);
        grid.Rebuild(entities);
        (timeMultiplier, timeModifier) = (1, 1);
    }

    /// <summary>
    /// Unregisters an entity that has left the list; it hears <see cref="Entity.OnRemoved"/> still
    /// knowing its id, and may be added again from there.
    /// </summary>
    private void Release(Entity entity)
    {
        byId.Remove(entity.Id);
        if (entity.UniqueId != 0 && byName.TryGetValue(entity.UniqueId, out var holder) && ReferenceEquals(holder, entity))
        {
            byName.Remove(entity.UniqueId);
        }
        if (entity is IShield shield)
        {
            shields.Remove(shield);
        }
        entity.Manager = null;
        entity.OnRemoved();
        if (entity.Manager is null)
        {
            entity.Id = NetId.None;
        }
    }

    /// <summary>Keeps the name index in step when an entity in the manager changes its name.</summary>
    internal void Rename(Entity entity, int oldName)
    {
        if (oldName != 0 && byName.TryGetValue(oldName, out var holder) && ReferenceEquals(holder, entity))
        {
            byName.Remove(oldName);
        }
        if (entity.UniqueId != 0)
        {
            byName[entity.UniqueId] = entity;
        }
    }

    private Guard Busy()
    {
        if (busy)
        {
            throw new InvalidOperationException("the manager is already updating or removing");
        }
        busy = true;
        return new Guard(this);
    }

    private readonly struct Guard(EntityManager manager) : IDisposable
    {
        public void Dispose() => manager.busy = false;
    }

    /// <summary>Places every entity in the grid where it is now; queries see these places until the next rebuild.</summary>
    public void RebuildGrid() => grid.Rebuild(entities);

    /// <summary>
    /// The living entities within <paramref name="radius"/> of <paramref name="center"/>, counting their own
    /// radius; on X and Z only with <paramref name="ignoreY"/>. With <paramref name="ignoreProtected"/>, the
    /// shields in the result hide what is behind them, and every entity that is not damageable is dropped.
    /// </summary>
    public List<Entity> Query(Vector3 center, float radius, bool ignoreProtected = false, bool ignoreY = false)
    {
        CheckQuery(center, radius);
        var flat = ignoreY ? center with { Y = 0 } : center;                   // the shield test uses it too, as the original
        var found = grid.Near(center, radius)
            .Where(e => Contains(e) && !e.IsDead && Distance(flat, e.Position, ignoreY) <= radius + e.Radius)
            .ToList();
        if (ignoreProtected)
        {
            foreach (var shield in found.OfType<IShield>().ToList())
            {
                found.RemoveAll(e => !ReferenceEquals(e, shield) && Hidden(shield, flat, e));
            }
        }
        return found;
    }

    /// <summary>Behind the shield (hit somewhere but at the query's centre), or not damageable at all.</summary>
    private static bool Hidden(IShield shield, Vector3 center, Entity entity) =>
        !entity.IsDamageable || (shield.Blocks(center, entity.Position, ShieldTolerance, out var hit) && Vector3.DistanceSquared(hit, center) > 1e-6f);

    private static float Distance(Vector3 center, Vector3 position, bool ignoreY) =>
        Vector3.Distance(center, ignoreY ? position with { Y = 0 } : position);

    /// <summary>
    /// The nearest living damageable entity within <paramref name="radius"/> (counting its radius) other
    /// than <paramref name="caster"/>; with <paramref name="ignoreProtected"/>, none that any shield hides.
    /// </summary>
    public Entity? Closest(Vector3 center, float radius, Entity? caster = null, bool ignoreProtected = false)
    {
        CheckQuery(center, radius);
        Entity? closest = null;
        var best = float.MaxValue;
        foreach (var entity in grid.Near(center, radius))
        {
            if (!Contains(entity) || !entity.IsDamageable || entity.IsDead || ReferenceEquals(entity, caster))
            {
                continue;
            }
            var squared = Vector3.DistanceSquared(entity.Position, center);
            var reach = radius + entity.Radius;
            if (!(reach >= 0 && squared <= reach * reach && squared < best))
            {
                continue;
            }
            if (ignoreProtected && shields.Any(s => s.Blocks(center, entity.Position, ShieldTolerance, out _)))
            {
                continue;
            }
            (closest, best) = (entity, squared);
        }
        return closest;
    }

    private static void CheckQuery(Vector3 center, float radius)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(center), center, "must be finite");
        }
        if (!(radius >= 0) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be finite and not negative");
        }
    }

    /// <summary>Whether the entity stands inside a sphere shield, and which.</summary>
    public bool IsProtectedByShield(Entity entity, out IShield? shield)
    {
        ArgumentNullException.ThrowIfNull(entity);
        shield = shields.FirstOrDefault(s => s.IsSphere && Vector3.DistanceSquared(entity.Position, s.Position) < s.Radius * s.Radius);
        return shield is not null;
    }
}
