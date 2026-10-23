using Microsoft.Xna.Framework;
using OpenMagicka.Content.Particles;
using P = OpenMagicka.Content.Particles.EmitterParameter;

namespace OpenMagicka.Engine.Particles;

/// <summary>Where an emitter is in a step: the effect's times and places before and after it.</summary>
public readonly record struct EmitterStep(
    float Dt, float PreviousTotal, float Total, float Timeline, float PreviousTimeline, Matrix Transform, Matrix PreviousTransform)
{
    /// <summary>How fast the effect itself moved this step, for emitters with relative velocity.</summary>
    public Vector3 EffectVelocity => (Transform.Translation - PreviousTransform.Translation) / Dt;
}

/// <summary>
/// One emitter of a running effect, with its own place in the random table
/// (docs/engine/ARCHITECTURE.md §5.3). Continuous emitters spawn their rate; pulse emitters spawn
/// their count once when the timeline passes their trigger time.
/// </summary>
public sealed class EmitterRuntime(EmitterContent emitter, ushort randomOffset)
{
    private ushort at = randomOffset;

    public void Update(in EmitterStep step, ParticlePool pool)
    {
        if (emitter.Kind == EmitterKind.Pulse)
        {
            Pulse(step, pool);
        }
        else
        {
            Continuous(step, pool);
        }
    }

    private float At(P parameter, float time) => emitter.Evaluate(parameter, time);

    private bool Has(EmitterFlags flag) => emitter.Flags.HasFlag(flag);

    /// <summary>The emitter's own place at a time on the timeline, in the effect's space.</summary>
    private Matrix Place(float time) =>
        Matrix.CreateRotationY(At(P.RotationY, time)) * Matrix.CreateTranslation(At(P.PositionX, time), At(P.PositionY, time), At(P.PositionZ, time));

    private Vector3 Offset(float time) => new(At(P.PositionXOffset, time), At(P.PositionYOffset, time), At(P.PositionZOffset, time));

    private void Continuous(in EmitterStep step, ParticlePool pool)
    {
        var t = step.Timeline;
        // The rate is the mean of its values at the start and the end of the step; whole
        // particles are counted from the running total time.
        var rate = (At(P.ParticlesPerSecond, t) + At(P.ParticlesPerSecond, t + step.Dt)) * 0.5f;
        var count = (int)(step.Total * rate) - (int)(step.PreviousTotal * rate);
        if (count <= 0)
        {
            return;
        }
        var from = Place(step.PreviousTimeline) * step.PreviousTransform;
        var to = Place(t) * step.Transform;
        var (offsetFrom, offsetTo) = (Offset(step.PreviousTimeline), Offset(t));
        var hsv = Has(EmitterFlags.Hsv);
        var aligned = Has(EmitterFlags.RotationAligned);
        var cone = Has(EmitterFlags.ConeSpread);
        var carried = Has(EmitterFlags.RelativeVelocity) ? step.EffectVelocity : Vector3.Zero;
        var baseColor = hsv ? Vector4.Zero : new Vector4(At(P.ColorR, t), At(P.ColorG, t), At(P.ColorB, t), 0);
        for (var i = 1; i <= count; i++)
        {
            var share = (float)i / count;
            var transform = Matrix.Lerp(from, to, share);
            var direction = cone
                ? ParticleRandom.Cone(ref at, transform, At(P.SpreadConeAngle, t), At(P.SpreadConeDistribution, t))
                : ParticleRandom.Arc(ref at, transform, At(P.SpreadArcHorizontalAngle, t), At(P.SpreadArcHorizontalDistribution, t),
                    At(P.SpreadArcVerticalMin, t), At(P.SpreadArcVerticalMax, t), At(P.SpreadArcVerticalDistribution, t));
            var offset = Vector3.Lerp(offsetFrom, offsetTo, share);
            var position = Vector3.Transform(Jitter(offset), transform);
            var startSize = ParticleRandom.Range(ref at, At(P.SizeStartMin, t), At(P.SizeStartMax, t), At(P.SizeStartDistribution, t));
            var endSize = ParticleRandom.Range(ref at, At(P.SizeEndMin, t), At(P.SizeEndMax, t), At(P.SizeEndDistribution, t));
            var ttl = ParticleRandom.Range(ref at, At(P.LifeTimeMin, t), At(P.LifeTimeMax, t), At(P.LifeTimeDistribution, t));
            var speed = ParticleRandom.Range(ref at, At(P.VelocityMin, t), At(P.VelocityMax, t), At(P.VelocityDistribution, t));
            var (rotation, spin) = aligned ? (0f, 0f) : Spin(At(P.RotationMin, t), At(P.RotationMax, t), At(P.RotationSpeedMin, t),
                At(P.RotationSpeedMax, t), (int)At(P.RotationPCCW, t));
            var color = hsv
                ? new Vector4(
                    ParticleRandom.Range(ref at, At(P.HueMin, t), At(P.HueMax, t), At(P.HueDistribution, t)),
                    ParticleRandom.Range(ref at, At(P.SatMin, t), At(P.SatMax, t), At(P.SatDistribution, t)),
                    ParticleRandom.Range(ref at, At(P.ValueMin, t), At(P.ValueMax, t), At(P.ValueDistribution, t)),
                    ParticleRandom.Range(ref at, At(P.AlphaMin, t), At(P.AlphaMax, t), At(P.AlphaDistribution, t)))
                : baseColor with { W = ParticleRandom.Range(ref at, At(P.ColorAMin, t), At(P.ColorAMax, t), At(P.ColorADistribution, t)) };
            pool.Spawn(new ParticleSpawn
            {
                Position = position, Velocity = direction * speed + carried, Gravity = At(P.Gravity, t), Drag = At(P.Drag, t), TTL = ttl,
                StartSize = startSize, EndSize = endSize, Rotation = rotation, RotationVelocity = spin, Color = color,
                Sprite = emitter.Particle, AlphaBlended = Has(EmitterFlags.AlphaBlended), Hsv = hsv, Colorize = Has(EmitterFlags.Colorize),
                RotationAligned = aligned,
            });
        }
    }

