using System.Numerics;
using OpenMagicka.Engine.Particles;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The bursts, as the original's (docs/engine/ARCHITECTURE.md §17): a bolt shows a while as it fades, lit along;
/// a blast throws its element's effect eight ways, as far as each way reaches (an arcane or life one spreads its
/// disc instead), and lights a while; a tracer flies its shot as one particle; a gun flashes at its muzzle and
/// throws shells while it fires.
/// </summary>
public sealed partial class SpellVisuals
{
    /// <summary>A bolt's light: its reach, and how bright by the square root of its life left.</summary>
    public const float BoltLightRadius = 5, BoltLightIntensity = 1.5f;

    /// <summary>How fast the discs turn, and how fast their life runs.</summary>
    public const float DiscTurn = 4, DiscPace = 5;

    private static readonly Elements[] Singles =
        [.. Enum.GetValues<Elements>().Where(e => e is > Elements.None and <= Elements.Poison && BitOperations.IsPow2((int)e))];

    private readonly List<(Entity? Source, AnimationCue Cue)> cues = [];
    private readonly List<Bolt> bolts = [];
    private readonly List<Disc> discs = [];
    private readonly List<PassingLight> lights = [];
    private readonly List<(TracerCue Tracer, float Delay)> tracers = [];
    private readonly Random random = new(7);
    private float discTurn;

    private sealed class Bolt(LightningCue cue, int shape)
    {
        public LightningCue Cue { get; } = cue;
        public int Shape { get; } = shape;
        public float Life { get; set; } = cue.Life;
    }

    private sealed class Disc(Vector3 centre, float radius, Elements elements)
    {
        public Vector3 Centre { get; } = centre;
        public float MaxRadius { get; } = radius;
        public Elements Elements { get; } = elements;
        public float Life { get; set; } = 1;
    }

    /// <summary>
    /// A light that comes up over 1/speed seconds within its time, then, its time out, goes over 1/speed (from full,
    /// as the original's, even when it had not come up all the way).
    /// </summary>
    public sealed class PassingLight(Vector3 position, Vector3 colour, float intensity, float radius, float speed, float time)
    {
        public Vector3 Position { get; } = position;
        public Vector3 Colour { get; } = colour;
        public float Intensity { get; } = intensity;
        public float Radius { get; } = radius;
        public float Brightness { get; private set; }
        public bool IsOut { get; private set; }
        private float time = time;
        private bool going;

        public void Update(float dt)
        {
            time -= dt;
            if (!going && time <= 0)
            {
                (going, Brightness) = (true, 1);                              // as the original: it starts going from full
                return;
            }
            Brightness = going ? Brightness - dt * speed : MathF.Min(1, Brightness + dt * speed);
            IsOut = going && Brightness <= 0;
        }
    }

    /// <summary>A gun's shells, while it fires.</summary>
    private sealed record Shells(Item Gun);

    /// <summary>A cue heard (the world raises them; a test may too): the bursts and gunfire are kept for the next tick.</summary>
    public void Hear(Entity? source, AnimationCue cue)
    {
        if (cue is LightningCue or BlastCue or TracerCue or MuzzleCue or GunfireCue or ShieldHitCue)
        {
            cues.Add((source, cue));
        }
    }

    private void TakeCues()
    {
        foreach (var (source, cue) in cues)
        {
            switch (cue)
            {
                case LightningCue bolt:
                    Strike(bolt);
                    break;
                case BlastCue blast when blast.Arcane:
                    Spread(blast);
                    break;
                case BlastCue blast:
                    Throw(blast);
                    break;
                case ShieldHitCue hit:
                    Strike(hit);
                    break;
                case TracerCue tracer:
                    tracers.Add((tracer, tracer.Delay));
                    break;
                case MuzzleCue { Source: { Template.Gun: { Muzzle.Length: > 0 } gun } item }:
                    effects.Once(gun.Muzzle, (item.Template.Attach0 ?? Matrix4x4.Identity) * item.Transform);
                    break;
                case GunfireCue { Firing: true, Source: { Template.Gun: { Shells.Length: > 0 } gun } item }:
                    effects.Follow(new Shells(item), gun.Shells, () => source is { IsDead: true } or { Manager: null }
                        ? null                                                // its holder died or left mid-burst
                        : (item.Template.Attach1 ?? Matrix4x4.Identity) * item.Transform);
                    break;
                case GunfireCue { Firing: false } stop:
                    effects.Unfollow(new Shells(stop.Source));
                    break;
            }
        }
        cues.Clear();
    }

