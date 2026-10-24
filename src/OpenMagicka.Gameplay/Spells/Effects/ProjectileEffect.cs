using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A projectile spell: a boulder or a volley of shards (Force, falling for Self), a ground blast
/// (Area), or an ice blade and an underground wave (Weapon) (docs/gameplay/SPELLS.md §5.3).
/// </summary>
public sealed class ProjectileEffect : SpellEffect
{
    /// <summary>A shot leaving through a shield next to the caster is lost; the shield check is this thick plus 0.075 per Earth.</summary>
    public const float ShieldMargin = 0.1f;

    /// <summary>A Self volley falls from a circle of this radius, this high above the cast source; the last from <see cref="LastHeight"/>.</summary>
    public const float FallRadius = 6, FallHeight = 10, LastHeight = 8;

    /// <summary>An area cast's beat (only its first frame matters).</summary>
    public const float AreaInterval = 0.25f;

    /// <summary>An area cast's sound per element, as the original's table: shield has none, poison plays steam's.</summary>
    private static readonly IReadOnlyDictionary<Elements, string> AreaSounds = new Dictionary<Elements, string>
    {
        [Elements.Earth] = "spell_earth_area", [Elements.Water] = "spell_water_area", [Elements.Cold] = "spell_cold_area",
        [Elements.Fire] = "spell_fire_area", [Elements.Lightning] = "spell_lightning_area", [Elements.Arcane] = "spell_arcane_area",
        [Elements.Life] = "spell_life_area", [Elements.Ice] = "spell_ice_area", [Elements.Steam] = "spell_steam_area", [Elements.Poison] = "spell_steam_area",
    };

    private int left;
    private int total;
    private float charge;
    private float speed;
    private float splash;
    private float interval;
    private float timer;
    private IceBladeStrike? blade;
    private bool waveSent;

    /// <summary>Shots still to fire.</summary>
    public int ShotsLeft => Math.Max(left, 0);

    /// <summary>As the original, no projectile cast stops for a released button: a weapon cast lasts until its cast state ends it.</summary>
    protected override bool IsContinuous => false;

    protected override void Force(Character caster)
    {
        charge = caster.IsPlayer ? caster.SpellPower : CastRules.NonPlayerProjectileCharge;
        Volley(caster);
    }

    protected override void Self(Character caster)
    {
        charge = CastRules.NonPlayerProjectileCharge;
        Volley(caster);
    }

    protected override void Area(Character caster)
    {
        interval = AreaInterval;
        foreach (var element in AreaSounds.Keys.Where(e => (int)Spell[e] > 0))
        {
            caster.RaiseCue(new SoundCue(AreaSounds[element], SpellMissiles.SpellsBank, caster.Position, caster.Body.Facing, (int)Spell[element]));
        }
        if (caster.Manager is { } manager)
        {
            Blasts.Ground(manager, caster.World, caster, caster, ProjectileRules.AreaRadius(Spell), caster.Position, SpellDamage.For(Spell, SpellType.Projectile, CastType.Area));
        }
    }

    protected override void Weapon(Character caster)
    {
        FromStaff = false;
        interval = AreaInterval;
        if (Spell.Ice > 0 && caster.ItemIn(0) is not null)
        {
            var iceOnly = Spell with { Earth = 0 };
            blade = new IceBladeStrike(SpellDamage.For(iceOnly, SpellType.Projectile, CastType.Weapon), ProjectileRules.IceBladeLength(iceOnly));
        }
    }

