using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>A spray: a cone of the spray elements, a blast around the caster, a self-cast or a weapon arc (docs/gameplay/SPELLS.md §5.2).</summary>
public sealed class SprayEffect : SpellEffect
{
    /// <summary>A spray hits each target at most this often.</summary>
    public const float HitInterval = CastRules.TickInterval;

    /// <summary>How far above or below the spray reaches.</summary>
    public const float HeightTolerance = 5;

    /// <summary>The effects a Force spray, a weapon spray and a self-cast show for each element they have one for.</summary>
    private static readonly IReadOnlyDictionary<Elements, string> ForceEffects = new Dictionary<Elements, string>
    {
        [Elements.Water] = "spray_water1", [Elements.Cold] = "spray_cold1", [Elements.Fire] = "spray_fire1",
        [Elements.Lightning] = "spray_lightning1", [Elements.Steam] = "spray_steam1", [Elements.Poison] = "spray_poison1",
    };

    private static readonly IReadOnlyDictionary<Elements, string> WeaponEffects = new Dictionary<Elements, string>
    {
        [Elements.Water] = "weapon_spray_water", [Elements.Cold] = "weapon_spray_cold", [Elements.Fire] = "weapon_spray_fire",
        [Elements.Steam] = "weapon_spray_steam", [Elements.Poison] = "weapon_spray_poison",
    };

    /// <summary>As the original's table: shield shows fire's, poison steam's.</summary>
    public static readonly IReadOnlyDictionary<Elements, string> SelfEffects = new Dictionary<Elements, string>
    {
        [Elements.Earth] = "self_earth", [Elements.Water] = "self_water", [Elements.Cold] = "self_cold", [Elements.Fire] = "self_fire",
        [Elements.Lightning] = "self_lightning", [Elements.Arcane] = "self_arcane", [Elements.Life] = "self_life", [Elements.Shield] = "self_fire",
        [Elements.Ice] = "self_ice", [Elements.Steam] = "self_steam", [Elements.Poison] = "self_steam",
    };

    private readonly Dictionary<Entity, float> hit = [];
    private IReadOnlyList<DamageEntry> damage = [];
    private float range;
    private float buildUp;
    private float timeLeft;

    /// <summary>Whom the spray hit within the last <see cref="HitInterval"/>, and the time each still waits.</summary>
    public IReadOnlyDictionary<Entity, float> HitList => hit;

    protected override void Force(Character caster)
    {
        FromStaff = false;                                                    // outside panic, a Force spray follows the facing
        (timeLeft, MaxTime, range) = (SprayRules.MaxDuration, SprayRules.MaxDuration, SprayRules.Range(Spell));
        damage = SpellDamage.For(Spell, SpellType.Spray, CastType.Force);
        PlaySounds(caster, SpellType.Spray, CastType.Force);
        Show(caster, ForceEffects, caster.CastSource);
        GreaseBurn(caster);
    }

    protected override void Area(Character caster)
    {
        timeLeft = 0;                                                         // the blast is all of it: the effect ends at once
        damage = SpellDamage.For(Spell, SpellType.Spray, CastType.Area);
        PlaySounds(caster, Spell.Type, CastType.Area, playOut: true);     // the effect ends this very step: its blast is heard out
        if (caster.Manager is { } manager)
        {
            Blasts.Full(manager, caster.World, caster, caster, SprayRules.AreaRadius(Spell), caster.Position, damage);
        }
        GreaseBurn(caster);
    }

    protected override void Self(Character caster)
    {
        timeLeft = 1;                                                         // ends with its 0.2 s maximum
        damage = SpellDamage.For(Spell, SpellType.Spray, CastType.Self);
        Show(caster, SelfEffects, caster.Position);
        caster.Damage(damage, caster, caster.Position);
    }

    protected override void Weapon(Character caster)
    {
        (MinTime, MaxTime) = CastRules.EffectLifetime(CastType.Self);       // as the original: a weapon spray lives as a self-cast
        timeLeft = MaxTime;
        damage = SpellDamage.For(Spell, SpellType.Spray, CastType.Weapon);
        var at = caster.WeaponSource;
        Show(caster, WeaponEffects, at);
        if (caster.Manager is { } manager)
        {
            Blasts.Arc(manager, caster, caster, at, caster.Body.Facing, SprayRules.WeaponRadius, SprayRules.WeaponHalfAngle, damage);
        }
        GreaseBurn(caster);
    }

    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Spray);
        if (!IsActive)
        {
            return false;                                                     // a stopped spray sprays no more
        }
        foreach (var entity in hit.Keys.ToList())
        {
            hit[entity] -= dt;                                                // a float countdown, as the original's hit list
            if (hit[entity] <= 0)
            {
                hit.Remove(entity);
            }
        }
        if (CastType == CastType.Force)
        {
            timeLeft -= dt;
            buildUp = MathF.Min(1, buildUp + dt * SprayRules.BuildUpPerSecond);
            if (timeLeft > 0)
            {
                Spray(caster, range * buildUp);
            }
        }
        if (timeLeft <= 0 || MaxTime <= 0)
        {
            Stop(caster);
            return false;
        }
        return base.Update(caster, dt, out _);
    }

    /// <summary>One frame of a Force spray over the frame's range.</summary>
    private void Spray(Character caster, float frameRange)
    {
        if (caster.Manager is not { } manager || !(frameRange > 0))
        {
            return;
        }
        var origin = caster.CastSource;
        var direction = caster.Body.Facing;
        var end = origin + direction * frameRange;
        var free = 1f;
        if (caster.World.CastSegment(origin, end, levelOnly: true) is { } wall)
        {
            free *= wall.Fraction;
            end = origin + (end - origin) * wall.Fraction;
        }
        foreach (var shield in manager.Shields)
        {
            if (shield.Blocks(origin, end, 0, out var at) && Vector3.Distance(origin, end) > 0)
            {
                var fraction = Vector3.Distance(origin, at) / Vector3.Distance(origin, end);
                free *= fraction;
                end = origin + (end - origin) * fraction;
            }
        }
        var reach = SprayRules.Reach(frameRange, free);
        var tick = damage.Select(d => d with { Magnitude = d.Magnitude * CastRules.TickMagnitude }).ToList();
        foreach (var entity in manager.Query(caster.Position, frameRange, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, caster) || !entity.IsDamageable || hit.ContainsKey(entity)
                || !entity.ArcIntersect(origin, direction, entity is IShield ? frameRange : reach, SprayRules.HalfAngle, HeightTolerance, out _))
            {
                continue;
            }
            entity.Damage(tick, caster, origin);
            hit[entity] = HitInterval;
        }
    }

    /// <summary>A greased caster takes the first fire entry among the first four, from its cast source.</summary>
    private void GreaseBurn(Character caster)
    {
        if (caster.Statuses.Has(StatusEffects.Greased) && damage.Take(4).FirstOrDefault(d => (d.Element & Elements.Fire) != 0) is { Element: not Elements.None } fire)
        {
            caster.Damage(fire, caster, caster.CastSource);
        }
    }

    /// <summary>Each element with a whole magnitude of 1 or more starts its effect, until the spray ends.</summary>
    private void Show(Character caster, IReadOnlyDictionary<Elements, string> effects, Vector3 at)
    {
        foreach (var (element, name) in effects.Where(e => (int)Spell[e.Key] > 0))
        {
            caster.RaiseCue(new ItemEffectCue(name, at, caster.Body.Facing, Follow: true, this));
        }
    }

    protected override void End(Character caster)
    {
        hit.Clear();
        caster.RaiseCue(new ItemEffectEndCue(this));
    }
}
