using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>A push: a cone or a ring that grows for 0.25 s and pushes what it reaches once (docs/gameplay/SPELLS.md §5.1).</summary>
public sealed class PushEffect : SpellEffect
{
    /// <summary>A push lives at least this long, whatever its cast.</summary>
    public const float MinimumTime = 0.4f;

    /// <summary>How high a Force push reaches above or below the caster.</summary>
    public const float HeightTolerance = 4;

    /// <summary>How far an impulse carries, at full strength.</summary>
    public const float ImpulseDistance = 1.5f;

    public const string ForceEffect = "push_force", AreaEffect = "push_area";
    public const string ForceSound = "spell_push_force", AreaSound = "spell_push_area";

    private const int SpellsBank = 0x10;

    private readonly List<Entity> hit = [];
    private PushWave? wave;
    private float waveLeft;
    private float elevation;
    private Vector3 direction;

    /// <summary>Whom the wave reached, in order.</summary>
    public IReadOnlyList<Entity> HitList => hit;

    protected override void Force(Character caster) => Begin(caster, ForceEffect, ForceSound, caster.CastSource);

    protected override void Area(Character caster) => Begin(caster, AreaEffect, AreaSound, caster.Position);

    protected override void Self(Character caster) => MinTime = MinimumTime;

    protected override void Weapon(Character caster) => MinTime = MinimumTime;

    private void Begin(Character caster, string effect, string sound, Vector3 at)
    {
        MinTime = MinimumTime;
        var power = caster.SpellPower;
        wave = PushRules.For(CastType, power, caster.AdditionalPushRadius);
        waveLeft = wave!.Duration;
        elevation = PushRules.LaunchElevation(power);
        direction = caster.Body.Facing;
        caster.RaiseCue(new ItemEffectCue(effect, at, direction, Follow: false, this));
        caster.RaiseCue(new SoundCue(sound, SpellsBank, caster.Position, direction, power * 3));
        caster.SpellPower = 0;
    }

    /// <summary>The wave grows from a quarter of its radius to all of it; the push ends once the wave and its minimum time are over.</summary>
    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Push);
        waveLeft -= dt;
        MinTime -= dt;
        if (wave is { } push && waveLeft > 0)
        {
            Reach(caster, push);
        }
        else if (MinTime < 0)
        {
            Stop(caster);
            return false;
        }
        return true;
    }

    private void Reach(Character caster, PushWave push)
    {
        if (caster.Manager is not { } manager)
        {
            return;
        }
        var radius = push.CoveredRadius(push.Duration - waveLeft);
        var origin = caster.Position;
        var damage = new DamageEntry(AttackProperties.Pushed, Elements.Earth, push.Strength, PushWave.Magnitude);
        foreach (var entity in manager.Query(origin, radius, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, caster) || hit.Contains(entity) || entity.IsDead
                || (push.HalfAngle < MathF.PI && !entity.ArcIntersect(origin, direction, radius, push.HalfAngle, HeightTolerance, out _))
                || caster.World.CastSegment(origin, entity.Position, levelOnly: true) is not null)
            {
                continue;
            }
            if ((entity.Position - origin).Length() <= float.Epsilon)
            {
                continue;
            }
            hit.Add(entity);
            if (entity.IsDamageable && entity is not Missile)
            {
                entity.Damage(damage, caster, origin);
            }
            else
            {
                entity.Impulse(origin, elevation, push.Strength, ImpulseDistance);   // a missile is thrown, not hurt
            }
        }
    }
}