    /// <summary>The volley's numbers: one boulder with Earth, else three shards per Ice, the speed by the spell power (then spent).</summary>
    private void Volley(Character caster)
    {
        var volley = ProjectileRules.Force(Spell, caster.SpellPower);
        (left, total, speed, interval) = (volley.Count, volley.Count, volley.Speed, volley.Interval);
        splash = volley.RemainderBlastRadius;
        var sound = (Spell.Element & Elements.Earth) != 0 && (Spell.Element & Elements.Ice) == 0 ? "spell_earth_force" : "spell_ice_force";
        caster.RaiseCue(new SoundCue(sound, SpellMissiles.SpellsBank, caster.Position, caster.Body.Facing, caster.SpellPower * 3));
        caster.SpellPower = 0;
    }

    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Projectile);
        if (!IsActive)
        {
            return false;
        }
        if (CastType is CastType.Force or CastType.Area or CastType.Self)
        {
            timer -= dt;
            while (timer <= 0 && interval > 0)
            {
                timer += interval;
                left--;
                if (left >= 0 && CastType != CastType.Area)
                {
                    Fire(caster);
                }
                if (left < 0)
                {
                    break;
                }
            }
            if (left <= 0)
            {
                Stop(caster);
                return false;
            }
        }
        return base.Update(caster, dt, out _);
    }

    private void Fire(Character caster)
    {
        var from = caster.CastSource;
        if (CastType == CastType.Force && caster.Manager is { } manager
            && manager.Shields.Any(s => s.Blocks(caster.Position, from, ShieldMargin + 0.075f * Spell.Earth, out _)))
        {
            return;                                                           // lost against a shield at the caster's hand
        }
        var direction = caster.Body.Facing;
        Vector3 velocity;
        if (left > 0)
        {
            if (CastType == CastType.Self)
            {
                var angle = (float)caster.NextRandom() * MathF.Tau;
                from += new Vector3(FallRadius * MathF.Cos(angle), FallHeight, FallRadius * MathF.Sin(angle));
                direction = Vector3.Normalize(caster.Position - from);
            }
            else
            {
                direction = Vector3.Transform(direction, Matrix4x4.CreateRotationY(((float)caster.NextRandom() - 0.5f) * MathF.PI / 2 * (1 - charge)));
            }
            velocity = direction * speed * ProjectileRules.OtherSpeedFactor((float)caster.NextRandom());
        }
        else
        {
            if (CastType == CastType.Self)
            {
                from.Y += LastHeight;
                direction = new Vector3(0.001f, -1, 0);
            }
            velocity = direction * speed;
        }
        SpellMissiles.Launch(caster, Spell, from, velocity, splash, total);
    }

    /// <summary>The weapon cast's clip is over: the ice blade strikes, and an Earth weapon sends its wave.</summary>
    internal override void AnimationEnded(Character caster)
    {
        if (CastType != CastType.Weapon || !IsActive || caster.IsDead)
        {
            return;
        }
        Strike(caster);
        if ((Spell.Element & Elements.Earth) != 0 && !waveSent)
        {
            waveSent = true;                                                  // once, though a looping clip would end its action again
            var earthOnly = Spell with { Ice = 0 };
            GroundWave.Launch(caster, caster.WeaponSource, caster.Body.Facing, ProjectileRules.WaveSpeed, ProjectileRules.WaveRange,
                SpellDamage.For(earthOnly, SpellType.Projectile, CastType.Weapon));
        }
    }

    protected override void End(Character caster) => Strike(caster);

    private void Strike(Character caster)
    {
        blade?.Strike(caster);
        blade = null;
    }

    /// <summary>The ice blade: when it goes, it strikes once along the facing for its length.</summary>
    private sealed record IceBladeStrike(IReadOnlyList<DamageEntry> Damage, float Length)
    {
        public void Strike(Character caster)
        {
            if (caster.Manager is not { } manager)
            {
                return;
            }
            var along = new Entities.Segment(caster.Position, caster.Body.Facing * Length);
            foreach (var entity in manager.Query(along.At(0.5f), Length / 2, ignoreProtected: true))
            {
                if (!ReferenceEquals(entity, caster) && entity.IsDamageable && entity.SegmentIntersect(along, 1, out var at))
                {
                    entity.Damage(Damage, caster, at);
                }
            }
        }
    }
}
