using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Items;

/// <summary>What a gun fires (docs/engine/ENTITIES_AI.md §8.1.3).</summary>
/// <param name="Rate">Rounds per second.</param>
/// <param name="TracerSprite">The sprite of every fifth tracer, or −1.</param>
/// <param name="PlainSprite">The sprite of the other tracers, or −1.</param>
public sealed record GunTemplate(float Range, int Clip, int Rate, float Accuracy, float TracerVelocity, IReadOnlyList<EventGroup> Events,
    string Sound = "", string Muzzle = "", string Shells = "", int TracerSprite = -1, int PlainSprite = -1)
{
    public void Validate()
    {
        foreach (var (value, name) in new[] { (Range, nameof(Range)), (Accuracy, nameof(Accuracy)), (TracerVelocity, nameof(TracerVelocity)) })
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");
            }
        }
        if (Range < 0 || TracerVelocity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Range), Range, "the range and tracer speed must not be negative");
        }
        if (Rate <= 0 && Clip > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Rate), Rate, "a gun with rounds needs a rate");
        }
        ArgumentNullException.ThrowIfNull(Events);
        foreach (var group in Events)
        {
            ArgumentNullException.ThrowIfNull(group, nameof(Events));
            group.Validate();
        }
    }
}

/// <summary>A gun's firing (docs/engine/ENTITIES_AI.md §8.1.3).</summary>
public static class Guns
{
    /// <summary>How close a shot must pass a target's surface.</summary>
    public const float ShotRadius = 0.05f;

    /// <summary>Every this many shots, one is drawn with the tracer sprite.</summary>
    public const int TracerEvery = 5;

    /// <summary>How many times a shot may be deflected (the original has no limit; a stack overflow is not worth matching).</summary>
    public const int MaxBounces = 16;

    public static bool IsGun(WeaponClass weaponClass) =>
        weaponClass is WeaponClass.Handgun or WeaponClass.Rifle or WeaponClass.Machinegun or WeaponClass.Heavy;

