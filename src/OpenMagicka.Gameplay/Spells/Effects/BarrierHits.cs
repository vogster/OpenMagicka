using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// Whom one cast's barriers struck lately: they share it, so a target touching several of them is
/// struck once per <see cref="Memory"/>. Its first barrier still standing keeps its time.
/// </summary>
public sealed class BarrierHits
{
    public const float Memory = 0.25f;

    private readonly Dictionary<Entity, float> times = new(ReferenceEqualityComparer.Instance);
    private readonly List<Barrier> barriers = [];

    public IReadOnlyCollection<Entity> Struck => times.Keys;

    public bool Contains(Entity entity) => times.ContainsKey(entity);

    internal void Add(Entity entity) => times.TryAdd(entity, Memory);

    internal void Join(Barrier barrier) => barriers.Add(barrier);

    internal void Leave(Barrier barrier) => barriers.Remove(barrier);

    internal void Tick(Barrier barrier, float dt)
    {
        if (barriers.Count == 0 || !ReferenceEquals(barriers[0], barrier))
        {
            return;
        }
        foreach (var entity in times.Keys.ToList())
        {
            times[entity] -= dt;
            if (times[entity] <= 0)
            {
                times.Remove(entity);
            }
        }
    }
}
