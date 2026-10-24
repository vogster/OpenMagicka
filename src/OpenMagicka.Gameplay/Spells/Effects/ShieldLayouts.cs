using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// Where a Shield cast with other elements lays its mines or raises its barriers (docs/gameplay/SPELLS.md §5.6):
/// two chains along an arc or a ring, both ways from in front, or one line straight ahead.
/// </summary>
public static class ShieldLayouts
{
    /// <summary>A ring's two first links, one each side of the facing, and the steps that carry each chain on its own way.</summary>
    public readonly record struct RingStart(Vector3 PointA, Vector3 FacingA, ChainStep StepA, Vector3 PointB, Vector3 FacingB, ChainStep StepB, float Chord);

    /// <summary>
    /// The first links of a ring of <paramref name="count"/> around <paramref name="centre"/>: half a
    /// link's angle each side of the facing; each chain steps by the chord between them, turning a link's angle.
    /// </summary>
    public static RingStart Ring(Vector3 centre, Vector3 facing, float radius, int count, float range)
    {
        var angle = MathF.Tau / count;
        var reach = facing * radius;
        var (a, b) = (Vector3.Transform(reach, ShieldChain.Yaw(angle / 2)), Vector3.Transform(reach, ShieldChain.Yaw(-angle / 2)));
        var chord = a - b;
        return new RingStart(
            centre + a, Vector3.Normalize(a), new ChainStep(chord, ShieldChain.Yaw(angle), range, chord.Length()),
            centre + b, Vector3.Normalize(b), new ChainStep(-chord, ShieldChain.Yaw(-angle), range, chord.Length()),
            chord.Length());
    }

    /// <summary>Lays mines: an arc ahead (Force), a ring (Area) or a line (Weapon).</summary>
    public static void LayMines(Character caster, Spell spell, CastType cast)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed)
        {
            return;
        }
        var (at, facing) = (caster.Position, caster.Body.Facing);
        if (cast == CastType.Weapon)
        {
            var count = Count(spell);
            var first = at + facing * (caster.Radius + ShieldRules.LineStep);
            if (ShieldChain.Ground(caster.World, first + Vector3.UnitY, ShieldChain.FirstProbeDown) is { } ground)
            {
                Lay(manager, caster, ground, ShieldChain.Across(facing), spell, Line(facing, count));
            }
            return;
        }
        var circle = MathF.Tau * ShieldRules.ArcRadius;
        var links = (int)(circle / 2);
        var centre = cast == CastType.Force ? at - facing * ShieldRules.MineArcCentreBack : at;
        var ring = Ring(centre, facing, ShieldRules.ArcRadius, links, 0);
        var range = cast == CastType.Force ? circle / 8 : circle / 2 - ring.Chord;
        ring = Ring(centre, facing, ShieldRules.ArcRadius, links, range);
        foreach (var (point, look, step) in new[] { (ring.PointA, ring.FacingA, ring.StepA), (ring.PointB, ring.FacingB, ring.StepB) })
        {
            if (ShieldChain.Ground(caster.World, point, ShieldChain.FirstProbeDown) is { } ground)
            {
                Lay(manager, caster, ground, look, spell, step);
            }
        }
    }

    /// <summary>Raises barriers: an arc ahead (Force; its blows a quarter as strong), a ring of radius S + 3 (Area) or a line (Weapon).</summary>
    public static void RaiseBarriers(Character caster, Spell spell, CastType cast, Barriers rules)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed)
        {
            return;
        }
        var damage = SpellDamage.For(spell, SpellType.Shield, cast);
        if (cast == CastType.Force)
        {
            damage = [.. damage.Select(d => d with { Magnitude = d.Magnitude * 0.25f })];
        }
        var hits = new BarrierHits();
        var (at, facing) = (caster.Position, caster.Body.Facing);
        if (cast == CastType.Weapon)
        {
            var first = at + facing * (caster.Radius + ShieldRules.LineStep);
            if (ShieldChain.Ground(caster.World, first + 2 * Vector3.UnitY, ShieldChain.FirstProbeDown) is { } ground && manager.Entities.Count < EntityManager.Capacity)
            {
                Barrier.Raise(manager, caster.World, caster, ground, ShieldChain.Across(facing), 1, spell, damage, hits, Line(facing, Count(spell)));
            }
            return;
        }
        var radius = cast == CastType.Force ? ShieldRules.ArcRadius : rules.RingRadius;
        var circle = MathF.Tau * radius;
        var fit = circle / (2 * rules.SegmentRadius);
        var links = (int)fit;
        var scale = 1 + (fit - links) / links;
        var centre = cast == CastType.Force ? at - facing * rules.ArcCentreBack : at;
        var lift = cast == CastType.Force ? Vector3.UnitY : Vector3.Zero;
        var chord = Ring(centre, facing, radius, links, 0).Chord;
        var ring = Ring(centre, facing, radius, links, cast == CastType.Force ? circle / 8 : (circle - chord) / 2);
        foreach (var (point, look, step) in new[] { (ring.PointA, ring.FacingA, ring.StepA), (ring.PointB, ring.FacingB, ring.StepB) })
        {
            if (ShieldChain.Ground(caster.World, point + lift, ShieldChain.FirstProbeDown) is { } ground && manager.Entities.Count < EntityManager.Capacity)
            {
                Barrier.Raise(manager, caster.World, caster, ground, look, scale, spell, damage, hits, step);
            }
        }
        var shake = ShieldRules.BarrierShakeMin + (float)caster.NextRandom() * (ShieldRules.BarrierShakeMax - ShieldRules.BarrierShakeMin);
        caster.RaiseCue(new Animation.CameraShakeCue(at, shake, 0.5f * spell.Shield));
    }

    /// <summary>A line's links: <c>2 + trunc(max(Total/2, 1))</c>.</summary>
    private static int Count(Spell spell) => 2 + (int)MathF.Max(spell.TotalMagnitude * 0.5f, 1);

    private static ChainStep Line(Vector3 facing, int count) =>
        new(facing * ShieldRules.LineStep, Quaternion.Identity, ShieldRules.LineStep * count, ShieldRules.LineStep);

    private static void Lay(EntityManager manager, Character caster, Vector3 ground, Vector3 facing, Spell spell, ChainStep step)
    {
        if (manager.Entities.Count < EntityManager.Capacity)
        {
            SpellMine.Lay(manager, caster.World, caster, ground, facing, spell, step);
        }
    }
}
