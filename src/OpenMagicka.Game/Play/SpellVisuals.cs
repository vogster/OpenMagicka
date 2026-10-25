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

    /// <summary>A spell's missile's light's reach.</summary>
    public const float MissileLightRadius = 6;

    private readonly GameWorld world;
    private readonly EffectDirector effects;
    private readonly HashSet<Beam> seen = new(ReferenceEqualityComparer.Instance);
    private readonly Func<object, ModelLook?> looks;

    /// <param name="models">The skinned models the barriers and mines are drawn as, by asset; none without the game's content.</param>
    /// <param name="looks">What the templates look like (a bubble is its wearer's model).</param>
    public SpellVisuals(GameWorld world, EffectDirector effects, Func<string, Engine.Characters.AnimatedModel?>? models = null,
        Func<object, ModelLook?>? looks = null)
    {
        this.looks = looks ?? (_ => null);
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
        this.models = models ?? (_ => null);
        world.Entities.Cue += Hear;
    }

    /// <summary>A beam's effect at its source or its end.</summary>
    private sealed record BeamEnd(Beam Beam, bool Hit);

    /// <summary>A tick, after the effects': the bursts aged, the cues taken (new ones show whole), the new beams' effects started, the gone ones forgotten.</summary>
    public void Update(float dt)
    {
        Age(dt);
        AgeShields(dt);
        TakeCues();
        WatchStanding();
        AgeModels(dt);
        AgeSelfShields(dt);
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
        shields.Clear();
        barriers.Clear();
        mines.Clear();
        barrierModels.Clear();
        mineModels.Clear();
        selfShields.Clear();
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
        WriteShields(into);
        WriteBubbles(into);
        foreach (var missile in world.Entities.Entities.OfType<Gameplay.Entities.Missile>())
        {
            if (!missile.IsDead && MissileLight(missile.Glow) is { } colour)
            {
                into.Points.Add(new PointLighting(ToXna(missile.Position), MissileLightRadius, ToXna(colour * 1.333f), ToXna(colour * 0.666f), colour.Length()));
            }
        }
    }

    /// <summary>A spell's missile's light: the plain average of its Fire, Arcane, Life, Lightning and Cold; none without them.</summary>
    public static Vector3? MissileLight(Elements elements)
    {
        var lit = new[] { (Elements.Fire, SpellColours.Fire), (Elements.Arcane, SpellColours.Arcane), (Elements.Life, SpellColours.Life),
            (Elements.Lightning, SpellColours.Lightning), (Elements.Cold, SpellColours.Cold) }.Where(p => (elements & p.Item1) != 0).ToList();
        return lit.Count == 0 ? null : lit.Aggregate(Vector3.Zero, (sum, p) => sum + p.Item2) / lit.Count;
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
