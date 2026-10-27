using System.Numerics;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The candle that lights the Tome's book (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: its
/// strength eases towards a target at a speed, with a bounded random wobble on it whose size eases too; the light's
/// three colours are its strength (×100: the effect divides by the squared distance) times warm ones.
/// </summary>
public sealed class Candle(Func<float> random)
{
    public static readonly Vector3 Ambient = new(0.3f, 0.2f, 0.2f), Diffuse = new(0.7f, 0.75f, 0.75f), Specular = new(1.3f, 1, 1);

    private readonly Func<float> random = random ?? throw new ArgumentNullException(nameof(random));
    private float speed = 24, amount = 1, intensity, wobble;
    private float targetSpeed = 6, targetAmount = 0.2f, targetIntensity;

    /// <summary>The candle from a seeded random (a uniform −1…1).</summary>
    public Candle(Random random)
        : this(() => (float)(random.NextDouble() * 2 - 1))
    {
    }

    /// <summary>The light's strength now: its intensity with the wobble, ×100.</summary>
    public float Strength => (intensity + wobble * amount) * 100;

    /// <summary>What the light goes towards: its intensity, the wobble's size, the speed it goes at.</summary>
    public void Aim(float intensity, float amount, float speed) => (targetIntensity, targetAmount, targetSpeed) = (intensity, amount, speed);

    public void Step(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            return;
        }
        speed += (targetSpeed - speed) * dt * 5;
        amount += (targetAmount - amount) * dt * 5;
        intensity += (targetIntensity - intensity) * dt * speed;
        wobble = Math.Clamp(wobble + random() * dt * speed, -1, 1);
    }
}
