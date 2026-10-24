using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>What a barrier takes from blows, and how long a solid one strikes (docs/gameplay/SPELLS.md §5.6).</summary>
public sealed partial class Barrier
{
    private const Elements Geysers = Elements.Water | Elements.Cold | Elements.Fire | Elements.Lightning | Elements.Steam | Elements.Poison;

    private const Elements Volcanoes = Elements.Water | Elements.Cold | Elements.Fire | Elements.Steam | Elements.Poison;

    /// <summary>
    /// A solid barrier's blow takes Arcane out of the blow first. A barrier of an element puts out
    /// its effect on its opposite (no harm done); ice lets fire through whole; otherwise its armour
    /// comes off. An elemental wall loses five times the blow for the share of its elements the blow opposes.
    /// </summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if (IsDead)
        {
            return DamageResult.None;
        }
        var element = damage.Element & ~Elements.Arcane;
        if (!IsSolid)
        {
            if (damage.Amount * damage.Magnitude > 0)
            {
                HitPoints -= (int)(OpposedShare(element) * damage.Amount * 5);
            }
            return DamageResult.None;
        }
        var amount = damage.Amount;
        var result = DamageResult.None;
        if (amount > 0)
        {
            if (PutsOut(element))
            {
                effectTime = 0;
                return DamageResult.None;
            }
            amount = Spell.Ice > 0 && (element & Elements.Fire) != 0 ? amount : MathF.Max(amount - Armour, 0);
            result = amount == 0 ? DamageResult.Deflected : DamageResult.Damaged | DamageResult.Hit;
        }
        if (amount * damage.Magnitude > 0)
        {
            HitPoints -= amount;
        }
        return HitPoints <= 0 ? result | DamageResult.Killed : result;
    }

    /// <summary>Its element's opposite puts a solid barrier's effect out: cold by fire or steam, steam by cold, water by fire, fire by water.</summary>
    private bool PutsOut(Elements element)
    {
        var mine = Spell.Element;
        return ((mine & Elements.Cold) != 0 && (element & (Elements.Fire | Elements.Steam)) != 0)
            || ((mine & Elements.Steam) != 0 && (element & Elements.Cold) != 0)
            || ((mine & Elements.Water) != 0 && (element & Elements.Fire) != 0)
            || ((mine & Elements.Fire) != 0 && (element & Elements.Water) != 0);
    }

    private float OpposedShare(Elements element)
    {
        var own = Enumerable.Range(0, ElementIndex.Count).Select(i => (Elements)(1 << i))
            .Where(e => e != Elements.Shield && (Spell.Element & e) != 0).ToList();
        return own.Count == 0 ? 0 : (float)own.Count(e => ElementRelations.InclusiveOpposites(element, e)) / own.Count;
    }

    /// <summary>
    /// How long a solid barrier strikes, and how long pure ice stands. Ice: I seconds, a tesla
    /// (with Lightning) Z; an ice geyser (another element, no Arcane or Life) the strongest such
    /// element's magnitude. Earth strikes only as a volcano (Water, Cold, Fire, Steam or Poison, no
    /// Arcane or Life): five times that magnitude. Lightning always counts as the strongest.
    /// </summary>
    private static (float Effect, float Ice) EffectTimes(Spell spell)
    {
        var elements = spell.Element;
        var (effect, ice) = (0f, 0f);
        var beams = (elements & ElementSets.Beams) != 0;
        if ((elements & Elements.Ice) != 0)
        {
            effect = spell.Ice;
            var geyser = (elements & Geysers) != 0 && !beams;
            if (geyser && (elements & Elements.Lightning) != 0)
            {
                effect = spell.Lightning;
            }
            ice = effect;
            if (geyser && (elements & Elements.Earth) == 0)
            {
                effect = Dominant(spell) is var d && d != Elements.Lightning ? spell[d] : effect;
            }
        }
        if ((elements & Elements.Earth) != 0 && (elements & Volcanoes) != 0 && !beams)
        {
            effect = Dominant(spell) is var d && d != Elements.Lightning ? spell[d] * 5 : effect;
        }
        return (effect, ice);
    }

    /// <summary>
    /// Lightning if the spell holds it, else its strongest of Water, Cold, Fire and Steam, the first
    /// on a tie. As the original, Water wins when none of them is in the spell (its magnitude is 0).
    /// </summary>
    private static Elements Dominant(Spell spell)
    {
        if ((spell.Element & Elements.Lightning) != 0)
        {
            return Elements.Lightning;
        }
        var best = Elements.Water;
        foreach (var e in new[] { Elements.Cold, Elements.Fire, Elements.Steam })
        {
            if (spell[e] > spell[best])
            {
                best = e;
            }
        }
        return best;
    }
}
