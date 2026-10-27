using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Rain: for 17.5 s everything in the level is soaked (MAGICKS.md §5.1). A sky magick: indoors it does nothing.</summary>
public sealed class Rain() : Magick(MagickType.Rain, GlobalClip)
{
    public override bool Execute(Character caster) => Weather.Start(caster, Elements.Water, Weather.RainTime, 0) is not null;
}

/// <summary>Blizzard: for 10 s, and a second as it fades, everything in the level is chilled (MAGICKS.md §5.2). A sky magick.</summary>
public sealed class Blizzard() : Magick(MagickType.Blizzard, GlobalClip)
{
    public override bool Execute(Character caster) => Weather.Start(caster, Elements.Cold, Weather.BlizzardTime, Weather.BlizzardFade) is not null;
}

/// <summary>Thunder Storm: rain, and lightning from the sky on the tallest thing near random spots around the caster (MAGICKS.md §5.3). A sky magick.</summary>
public sealed class Thunderstorm() : Magick(MagickType.ThunderS, GlobalClip)
{
    public override bool Execute(Character caster)
    {
        if (Weather.Start(caster, Elements.Water, Weather.RainTime, 0) is not { } rain)
        {
            return false;
        }
        rain.Storm();
        return true;
    }
}

/// <summary>
/// The weather a magick brings: every 0.25 s each damageable thing in the level (not a missile, not
/// under a shield, not immune) takes a status of its element at 0.25; a storm adds bolts. One of
/// each element at a time: casting it again starts its time over. A simulation object.
/// </summary>
public sealed class Weather : Entity, IMagickEffect
{
    public const float RainTime = 17.5f, BlizzardTime = 10, BlizzardFade = 1, Tick = 0.25f, Magnitude = 0.25f;

    /// <summary>A storm's bolts: the first after 2 s, then every 0.5 to 1 s, one or two within 20 of the caster, each on the tallest within 5.</summary>
    public const float FirstBolt = 2, Spread = 20, BoltReach = 5;

    public const string RainSound = "magick_rain", BlizzardSound = "magick_blizzard", StormSound = "magick_thunderstorm";

    private EntityManager? level;
    private float ttl;
    private float fade;
    private float tick;
    private float boltTimer = float.NaN;

    private Weather(Character caster, Elements element, float time, float fade) => (Caster, Element, ttl, this.fade) = (caster, element, time, fade);

    /// <summary>Who cast it last (a recast takes it over, as the original).</summary>
    public Character Caster { get; private set; }

    public Elements Element { get; }

    public bool IsStorm => !float.IsNaN(boltTimer);

    public override Vector3 Position => Caster.Position;

    public override float Radius => Spread;

    public override bool IsDead => ttl <= 0 && fade <= 0;

    public override bool IsRemovable => IsDead;

    protected internal override void OnAdded() => level = Manager;

    /// <summary>Its ambience stops when it goes.</summary>
    protected internal override void OnRemoved()
    {
        level?.RaiseCue(this, new SoundStopCue(Element == Elements.Cold ? BlizzardSound : RainSound));
        if (IsStorm)
        {
            level?.RaiseCue(this, new SoundStopCue(StormSound));
        }
    }

    /// <summary>The weather of the element, started or started over; null indoors or with no room.</summary>
    internal static Weather? Start(Character caster, Elements element, float time, float fade)
    {
        if (caster.Manager is not { } manager || manager.Indoors)
        {
            return null;                                                      // as the original, silently
        }
        caster.RaiseCue(new SoundCue(element == Elements.Cold ? BlizzardSound : RainSound, MagickEffects.SpellsBank, caster.Position, caster.Body.Facing));
        if (manager.Entities.OfType<Weather>().FirstOrDefault(w => w.Element == element && !w.IsDead) is { } running)
        {
            (running.ttl, running.fade, running.Caster) = (time, MathF.Max(running.fade, fade), caster);
            return running;
        }
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return null;
        }
        var weather = new Weather(caster, element, time, fade);
        manager.Add(weather);
        return weather;
    }

    /// <summary>Makes the rain a storm (bolts from 2 s on); a storm already raging keeps its pace.</summary>
    internal void Storm()
    {
        if (!IsStorm)
        {
            boltTimer = FirstBolt;
            Manager?.RaiseCue(this, new SoundCue(StormSound, MagickEffects.SpellsBank, Position, Vector3.UnitX));
        }
    }

    /// <summary>Nullify ends it.</summary>
    public void End() => (ttl, fade) = (0, 0);

    public override void Update(float dt)
    {
        ttl -= dt;
        if (ttl <= 0)
        {
            fade -= dt;
        }
        if (IsDead || Manager is not { } manager)
        {
            return;
        }
        tick -= dt;
        if (tick <= 0)
        {
            tick = Tick;
            Soak(manager);
            if (Element == Elements.Cold)
            {
                foreach (var liquid in manager.Liquids)
                {
                    liquid.FreezeAll(Magnitude);                              // a blizzard freezes every lake (OM-88)
                }
            }
        }
        if (IsStorm && ttl > 0)
        {
            boltTimer -= dt;
            if (boltTimer <= 0)
            {
                boltTimer = 0.5f + (float)Caster.NextRandom() * 0.5f;
                var count = (int)(Caster.NextRandom() * 2) + 1;
                for (var i = 0; i < count; i++)
                {
                    Bolt(manager);
                }
            }
        }
    }

    private void Soak(EntityManager manager)
    {
        var status = new DamageEntry(AttackProperties.Status, Element, 0, Magnitude);
        foreach (var entity in manager.Entities.ToList())
        {
            if (!entity.IsDamageable || entity is Missile || entity.IsDead || manager.IsProtectedByShield(entity, out _)
                || entity is Character character && character.Defences.ResistanceAgainst(Element, character.Statuses.Active) >= 1)
            {
                continue;
            }
            entity.Damage(status, Caster, default);
        }
    }

    /// <summary>A bolt near a random spot within 20 of the caster: the tallest within 5 (shields hide) is struck, or the bare spot.</summary>
    private void Bolt(EntityManager manager)
    {
        var distance = MathF.Sqrt((float)Caster.NextRandom()) * Spread;
        var angle = (float)Caster.NextRandom() * MathF.Tau;
        var spot = manager.Walkable(Caster.Position + new Vector3(MathF.Cos(angle) * distance, 0, MathF.Sin(angle) * distance), Navigation.NavMovement.All);
        Entity? target = null;
        var top = float.MinValue;
        foreach (var entity in manager.Query(spot, BoltReach, ignoreProtected: true, ignoreY: true))
        {
            if (entity.IsDamageable && entity is not (Missile or Character { IsEthereal: true }) && Thunderbolt.Top(entity) is var height && height > top)
            {
                (target, top) = (entity, height);
            }
        }
        if (target is not null)
        {
            spot = target.Position;
            target.Damage(Thunderbolt.Damage, Caster, spot + Vector3.UnitY * 20);
        }
        Thunderbolt.Strike(manager, Caster, spot, scorch: target is not IShield, shake: 1.2f);
    }
}
