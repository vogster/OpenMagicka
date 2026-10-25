using System.Numerics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The spells' own pictures on the logic thread (OM-76, docs/engine/ARCHITECTURE.md §17): each tick the beams
/// in the world become their pictures and lights, and each new beam starts its effects at its source (a first
/// beam only) and at its end, which follow it; the bursts the gameplay cues (bolts, blasts, tracers, gunfire)
/// live their while here.
/// </summary>
public sealed partial class SpellVisuals : IDisposable
{
    /// <summary>A beam's light: its reach and how bright, as the original's.</summary>
    public const float BeamLightRadius = 5, BeamLightIntensity = 0.6f;

    private readonly GameWorld world;
    private readonly EffectDirector effects;
    private readonly HashSet<Beam> seen = new(ReferenceEqualityComparer.Instance);

    public SpellVisuals(GameWorld world, EffectDirector effects)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
        world.Entities.Cue += Hear;
    }

    /// <summary>A beam's effect at its source or its end.</summary>
    private sealed record BeamEnd(Beam Beam, bool Hit);

    /// <summary>A tick, after the effects': the cues taken, the bursts aged, the new beams' effects started, the gone ones forgotten.</summary>
    public void Update(float dt)
    {
        TakeCues();
        Age(dt);
        var beams = world.Entities.Entities.OfType<Beam>().Where(b => !b.IsDead).ToList();
        seen.IntersectWith(beams);
        foreach (var beam in beams.Where(seen.Add))
        {
            var (source, hit) = beam.IsArcane ? ("arcane_source", "arcane_hit") : ("life_source", "life_hit");
            if (beam.Parents.Count == 0)
            {
                effects.Follow(new BeamEnd(beam, false), source, () => Live(beam) ? EffectDirector.Facing(beam.Position, beam.Direction) : null);
            }
            effects.Follow(new BeamEnd(beam, true), hit, () => Live(beam) ? EffectDirector.Facing(End(beam), beam.Direction) : null);
        }
    }

    /// <summary>The level went to a scene, or back to a checkpoint: the bursts of the old world go.</summary>
    public void Clear()
    {
        seen.Clear();
        ClearBursts();
    }

    /// <summary>What the spells show this tick, into the frame's pictures (cleared first).</summary>
    public void Write(SpellPictures into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var beam in world.Entities.Entities.OfType<Beam>().Where(b => !b.IsDead))
        {
            var (picture, light) = Picture(beam);
            into.Beams.Add(picture);
            into.Lights.Add(light);
        }
        WriteBursts(into);
    }

    /// <summary>A beam's picture and its light along it (a branch's lit round about too).</summary>
    public static (BeamPicture Picture, CapsuleLighting Light) Picture(Beam beam)
    {
        ArgumentNullException.ThrowIfNull(beam);
        var colour = ToXna(beam.Colour);
        var branch = beam.Parents.Count > 0;
        var picture = new BeamPicture(ToXna(beam.Position), ToXna(beam.Direction), beam.Length, beam.Age * 30, beam.Rings, branch, colour, Rows(beam.ExtraElements));
        var light = new CapsuleLighting(picture.Origin, ToXna(End(beam)), BeamLightRadius, colour * BeamLightIntensity,
            branch ? colour * 0.5f * BeamLightIntensity : Xna.Vector3.Zero);
        return (picture, light);
    }

    /// <summary>The rows of a beam's other elements.</summary>
    public static BeamRows Rows(Elements elements)
    {
        var rows = BeamRows.None;
        foreach (var (element, row) in RowsOf)
        {
            rows |= (elements & element) != 0 ? row : BeamRows.None;
        }
        return rows;
    }

    private static readonly (Elements Element, BeamRows Row)[] RowsOf =
    [
        (Elements.Water, BeamRows.Water), (Elements.Cold, BeamRows.Cold), (Elements.Fire, BeamRows.Fire),
        (Elements.Lightning, BeamRows.Lightning), (Elements.Steam, BeamRows.Steam), (Elements.Poison, BeamRows.Poison),
    ];

    private static bool Live(Beam beam) => !beam.IsDead && beam.Manager is not null;

    private static Vector3 End(Beam beam) => beam.Position + beam.Direction * beam.Length;

    private static Xna.Vector3 ToXna(Vector3 v) => new(v.X, v.Y, v.Z);

    public void Dispose() => world.Entities.Cue -= Hear;
}