    private void Pulse(in EmitterStep step, ParticlePool pool)
    {
        var trigger = At(P.TriggerTime, 0);
        // Fires when the timeline passes the trigger, or wraps past it in a looping effect.
        if (!(step.Timeline > trigger) || !(step.PreviousTimeline <= trigger || step.PreviousTimeline > step.Timeline))
        {
            return;
        }
        var place = Place(0) * step.Transform;
        var offset = Offset(0);
        var hsv = Has(EmitterFlags.Hsv);
        var aligned = Has(EmitterFlags.RotationAligned);
        var carried = Has(EmitterFlags.RelativeVelocity) ? step.EffectVelocity : Vector3.Zero;
        for (var i = 0; i < emitter.NrOfParticles; i++)
        {
            var position = Vector3.Transform(Jitter(offset), place);
            var startSize = ParticleRandom.Range(ref at, At(P.SizeStartMin, 0), At(P.SizeStartMax, 0), At(P.SizeStartDistribution, 0));
            var endSize = ParticleRandom.Range(ref at, At(P.SizeEndMin, 0), At(P.SizeEndMax, 0), At(P.SizeEndDistribution, 0));
            var direction = Has(EmitterFlags.ConeSpread)
                ? ParticleRandom.Cone(ref at, place, At(P.SpreadConeAngle, 0), At(P.SpreadConeDistribution, 0))
                : ParticleRandom.Arc(ref at, place, At(P.SpreadArcHorizontalAngle, 0), At(P.SpreadArcHorizontalDistribution, 0),
                    At(P.SpreadArcVerticalMin, 0), At(P.SpreadArcVerticalMax, 0), At(P.SpreadArcVerticalDistribution, 0));
            var speed = ParticleRandom.Range(ref at, At(P.VelocityMin, 0), At(P.VelocityMax, 0), At(P.VelocityDistribution, 0));
            var ttl = ParticleRandom.Range(ref at, At(P.LifeTimeMin, 0), At(P.LifeTimeMax, 0), At(P.LifeTimeDistribution, 0));
            var (rotation, spin) = aligned ? (0f, 0f) : Spin(At(P.RotationMin, 0), At(P.RotationMax, 0), At(P.RotationSpeedMin, 0),
                At(P.RotationSpeedMax, 0), (int)At(P.RotationPCCW, 0));
            // A pulse takes alpha from AlphaMin/Max either way, and colorizes only HSV particles.
            var color = hsv
                ? new Vector4(
                    ParticleRandom.Range(ref at, At(P.HueMin, 0), At(P.HueMax, 0), At(P.HueDistribution, 0)),
                    ParticleRandom.Range(ref at, At(P.SatMin, 0), At(P.SatMax, 0), At(P.SatDistribution, 0)),
                    ParticleRandom.Range(ref at, At(P.ValueMin, 0), At(P.ValueMax, 0), At(P.ValueDistribution, 0)),
                    ParticleRandom.Range(ref at, At(P.AlphaMin, 0), At(P.AlphaMax, 0), At(P.AlphaDistribution, 0)))
                : new Vector4(At(P.ColorR, 0), At(P.ColorG, 0), At(P.ColorB, 0),
                    ParticleRandom.Range(ref at, At(P.AlphaMin, 0), At(P.AlphaMax, 0), At(P.AlphaDistribution, 0)));
            pool.Spawn(new ParticleSpawn
            {
                Position = position, Velocity = direction * speed + carried, Gravity = At(P.Gravity, 0), Drag = At(P.Drag, 0), TTL = ttl,
                StartSize = startSize, EndSize = endSize, Rotation = rotation, RotationVelocity = spin, Color = color,
                Sprite = emitter.Particle, AlphaBlended = Has(EmitterFlags.AlphaBlended), Hsv = hsv, Colorize = hsv && Has(EmitterFlags.Colorize),
                RotationAligned = aligned,
            });
        }
    }

    /// <summary>A random point in the box ±offset around the emitter.</summary>
    private Vector3 Jitter(Vector3 offset) => new(
        (ParticleRandom.Next(ref at) * 2 - 1) * offset.X,
        (ParticleRandom.Next(ref at) * 2 - 1) * offset.Y,
        (ParticleRandom.Next(ref at) * 2 - 1) * offset.Z);

    /// <summary>A start angle and a spin, counter-clockwise for about <paramref name="percentCounterClockwise"/> in 100.</summary>
    private (float Rotation, float Spin) Spin(float min, float max, float speedMin, float speedMax, int percentCounterClockwise)
    {
        var rotation = ParticleRandom.Range(ref at, min, max);
        var speed = ParticleRandom.Range(ref at, speedMin, speedMax);
        return (rotation, speed * ParticleRandom.Sign(ref at, percentCounterClockwise));
    }
}
