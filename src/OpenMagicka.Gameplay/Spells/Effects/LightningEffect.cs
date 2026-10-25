using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A bolt for the renderer: from where it left to where it ended (a faded one, a miss, ends nowhere), its colour
/// and how long it shows (SPELLS.md §14: a quarter second; a targeted bolt 0.4 s, one from the sky 1 s).
/// </summary>
public sealed record LightningCue(Vector3 From, Vector3 To, bool Faded, Vector3 Colour, float Life = LightningCue.Short) : AnimationCue
{
    public const float Short = 0.25f, Targeted = 0.4f, FromTheSky = 1;
}

/// <summary>Lightning: bolts fired one after another along the facing or all around, chaining from target to target (docs/gameplay/SPELLS.md §5.5).</summary>
public sealed class LightningEffect : SpellEffect
{
    private readonly HashSet<Entity> hitList = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<DamageEntry> damage = [];
    private int left;
    private float interval;
    private float timer;
    private float range;
    private bool allAround;

    /// <summary>Bolts still to fire.</summary>
    public int BoltsLeft => left;

    protected override void Force(Character caster)
    {
        FromStaff = false;
        Begin(caster, CastType.Force);
    }

    protected override void Weapon(Character caster)
    {
        FromStaff = false;
        Begin(caster, CastType.Weapon);
    }

    protected override void Area(Character caster)
    {
        FromStaff = false;
        Begin(caster, CastType.Area);
    }

    protected override void Self(Character caster)
    {
        left = 1;                                                             // a self-cast fires no bolt: it lasts by the base rules
        caster.Damage(SpellDamage.For(Spell, SpellType.Lightning, CastType.Self), caster, caster.Position);
    }

    private void Begin(Character caster, CastType cast)
    {
        var strike = LightningRules.For(Spell, cast, wet: false);
        range = caster.Modify(Modified.SpellRange, strike.Range);
        allAround = strike.Pattern == LightningPattern.Circle;
        left = strike.BoltsPerSecond;
        interval = 1f / strike.BoltsPerSecond;
        damage = SpellDamage.For(Spell, SpellType.Lightning, cast is CastType.Weapon ? CastType.Force : cast)
            .Select(d => d with { Magnitude = d.Magnitude * strike.MagnitudeScale }).ToList();
    }

    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Lightning);
        if (!IsActive)
        {
            return false;
        }
        timer -= dt;
        if (timer <= 0 && left > 0)
        {
            if (CastType != CastType.Self)
            {
                Bolt(caster);                                                 // one a frame at most, the timer set afresh, as the original
            }
        }
        else if (left <= 0)
        {
            Stop(caster);
            return false;
        }
        return base.Update(caster, dt, out _);
    }

    private void Bolt(Character caster)
    {
        hitList.Clear();
        hitList.Add(caster);
        timer = interval;
        left--;
        var direction = caster.Body.Facing;
        if (caster.Statuses.Has(StatusEffects.Wet) && !caster.HasPassiveAbility(PassiveAbility.WetLightning))
        {
            (range, allAround) = (2, true);
        }
        Vector3 source;
        if (allAround)
        {
            direction = Vector3.Transform(direction, Matrix4x4.CreateRotationY(left % 8 * MathF.PI / 4));
            source = caster.CastSource;
        }
        else
        {
            source = CastType == CastType.Weapon ? caster.WeaponSource : caster.CastSource;
        }
        LightningBolts.Strike(caster, source, direction, range, damage, Spell, hitList);
    }
}

/// <summary>How a spell's bolt finds its target and chains on (docs/gameplay/SPELLS.md §5.5).</summary>
public static partial class LightningBolts
{
    /// <summary>A bolt from a source along a direction; it chains from each target it hits, with the same range.</summary>
    public static void Strike(Character caster, Vector3 source, Vector3 direction, float range, IReadOnlyList<DamageEntry> damage, Spell spell, ISet<Entity> hitList)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(hitList);
        for (var link = 0; link < EntityManager.Capacity && range > 0.05f && caster.Manager is { } manager; link++)
        {
            var (target, at) = Pick(manager, source, direction, range, hitList);
            if (target is null)
            {
                var wobble = ((float)caster.NextRandom() - 0.5f) * 0.5f * MathF.PI / 4;
                var faded = Vector3.Transform(direction, Matrix4x4.CreateRotationY(wobble));
                var end = source + faded * range;
                if (caster.World.CastSegment(source, end, levelOnly: true) is { } wall)
                {
                    end = wall.Position;
                }
                caster.RaiseCue(new LightningCue(source, end, Faded: true, SpellColours.Of(spell)));
                hitList.Clear();
                return;
            }
            hitList.Add(target);
            var deflected = false;
            if (target is Character { IsBlocking: true, BlockSlot: >= 0 } blocker && blocker.ItemIn(0) is { } weapon)
            {
                weapon.Imbued = weapon.Imbued.Add(spell.Normalized(), lightningPrecedence: true).Queue;
                at = weapon.Position;
                deflected = true;
            }
            else
            {
                deflected = target.Damage(damage, caster, source) == DamageResult.Deflected;   // a wholly deflected bolt goes no further
            }
            caster.RaiseCue(new LightningCue(source, at, Faded: false, SpellColours.Of(spell)));
            if (deflected || target is IShield)
            {
                return;
            }
            var towards = at - source;
            direction = towards.LengthSquared() > 1e-12f ? Vector3.Normalize(towards) : direction;
            source = at;
        }
    }

    /// <summary>
    /// The bolt's target, in query order: one within ±90° (flat) whose angle and distance both beat
    /// the best so far, or one on the source; shields are aimed at their nearest point.
    /// </summary>
    private static (Entity? Target, Vector3 At) Pick(EntityManager manager, Vector3 source, Vector3 direction, float range, ISet<Entity> hitList)
    {
        Entity? best = null;
        var at = source;
        var (bestDistance, bestAngle) = (float.MaxValue, float.MaxValue);
        var flat = direction with { Y = 0 };
        flat = flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : Vector3.Zero;
        foreach (var entity in manager.Query(source, range, ignoreProtected: true))
        {
            if (!entity.IsDamageable || entity.IsDead || hitList.Contains(entity) || entity is Character { IsEthereal: true }
                || entity is Character c && c.Statuses.Has(StatusEffects.Frozen))
            {
                continue;
            }
            var position = entity.Position;
            if (entity is IShield shield)
            {
                position = shield.Position + (source - shield.Position is var off && off.LengthSquared() > 1e-12f ? Vector3.Normalize(off) * shield.Radius : Vector3.Zero);
            }
            else if (manager.Shields.Any(s => s.Blocks(source, position, 0, out _)))
            {
                continue;
            }
            var distance = Vector3.DistanceSquared(source, position);
            var toward = (position - source) with { Y = 0 };
            var angle = toward.LengthSquared() > 1e-12f && flat != Vector3.Zero
                ? MathF.Acos(Math.Clamp(Vector3.Dot(flat, Vector3.Normalize(toward)), -1, 1))
                : 0;
            if ((angle <= MathF.PI / 2 && angle < bestAngle && distance < bestDistance) || distance < float.Epsilon)
            {
                (best, at, bestDistance, bestAngle) = (entity, position, distance, angle);
            }
        }
        return (best, at);
    }
}