    private void Strike(LightningCue cue)
    {
        var way = cue.To - cue.From;
        way = way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) : -Vector3.UnitZ;
        effects.Once("lightning_source", EffectDirector.Facing(cue.From, way));
        effects.Once("lightning_hit", EffectDirector.Facing(cue.To, way));
        if (cue.Life > 0)
        {
            bolts.Add(new Bolt(cue, random.Next(BurstDrawing.Shapes)));
        }
    }

    /// <summary>An arcane or life blast: its disc, each element's explosion, and a short light of its colour.</summary>
    private void Spread(BlastCue blast)
    {
        discs.Add(new Disc(blast.Position, blast.Radius, blast.Element));
        foreach (var element in Bits(blast.Element).Where(e => e is not (Elements.Earth or Elements.Shield or Elements.Ice)))
        {
            effects.Once($"beam_{Name(element)}_explosion", EffectDirector.Facing(blast.Position, -Vector3.UnitZ));
        }
        lights.Add(new PassingLight(blast.Position, SpellColours.Of(Spell.Of(blast.Element)), 2, blast.Radius * 2, 2, 0.25f));
    }

    /// <summary>A blast of one element: its effect eight ways round, each scaled out as far as that way reaches; a ground blast's light.</summary>
    private void Throw(BlastCue blast)
    {
        var element = Bits(blast.Element).FirstOrDefault();
        if (element == Elements.None)
        {
            return;
        }
        var effect = blast.Ground || element == Elements.Earth ? $"area_ground_{Name(element)}" : $"area_{Name(element)}1_b";
        for (var i = 0; i < Blasts.Directions; i++)
        {
            effects.Once(effect, BlastWay(blast, i));
        }
        if (blast.Ground && element != Elements.Earth)
        {
            lights.Add(new PassingLight(blast.Position, SpellColours.Of(Spell.Of(element)), 6, blast.Radius * 2, 4, 1));
        }
    }

    /// <summary>One of a blast's eight effects: turned i eighths round, its level axes scaled by how far that way reaches × radius / 10.</summary>
    public static Matrix4x4 BlastWay(BlastCue blast, int i)
    {
        ArgumentNullException.ThrowIfNull(blast);
        var scale = (i < blast.Reach.Count ? blast.Reach[i] : 1) * blast.Radius * 0.1f;
        var way = Matrix4x4.CreateRotationY(i * MathF.Tau / Blasts.Directions);
        (way.M11, way.M12, way.M13) = (way.M11 * scale, way.M12 * scale, way.M13 * scale);
        (way.M31, way.M32, way.M33) = (way.M31 * scale, way.M32 * scale, way.M33 * scale);
        way.Translation = blast.Position;
        return way;
    }

    private void Age(float dt)
    {
        foreach (var bolt in bolts)
        {
            bolt.Life -= dt;
        }
        bolts.RemoveAll(b => b.Life <= 0);
        discTurn = (discTurn + DiscTurn * dt) % MathF.Tau;
        foreach (var disc in discs)
        {
            disc.Life -= DiscPace * dt;
        }
        discs.RemoveAll(d => d.Life <= -1);
        foreach (var light in lights)
        {
            light.Update(dt);
        }
        lights.RemoveAll(l => l.IsOut);
        for (var i = tracers.Count - 1; i >= 0; i--)
        {
            var (tracer, delay) = tracers[i];
            if ((delay -= dt) > 0)
            {
                tracers[i] = (tracer, delay);
                continue;
            }
            tracers.RemoveAt(i);
            if (Tracer(tracer) is { } particle)
            {
                effects.Spawn(particle);
            }
        }
    }

    /// <summary>A tracer's particle: from a unit past the muzzle to a unit short of the end, at its speed; none for a shorter shot.</summary>
    public static ParticleSpawn? Tracer(TracerCue tracer)
    {
        ArgumentNullException.ThrowIfNull(tracer);
        var path = tracer.To - tracer.From;
        var length = path.Length();
        var life = (length - 2) / tracer.Speed;
        if (!(tracer.Speed > 0) || !float.IsFinite(life) || !(life > 0) || tracer.Sprite is < 0 or > byte.MaxValue)
        {
            return null;
        }
        var way = path / length;
        return new ParticleSpawn
        {
            Position = ToXna(tracer.From + way), Velocity = ToXna(way * tracer.Speed), Drag = 1, TTL = life, StartSize = 1, EndSize = 1,
            Color = Xna.Vector4.One, Sprite = (byte)tracer.Sprite, RotationAligned = true,                 // ours: drawn along its flight
        };
    }

    private void ClearBursts()
    {
        cues.Clear();
        bolts.Clear();
        discs.Clear();
        lights.Clear();
        tracers.Clear();
    }

    private void WriteBursts(SpellPictures into)
    {
        foreach (var bolt in bolts)
        {
            var (from, to, colour) = (ToXna(bolt.Cue.From), ToXna(bolt.Cue.To), ToXna(bolt.Cue.Colour));
            into.Bolts.Add(new BoltPicture(from, to, colour, MathF.Sqrt(bolt.Life * 10), bolt.Shape));
            into.Lights.Add(new CapsuleLighting(from, to, BoltLightRadius, colour * BoltLightIntensity * MathF.Sqrt(bolt.Life), Xna.Vector3.Zero));
        }
        foreach (var disc in discs)
        {
            into.Discs.Add(new DiscPicture(ToXna(disc.Centre), (1 - disc.Life) * disc.MaxRadius, discTurn, MathF.Min(1, 4 * disc.Life + 1), (int)disc.Elements));
        }
        foreach (var light in lights)
        {
            into.Points.Add(new PointLighting(ToXna(light.Position), light.Radius, ToXna(light.Colour) * light.Brightness * light.Intensity, Xna.Vector3.Zero, 1));
        }
    }

    /// <summary>The single elements of a set, Earth first.</summary>
    private static IEnumerable<Elements> Bits(Elements elements) => Singles.Where(e => (elements & e) != 0);

    private static string Name(Elements element) => element.ToString().ToLowerInvariant();
}
