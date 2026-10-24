using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>The missiles of projectile spells: boulders, ice boulders and shards (docs/gameplay/SPELLS.md §5.3).</summary>
public static class SpellMissiles
{
    public const int SpellsBank = 0x10;

    /// <summary>The elements in the original's order, for its per-element effect and sound tables.</summary>
    private static readonly Elements[] Order = [Elements.Earth, Elements.Water, Elements.Cold, Elements.Fire, Elements.Lightning, Elements.Arcane,
        Elements.Life, Elements.Shield, Elements.Ice, Elements.Steam, Elements.Poison];

    /// <summary>A missile's big trail: as the original's table, lightning has no 4, and shield shows fire's.</summary>
    private static string TrailBig(Elements e) => e switch
    {
        Elements.Lightning => "projectile_lightning",
        Elements.Shield => "projectile_fire4",
        _ => $"projectile_{Name(e)}4",
    };

    private static string HitBig(Elements e) => $"projectilehit_big_{Name(e == Elements.Shield ? Elements.Fire : e)}";

    private static string TrailSmall(Elements e) => $"projectiletrail_{Name(e == Elements.Shield ? Elements.Fire : e)}";

    private static string HitSmall(Elements e) => $"projectilehit_{Name(e == Elements.Shield ? Elements.Fire : e)}";

    private static string Sound(Elements e, string kind) => $"spell_{Name(e)}_{kind}";

    private static string Name(Elements e) => e.ToString().ToLowerInvariant();

    /// <summary>
    /// The event groups of a spell's missile: its start, its hit, its collision and its 10 s end.
    /// <paramref name="splash"/> is a boulder's blast radius; <paramref name="count"/> the shards in the volley.
    /// </summary>
    public static IReadOnlyList<EventGroup> Events(Spell spell, float splash, int count)
    {
        var damage = SpellDamage.For(spell, SpellType.Projectile, CastType.Force);
        var elements = spell.Element;
        var present = Order.Where(e => spell[e] > 0).ToList();
        var (start, hit, collision) = (new List<ItemEvent>(), new List<ItemEvent>(), new List<ItemEvent>());
        if ((elements & ElementSets.Physical) == ElementSets.Physical)
        {
            hit.AddRange(Damages(damage, 5));
            hit.Add(new RemoveEvent(0));
            collision.Add(new DecalEvent("crater", spell.Earth + spell.Ice - 1));
            collision.Add(new RemoveEvent(1));
            foreach (var e in present)
            {
                start.Add(new SoundEvent(Sound(e, "projectile"), SpellsBank, spell[e]));
                start.Add(new EffectEvent(TrailBig(e), Follow: true, WorldAlign: false));
                hit.Add(new SoundEvent(Sound(e, "blast"), SpellsBank, spell[e]));
                collision.Add(new EffectEvent(HitBig(e), Follow: false, WorldAlign: false));
                collision.Add(new EffectEvent(HitBig(e), Follow: false, WorldAlign: false));   // twice, as the original's table
                collision.Add(new SoundEvent(Sound(e, "blast"), SpellsBank, spell[e]));
            }
        }
        else if ((elements & Elements.Earth) != 0)
        {
            hit.AddRange(Damages(damage, 5));
            collision.Add(new DecalEvent("crater", 1 + (spell.Earth - 1) * 0.5f));
            var remainder = elements & ~Elements.Earth;
            if (splash > float.Epsilon)
            {
                if (remainder != Elements.None)
                {
                    var blast = new BlastEvent(SpellDamage.For(spell, SpellTypes.Of(remainder), CastType.Area), splash);
                    hit.Add(blast);
                    collision.Add(blast);
                }
                hit.Add(new RemoveEvent(0));
                collision.Add(new RemoveEvent(0));
            }
            else
            {
                hit.Add(new RemoveEvent(ProjectileRules.BoulderPierces));
                collision.Add(new RemoveEvent(1));
            }
            foreach (var e in present)
            {
                start.Add(new SoundEvent(Sound(e, "projectile"), SpellsBank, 1));
                start.Add(new EffectEvent(TrailBig(e), Follow: true, WorldAlign: false));
                if (splash > 0)
                {
                    hit.Add(new SoundEvent(Sound(e, "blast"), SpellsBank, spell[e]));
                    collision.Add(new SoundEvent(Sound(e, "blast"), SpellsBank, spell[e]));
                }
                else
                {
                    collision.Add(new EffectEvent(HitBig(e), Follow: false, WorldAlign: false));
                    collision.Add(new SoundEvent(Sound(e, "hit"), SpellsBank, spell[e]));
                }
            }
        }
        else
        {
            foreach (var e in present)
            {
                start.Add(new EffectEvent(TrailSmall(e), Follow: true, WorldAlign: false));
                hit.Add(new EffectEvent(HitSmall(e), Follow: false, WorldAlign: false));
                collision.Add(new EffectEvent(HitSmall(e), Follow: false, WorldAlign: false));
            }
            var share = count > 0 ? 1f / count : 1;
            var shards = damage.Select(d => d with { Attack = d.Attack | AttackProperties.Piercing, Magnitude = d.Magnitude * share }).ToList();
            hit.AddRange(Damages(shards, 4));
            hit.Add(new SoundEvent(Sound(Elements.Ice, "hit"), SpellsBank, 1));
            collision.Add(new SoundEvent(Sound(Elements.Ice, "hit"), SpellsBank, 1));
            collision.Add(new RemoveEvent(0));
        }
        return
        [
            new EventGroup(new EventCondition(EventTrigger.Default), start),
            new EventGroup(new EventCondition(EventTrigger.Hit, Repeat: true), hit),
            new EventGroup(new EventCondition(EventTrigger.Collision, Repeat: true), collision),
            new EventGroup(new EventCondition(EventTrigger.Timer, Time: ProjectileRules.Lifetime), [new RemoveEvent(0)]),
        ];
    }

    /// <summary>Launches one of a spell's missiles from a caster; null when the level is full.</summary>
    public static Missile? Launch(Character caster, Spell spell, Vector3 from, Vector3 velocity, float splash, int count)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed || manager.Entities.Count >= EntityManager.Capacity)
        {
            return null;
        }
        var missile = new Missile(caster.World, caster, from, velocity, 0.075f + 0.075f * spell.Earth, Events(spell, splash, count), canHitOwner: true);
        try
        {
            manager.Add(missile);
        }
        catch
        {
            caster.World.Remove(missile.Body);
            throw;
        }
        missile.Start();
        return missile;
    }

    /// <summary>The first damage entries, the first scaled by the missile's speed.</summary>
    private static IEnumerable<ItemEvent> Damages(IReadOnlyList<DamageEntry> damage, int most) =>
        damage.Take(most).Select((d, i) => new DamageEvent(d, VelocityBased: i == 0));
}
