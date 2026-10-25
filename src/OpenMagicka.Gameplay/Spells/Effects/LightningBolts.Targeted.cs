using System.Numerics;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>Whom a bolt has struck lately: it neither strikes them again nor chains to them.</summary>
public interface IHitList
{
    bool Contains(Entity entity);

    void Add(Entity entity);
}

/// <summary>A bolt at a given target (docs/gameplay/SPELLS.md §5.6: an elemental barrier's touch).</summary>
public static partial class LightningBolts
{
    /// <summary>A chained link looks this far either side of the bolt's way.</summary>
    public const float ChainAngle = MathF.PI / 4;

    /// <summary>
    /// A bolt from a source at a given target, then on from it as the original's targeted bolt: to
    /// the nearest thing within the range left and <see cref="ChainAngle"/> of the bolt's way, each
    /// link's length coming off the range. It stops at a deflection, a barrier or a shield.
    /// </summary>
    /// <param name="caster">Whose bolt it is (its spell-range buffs count); null for none.</param>
    /// <param name="cueSource">What the cues come from without a caster.</param>
    public static void StrikeAt(EntityManager manager, Character? caster, Entity cueSource, Vector3 source, Entity target, float range,
        IReadOnlyList<DamageEntry> damage, IHitList hits)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hits);
        if (caster is not null)
        {
            range = caster.Modify(Modified.SpellRange, range);
        }
        for (var link = 0; link < EntityManager.Capacity; link++)
        {
            hits.Add(target);
            var deflected = Hit(caster, source, target, damage);
            var at = target is IShield shield ? Nearest(shield, source) : target.Position;
            Raise(manager, caster, cueSource, new LightningCue(source, at, Faded: false, SpellColours.Lightning, LightningCue.Targeted));
            if (deflected || !(range > 0.05f) || target is Barrier || target is IShield)
            {
                return;
            }
            var way = Flat(at - source);
            if (Next(manager, at, way, range, hits) is not { } next)
            {
                Raise(manager, caster, cueSource, new LightningCue(at, at + way * range, Faded: true, SpellColours.Lightning));
                return;
            }
            range -= Vector3.Distance(at, next.Position);
            (source, target) = (target.Position, next);
        }
    }

    /// <summary>A blocking character takes the bolt into its weapon (as plain Lightning); anything else takes the damage. True when it is wholly deflected.</summary>
    private static bool Hit(Character? caster, Vector3 source, Entity target, IReadOnlyList<DamageEntry> damage)
    {
        if (target is Character { IsBlocking: true } blocker && blocker.ItemIn(0) is { } weapon)
        {
            weapon.Imbued = weapon.Imbued.Add(Spell.Of(Elements.Lightning), lightningPrecedence: true).Queue;
            return true;
        }
        return target.Damage(damage, caster, source) == DamageResult.Deflected;
    }

    /// <summary>
    /// The nearest that a chained bolt may strike: a character that is not ethereal, a solid
    /// barrier, a missile or another damageable thing, not frozen, not struck lately; never a mine,
    /// a shield or an elemental wall.
    /// </summary>
    private static Entity? Next(EntityManager manager, Vector3 at, Vector3 way, float range, IHitList hits)
    {
        Entity? best = null;
        var bestDistance = float.MaxValue;
        foreach (var entity in manager.Query(at, range, ignoreProtected: true))
        {
            var allowed = entity switch
            {
                Character c => !c.IsEthereal && !c.Statuses.Has(StatusEffects.Frozen),
                Barrier b => b.IsSolid,
                SpellMine or IShield => false,
                _ => true,
            };
            if (!allowed || !entity.IsDamageable || entity.IsDead || hits.Contains(entity))
            {
                continue;
            }
            var distance = Vector3.DistanceSquared(at, entity.Position);
            var toward = Flat(entity.Position - at);
            var angle = way == Vector3.Zero || toward == Vector3.Zero ? 0 : MathF.Acos(Math.Clamp(Vector3.Dot(way, toward), -1, 1));
            if (distance < bestDistance && angle <= ChainAngle)
            {
                (best, bestDistance) = (entity, distance);
            }
        }
        return best;
    }

    private static Vector3 Flat(Vector3 v) => v with { Y = 0 } is var flat && flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : Vector3.Zero;

    private static Vector3 Nearest(IShield shield, Vector3 source) =>
        shield.Position + (source - shield.Position is var off && off.LengthSquared() > 1e-12f ? Vector3.Normalize(off) * shield.Radius : Vector3.Zero);

    private static void Raise(EntityManager manager, Character? caster, Entity cueSource, Animation.AnimationCue cue)
    {
        if (caster is not null)
        {
            caster.RaiseCue(cue);
        }
        else
        {
            manager.RaiseCue(cueSource, cue);
        }
    }
}
