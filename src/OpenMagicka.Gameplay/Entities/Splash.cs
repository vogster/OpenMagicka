using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>Damage all around a point, weaker towards the edge (docs/engine/ENTITIES_AI.md §8.2).</summary>
public static class Splash
{
    /// <summary>
    /// Every damageable thing within <paramref name="range"/> of <paramref name="origin"/> (counting its
    /// radius, not hidden by a shield, not ethereal, not <paramref name="ignored"/>) takes the damage, its
    /// magnitude × (1 − (distance − its radius) / range), as <paramref name="attacker"/>.
    /// </summary>
    public static DamageResult Hit(EntityManager manager, Entity? attacker, Entity? ignored, Vector3 origin, float range, DamageEntry damage)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (!(range > 0) || !float.IsFinite(range))
        {
            return DamageResult.None;
        }
        var result = DamageResult.None;
        foreach (var entity in manager.Query(origin, range, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, ignored) || !entity.IsDamageable || entity is Character { IsEthereal: true })
            {
                continue;
            }
            var share = 1 - (Vector3.Distance(origin, entity.Position) - entity.Radius) / range;
            if (share > 0)
            {
                result |= entity.Damage(damage with { Magnitude = damage.Magnitude * share }, attacker, origin);
            }
        }
        return result;
    }
}
