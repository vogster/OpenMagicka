using System.Numerics;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The standing spells' effects, as the original's (docs/engine/ARCHITECTURE.md §17): an elemental barrier is its
/// element's wall of particles, standing where it stands; a solid one bursts out of the ground as it rises and as it
/// breaks; a mine flares as it is set off and bursts as it goes off; one of Arcane or Life bursts as a barrier too.
/// </summary>
public sealed partial class SpellVisuals
{
    private readonly Dictionary<Barrier, bool> barriers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpellMine, bool> mines = new(ReferenceEqualityComparer.Instance);

    /// <summary>A barrier's wall of particles.</summary>
    private sealed record BarrierWall(Barrier Barrier);

    private void WatchStanding()
    {
        foreach (var barrier in world.Entities.Entities.OfType<Barrier>().Where(b => !barriers.ContainsKey(b)))
        {
            barriers[barrier] = false;
            Rise(barrier);
        }
        foreach (var (barrier, fell) in barriers.ToList())
        {
            var gone = barrier.Manager is null || barrier.IsRemovable;
            if (!fell && (barrier.IsDead || gone))
            {
                barriers[barrier] = true;
                Fall(barrier);
            }
            if (gone)
            {
                barriers.Remove(barrier);
            }
        }
        foreach (var mine in world.Entities.Entities.OfType<SpellMine>().Where(m => !mines.ContainsKey(m)))
        {
            mines[mine] = false;
        }
        foreach (var (mine, lit) in mines.ToList())
        {
            if (!lit && mine.IsLit)
            {
                mines[mine] = true;
                effects.Once("mine_activation", EffectDirector.Facing(mine.Position, -Vector3.UnitZ));
            }
            if (mine.IsDead || mine.Manager is null)
            {
                if (mine.HasGoneOff)
                {
                    effects.Once("mine_explosion", EffectDirector.Facing(mine.Position, -Vector3.UnitZ));
                }
                mines.Remove(mine);
            }
        }
    }

    /// <summary>An elemental barrier's wall of its element, standing where it stands and as big; a solid one's burst from the ground.</summary>
    private void Rise(Barrier barrier)
    {
        if (barrier.IsSolid)
        {
            foreach (var (element, effect) in new[] { (Elements.Ice, "barrier_ice_spawn"), (Elements.Earth, "barrier_earth_spawn") })
            {
                if ((barrier.Spell.Element & element) != 0)
                {
                    effects.Once(effect, EffectDirector.Facing(barrier.Ground, barrier.Facing));
                }
            }
            return;
        }
        if (WallElement(barrier.Spell) is { } wall)
        {
            effects.Follow(new BarrierWall(barrier), $"barrier_{wall.ToString().ToLowerInvariant()}1_b",
                () => barrier.Manager is null || barrier.IsDead ? null : Matrix4x4.CreateScale(barrier.Scale) * EffectDirector.Facing(barrier.Position, barrier.Facing));
        }
    }

    /// <summary>A solid barrier breaking: an arcane or life one bursts as a mine does; another crumbles back into the ground.</summary>
    private void Fall(Barrier barrier)
    {
        if (!barrier.IsSolid)
        {
            return;                                                           // its wall stops with it
        }
        if ((barrier.Spell.Element & ElementSets.Beams) != 0)
        {
            effects.Once("mine_explosion", EffectDirector.Facing(barrier.Position, barrier.Facing));
            return;
        }
        foreach (var (element, effect) in new[] { (Elements.Ice, "barrier_ice_death"), (Elements.Earth, "barrier_earth_death") })
        {
            if ((barrier.Spell.Element & element) != 0)
            {
                effects.Once(effect, EffectDirector.Facing(barrier.Ground, barrier.Facing));
            }
        }
    }

    /// <summary>
    /// The element an elemental barrier's wall is of, as the original picks it: Steam with Lightning and Steam, else
    /// Lightning, else the strongest of Water, Cold, Fire and Steam (the first of equals); none without any.
    /// </summary>
    public static Elements? WallElement(Spell spell)
    {
        if (spell.Lightning > 0)
        {
            return spell.Steam > 0 ? Elements.Steam : Elements.Lightning;
        }
        var (best, most) = ((Elements?)null, 0f);
        foreach (var element in new[] { Elements.Water, Elements.Cold, Elements.Fire, Elements.Steam })
        {
            if (spell[element] > most)
            {
                (best, most) = (element, spell[element]);
            }
        }
        return best;
    }
}
