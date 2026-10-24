using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Chain Lightning: one big bolt from the staff along the facing, chaining on (MAGICKS.md §4.1).</summary>
public sealed class ChainLightning() : Magick(MagickType.ChainLightning, DirectClip)
{
    public const float Range = 15, Amount = 850;

    public static readonly IReadOnlyList<DamageEntry> Damage = [new(AttackProperties.Damage, Elements.Lightning, Amount, 1)];

    public override bool Execute(Character caster)
    {
        var hits = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        if (!caster.Statuses.Has(StatusEffects.Wet))
        {
            hits.Add(caster);                                                 // a wet caster may be struck by its own bolt
        }
        LightningBolts.Strike(caster, caster.CastSource, caster.Body.Facing, Range, Damage, Spell.Of(Elements.Lightning), hits);
        Sound(caster, Thunderbolt.BoltSound);
        caster.RaiseCue(new CameraShakeCue(caster.Position, 1.5f, 0.333f));
        return true;
    }
}

/// <summary>
/// Thunder Bolt: lightning from the sky on the tallest thing within 8 of a point 8 ahead (MAGICKS.md
/// §4.2); a caster under a shield strikes its shield. It fails indoors.
/// </summary>
public sealed class Thunderbolt() : Magick(MagickType.ThunderB, DirectClip)
{
    public const float Range = 16, Amount = 5000;

    public const string BoltSound = "magick_thunderbolt", BoltEffect = "magick_thunderbolt", Scorch = "scorched";

    public static readonly IReadOnlyList<DamageEntry> Damage = [new(AttackProperties.Knockback | AttackProperties.Damage, Elements.Lightning, Amount, 1)];

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Indoors)
        {
            return Fail(caster);
        }
        var centre = (caster.Position with { Y = 0 }) + caster.Body.Facing * (Range / 2);
        Entity? target = manager.IsProtectedByShield(caster, out var shield) && shield is Entity cover ? cover : Tallest(manager, caster, centre, Range / 2);
        Vector3 at;
        if (target is not null)
        {
            at = target.Position;
            target.Damage(Damage, caster, centre);
        }
        else
        {
            at = centre + new Vector3(((float)caster.NextRandom() - 0.5f) * Range / 2, caster.Position.Y, ((float)caster.NextRandom() - 0.5f) * Range / 2);
        }
        Strike(manager, caster, at, scorch: target is not IShield);
        return true;
    }

    /// <summary>Of the damageable things within the radius (on the flat), the one whose top is highest; never the caster, a missile, an ethereal character or one behind a shield.</summary>
    public static Entity? Tallest(EntityManager manager, Entity? caster, Vector3 centre, float radius)
    {
        Entity? best = null;
        var top = float.MinValue;
        foreach (var entity in manager.Query(centre, radius, ignoreY: true))
        {
            if (ReferenceEquals(entity, caster) || !entity.IsDamageable || entity is Missile or Character { IsEthereal: true } || manager.IsProtectedByShield(entity, out _))
            {
                continue;
            }
            var height = Top(entity);
            if (height > top)
            {
                (best, top) = (entity, height);
            }
        }
        return best;
    }

    /// <summary>The bolt's looks and sounds at a point: the bolt from 40 above, its flash, a scorch on the floor, a shake.</summary>
    internal static void Strike(EntityManager manager, Entity source, Vector3 at, bool scorch, float shake = 1.5f)
    {
        manager.RaiseCue(source, new LightningCue(at + Vector3.UnitY * 40, at, Faded: false));
        manager.RaiseCue(source, new WorldEffectCue(BoltEffect, at, Vector3.UnitX));
        if (scorch && source is Character { World: var world } && world.CastSegment(at + Vector3.UnitY, at - Vector3.UnitY * 9, levelOnly: true) is { } floor)
        {
            manager.RaiseCue(source, new DecalCue(Scorch, floor.Position, floor.Normal, Vector3.UnitX, 4));
        }
        manager.RaiseCue(source, new SoundCue(BoltSound, MagickEffects.SpellsBank, at, Vector3.UnitX));
        manager.RaiseCue(source, new CameraShakeCue(at, shake, 0.333f));
    }

    /// <summary>The top of an entity: a character's capsule, else its sphere.</summary>
    internal static float Top(Entity entity) =>
        entity is Character character ? character.Position.Y + character.Template.Length / 2 + character.Radius : entity.Position.Y + entity.Radius;
}

/// <summary>
/// Crash to Desktop: a random character within 20 (shields hide), the caster too, vanishes on the
/// spot (MAGICKS.md §4.3). Bosses (over 100 000 hit points) and those only a script may kill are
/// spared; with nobody left, it fails.
/// </summary>
public sealed class CrashToDesktop() : Magick(MagickType.CTD, SweepClip)
{
    public const float Range = 20, Unkillable = 100_000;

    public const string Effect = "magick_ctd";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return false;
        }
        var victims = manager.Query(caster.Position, Range, ignoreProtected: true, ignoreY: true).OfType<Character>()
            .Where(c => !c.CannotDieWithoutExplicitKill && c.MaxHitPoints <= Unkillable).ToList();
        if (victims.Count == 0)
        {
            return Fail(caster);
        }
        var victim = victims[(int)(caster.NextRandom() * victims.Count) % victims.Count];
        manager.RaiseCue(victim, new WorldEffectCue(Effect, victim.Position, Vector3.UnitY));
        manager.RaiseCue(victim, new SoundCue(Effect, MagickEffects.AdditionalBank, victim.Position, victim.Body.Facing));
        victim.Terminate();
        return true;
    }
}