    /// <summary>The action's first frame: a full clip at the action's share of the accuracy.</summary>
    public static void Begin(Character owner, Item item, float accuracy)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        item.ClearHitList();
        if (item.Template.Gun is not { } gun || !item.GunEvents.Any)
        {
            return;
        }
        item.GunAccuracy = gun.Accuracy * accuracy;
        item.Rounds = gun.Clip;
        if (item.ShotTimer <= 0)
        {
            item.ShotTimer = float.Epsilon;
            owner.RaiseCue(new GunfireCue(item, Firing: true));
        }
        item.Firing = true;
        item.Cooldown = item.Template.CooldownTime;
    }

    /// <summary>The action ends: a non-player's gun stops; a player's only if it is a rifle, a handgun, or a machinegun let go.</summary>
    public static void End(Character owner, Item item)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        var stops = !owner.IsPlayer || item.WeaponClass is WeaponClass.Rifle or WeaponClass.Handgun
            || (item.WeaponClass == WeaponClass.Machinegun && !owner.Attacking);
        if (stops)
        {
            Stop(owner, item);
        }
    }

    /// <summary>The burst ends at once (the action's end, or the gun leaving the hand).</summary>
    internal static void Stop(Character owner, Item item)
    {
        if (item.Firing || item.ShotTimer > 0)
        {
            (item.Rounds, item.ShotTimer, item.Firing) = (0, 0, false);
            owner.RaiseCue(new GunfireCue(item, Firing: false));
        }
    }

    /// <summary>The holder's frame: the shot timer runs, and a shot goes when it is due.</summary>
    internal static void Update(Character owner, Item item, float dt)
    {
        if (item.Template.Gun is not { } gun || !IsGun(item.WeaponClass) || item.ShotTimer <= 0 || item.Rounds <= 0)
        {
            return;
        }
        item.ShotTimer -= dt;
        if (item.ShotTimer > 0)
        {
            return;
        }
        if (item.Firing)
        {
            item.ShotTimer += 1f / gun.Rate;
            item.Rounds--;
        }
        owner.RaiseCue(new MuzzleCue(item));
        if (!owner.IsPlaying("pickup_weapon"))
        {
            Fire(owner, item, gun, item.Tip, Aim(owner, item), 0, gun.Range, owner, 0);
            item.Shots++;
        }
    }

    /// <summary>From attach1 to attach0, turned flat by ±s/2 and tipped by ±2s, where s = (1 − accuracy) × 0.2.</summary>
    private static Vector3 Aim(Character owner, Item item)
    {
        var back = ((item.Template.Attach1 ?? Matrix4x4.Identity) * item.Transform).Translation;
        var along = item.Tip - back;
        along = along.LengthSquared() > 1e-12f ? Vector3.Normalize(along) : owner.Body.Facing;
        var spread = (1 - item.GunAccuracy) * 0.2f;
        var heading = MathF.Atan2(along.Z, along.X) + Between(owner, -spread * 0.5f, spread * 0.5f);
        var aim = new Vector3(MathF.Cos(heading), along.Y + Between(owner, -spread * 2, spread * 2), MathF.Sin(heading));
        return Vector3.Normalize(aim);
    }

    private static float Between(Character owner, float low, float high) => low + (high - low) * (float)owner.NextRandom();

    /// <summary>One shot: the level shortens it, the nearest target it grazes takes it (or a deflector sends it on).</summary>
    private static void Fire(Character owner, Item item, GunTemplate gun, Vector3 from, Vector3 direction, float delay, float range, Entity ignored, int bounces)
    {
        var shot = new Segment(from, direction * range);
        var level = owner.World.IsDisposed ? null : owner.World.CastSegment(from, from + shot.Delta, levelOnly: true);
        if (level is { } hit)
        {
            shot = shot with { Delta = shot.Delta * hit.Fraction };
        }
        var (target, end) = Nearest(owner, shot, range, ignored);
        if (target is Character deflector && Deflects(deflector))
        {
            var travelled = Vector3.Distance(from, end);
            var normal = deflector.EntryNormal(shot, end) with { Y = 0 };
            var bounced = normal.LengthSquared() < 1e-6f ? direction : Vector3.Reflect(direction, Vector3.Normalize(normal));
            var left = range - travelled;
            if (bounces < MaxBounces && left > 1e-3f)
            {
                var later = gun.TracerVelocity > 0 ? (travelled - 2) / gun.TracerVelocity : 0;
                Fire(owner, item, gun, end, bounced, later, left, deflector, bounces + 1);
            }
        }
        else if (target is not null)
        {
            item.GunEvents.Fire(new EventContext(EventTrigger.Hit, Position: end), (e, at) => ItemEventRunner.Run(e, at, owner, item, target));
        }
        else if (level is { } wall)
        {
            end = wall.Position;
            item.GunEvents.Fire(new EventContext(EventTrigger.Collision, Position: end), (e, at) => ItemEventRunner.Run(e, at, owner, item, null));
        }
        else
        {
            end = shot.Origin + shot.Delta;
        }
        var sprite = item.Shots % TracerEvery == 0 && gun.TracerSprite >= 0 ? gun.TracerSprite : gun.PlainSprite;
        if (sprite >= 0)
        {
            owner.RaiseCue(new TracerCue(from, end, gun.TracerVelocity, sprite, delay));
        }
    }

    /// <summary>A character's own Deflect aura turns shots; a held item's does not, as the original.</summary>
    private static bool Deflects(Character character)
    {
        for (var i = 0; i < character.Auras.Count; i++)
        {
            if (character.Auras[i].Aura is DeflectAura)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The damageable thing around the shot's middle whose surface the shot passes nearest its start.</summary>
    private static (Entity? Target, Vector3 End) Nearest(Character owner, Segment shot, float range, Entity ignored)
    {
        var (target, end, best) = ((Entity?)null, shot.Origin + shot.Delta, float.MaxValue);
        if (owner.Manager is not { } manager)
        {
            return (null, end);
        }
        foreach (var entity in manager.Query(shot.At(0.5f), MathF.Max(0, range) * 0.5f, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, ignored) || !entity.IsDamageable || !entity.SegmentIntersect(shot, ShotRadius, out var at))
            {
                continue;
            }
            var distance = Vector3.DistanceSquared(shot.Origin, at);
            if (distance < best)
            {
                (target, end, best) = (entity, at, distance);
            }
        }
        return (target, end);
    }
}
