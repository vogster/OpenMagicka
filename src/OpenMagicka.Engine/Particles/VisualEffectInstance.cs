using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Particles;

namespace OpenMagicka.Engine.Particles;

/// <summary>
/// A running visual effect (docs/engine/ARCHITECTURE.md §5.3): its timeline advances with each
/// step and its emitters spawn into the pool. A Single effect stops once the timeline passes its
/// duration (its emitters still run that step), a Looping one wraps around it, an Infinite one
/// holds at its end (a looping effect with no duration gets a NaN timeline, as in the original). A
/// step shorter than float epsilon does nothing. Each emitter starts at its own place in the random
/// table, which it keeps across restarts: the original takes it from the clock; here it comes from
/// the seed, so two instances with the same seed make the same particles.
/// </summary>
public sealed class VisualEffectInstance
{
    private readonly VisualEffectContent effect;
    private readonly EmitterRuntime[] emitters;
    private Matrix transform = Matrix.Identity;
    private Matrix lastTransform = Matrix.Identity;
    private float total;

    public VisualEffectInstance(VisualEffectContent effect, int seed)
    {
        ArgumentNullException.ThrowIfNull(effect);
        this.effect = effect;
        emitters = [.. effect.Emitters.Select((e, i) => new EmitterRuntime(e, unchecked((ushort)(seed + i * 4099))))];
    }

    public bool IsActive { get; private set; }

    public float TimelinePosition { get; private set; }

    public void Start(Matrix at)
    {
        IsActive = true;
        transform = at;
        lastTransform = at;
        TimelinePosition = 0;
        total = 0;
    }

    public void Stop() => IsActive = false;

    /// <summary>Moves the effect; its next step spreads its particles from the old place to the new one.</summary>
    public void Move(Matrix to) => transform = to;

    public void Update(float dt, ParticlePool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (dt < float.Epsilon)
        {
            return;
        }
        if (IsActive)
        {
            var (previousTimeline, previousTotal) = (TimelinePosition, total);
            total += dt;
            TimelinePosition += dt;
            switch (effect.Type)
            {
                case VisualEffectType.Looping:
                    TimelinePosition %= effect.Duration;
                    break;
                case VisualEffectType.Infinite:
                    TimelinePosition = MathF.Min(TimelinePosition, effect.Duration);
                    break;
                default:
                    if (TimelinePosition > effect.Duration)
                    {
                        Stop();
                    }
                    break;
            }
            var step = new EmitterStep(dt, previousTotal, total, TimelinePosition, previousTimeline, transform, lastTransform);
            foreach (var emitter in emitters)
            {
                emitter.Update(step, pool);
            }
        }
        lastTransform = transform;
    }
}

/// <summary>A visual effect a level starts with, by name, and where it stands in the world.</summary>
public sealed record PlacedEffect(string Effect, Matrix World)
{
    /// <summary>
    /// A placement in the space of <paramref name="parent"/> (identity for the level's own effects, a
    /// part's world transform for a part's): <c>CreateWorld(position, forward, up)</c> × parent, as the
    /// original starts them.
    /// </summary>
    public static PlacedEffect At(VisualEffectPlacement placement, Matrix parent)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return new PlacedEffect(placement.Effect, Matrix.CreateWorld(placement.Position, placement.Forward, Vector3.Up) * parent);
    }
}
