using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Items;

/// <summary>A held item's melee strike and its events (docs/engine/ENTITIES_AI.md §8.1.2, §8.2).</summary>
public static class Melee
{
    /// <summary>How far past the melee range the strike looks for things to hit.</summary>
    public const float Reach = 5;

    /// <summary>The animation a turned blow blends to.</summary>
    public const string Recoil = "attack_recoil";

    /// <summary>The sound of a blow that kills, at its victim.</summary>
    public const string DeathblowSound = "wep_deathblow";

    /// <summary>The results that let a blow carry on (knocked down is never reported, as the original).</summary>
    private const DamageResult CarriesOn = DamageResult.KnockedDown | DamageResult.KnockedBack | DamageResult.Pushed | DamageResult.Killed;

    /// <summary>A strike's first frame: nobody hit yet, and the Default events.</summary>
    public static void Begin(Character owner, Item item)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        item.ClearHitList();
        Fire(owner, item, new EventContext(EventTrigger.Default), target: null);
    }

    /// <summary>One frame of a strike.</summary>
    public static void Strike(Character owner, Item item, DamageTargets targets)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        var carryOn = item.HitList.Count == 0;                                 // as the original: this frame's, from no one hit yet
        if (!item.MeleeEvents.Any || (!item.Template.MeleeMultiHit && !carryOn) || item.ShieldGlance >= 0 || owner.Manager is not { } manager)
        {
            return;
        }
        item.Cooldown = item.Template.CooldownTime;
        var sweep = Segment.Between(item.Tip, item.LastTip);
        if (Glances(owner, item, sweep, manager))
        {
            return;
        }
        Gather(owner, item, sweep, targets, manager);
        StrikeLevel(owner, item, sweep);
        Blows(owner, item, carryOn);
    }

    /// <summary>The strike ends: the effects that followed the item stop.</summary>
    public static void End(Character owner, Item item)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        owner.RaiseCue(new ItemEffectEndCue(item));
    }

    /// <summary>A shield in the sweep, or between the holder and the tip, turns the blow.</summary>
    private static bool Glances(Character owner, Item item, Segment sweep, EntityManager manager)
    {
        var range = item.Template.MeleeRange;
        var shields = manager.Shields;
        for (var i = 0; i < shields.Count; i++)                                // the strike ends at once, before an event could add one
        {
            if (shields[i].Blocks(sweep.Origin, sweep.Origin + sweep.Delta, range, out _) || shields[i].Blocks(owner.Position, item.Tip, range, out _))
            {
                Fire(owner, item, new EventContext(EventTrigger.Hit), target: null);
                item.ShieldGlance = Item.GlanceTime;
                owner.GoToAnimation(Recoil, 0.1f);
                return true;
            }
        }
        return false;
    }

    /// <summary>The fair things the sweep passes within the melee range of join the hit list (a single-hit item takes one).</summary>
    private static void Gather(Character owner, Item item, Segment sweep, DamageTargets targets, EntityManager manager)
    {
        var range = item.Template.MeleeRange;
        foreach (var entity in manager.Query(item.Tip, range + Reach, ignoreProtected: true))
        {
            if (ReferenceEquals(entity, owner) || item.HitList.Contains(entity) || !Fair(owner, entity, targets) || !entity.SegmentIntersect(sweep, range))
            {
                continue;
            }
            item.AddHit(entity);
            if (!item.Template.MeleeMultiHit)
            {
                return;
            }
        }
    }

    /// <summary>The Hit events for each listed thing not struck yet; a deflected blow that does not carry on recoils.</summary>
    private static void Blows(Character owner, Item item, bool carryOn)
    {
        for (var i = item.NextToDamage; i < item.HitList.Count; i++)
        {
            var result = Fire(owner, item, new EventContext(EventTrigger.Hit), item.HitList[i]);
            if ((result & (DamageResult.Killed | DamageResult.OverKilled)) != 0 && item.HitList[i] is Entity victim)
            {
                owner.RaiseCue(new Animation.SoundCue(DeathblowSound, Animation.SoundBank.Weapons, victim.Position, owner.Body.Facing));
            }
            carryOn = carryOn && (result & CarriesOn) != 0;
            if (!carryOn && result.HasFlag(DamageResult.Deflected))
            {
                owner.GoToAnimation(Recoil, 0.1f);
            }
            item.NextToDamage = i + 1;
            if (!item.Template.MeleeMultiHit)
            {
                return;
            }
        }
    }

    /// <summary>The holder's target always; others by faction against the targets.</summary>
    private static bool Fair(Character owner, Entity entity, DamageTargets targets)
    {
        if (ReferenceEquals(entity, owner.Target))
        {
            return true;
        }
        if (entity is Character character)
        {
            var friendly = (character.Factions & owner.Factions) != 0;
            return targets.HasFlag(friendly ? DamageTargets.Friendly : DamageTargets.Enemy);
        }
        return targets.HasFlag(DamageTargets.NonCharacters);
    }

    /// <summary>At most every 0.25 s, the Collision groups fire when the sweep crosses the level.</summary>
    private static void StrikeLevel(Character owner, Item item, Segment sweep)
    {
        if (item.LevelGlance > 0 || !item.MeleeEvents.Has(EventTrigger.Collision) || owner.World.IsDisposed || sweep.Delta.LengthSquared() < 1e-8f)
        {
            return;
        }
        var (from, to) = (sweep.Origin, sweep.Origin + sweep.Delta);
        if (owner.World.CastSegment(from, to, levelOnly: true) is null && owner.World.CastSegment(to, from, levelOnly: true) is null)
        {
            return;                                                           // either way: our level faces are one-sided
        }
        Fire(owner, item, new EventContext(EventTrigger.Collision), target: null);
        owner.GoToAnimation(Recoil, 0.1f);
        item.LevelGlance = Item.GlanceTime;
    }

    /// <summary>The item's melee groups that the context meets; their events act for the holder.</summary>
    internal static DamageResult Fire(Character owner, Item item, in EventContext context, Entity? target) =>
        item.MeleeEvents.Fire(context, (e, at) => ItemEventRunner.Run(e, at, owner, item, target));
}
