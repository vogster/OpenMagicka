namespace OpenMagicka.Engine.Hud;

/// <summary>
/// The boss fight's health bar at the top of the screen (docs/engine/ARCHITECTURE.md §19): while the fight runs it fades
/// in over 2 s empty, then fills to the bosses' share (closing in at 10 a second); when they are down it fades out.
/// </summary>
public sealed class BossBar
{
    public const float FadeSpeed = 0.5f, FillSpeed = 10;

    public float Alpha { get; private set; }

    /// <summary>The share the bar shows (it eases to the real one).</summary>
    public float Shown { get; private set; }

    public bool IsShown => Alpha > 0;

    public void Update(float dt, bool running, float share)
    {
        var going = !running || share <= 0;
        if ((going && Alpha <= 0) || (!running && share > 0))
        {
            (Alpha, Shown) = (0, 0);                                          // gone, not begun or reset: it starts again empty
            return;
        }
        Alpha = going ? MathF.Max(0, Alpha - FadeSpeed * dt) : MathF.Min(1, Alpha + FadeSpeed * dt);
        if (Alpha > 0.99f || going)
        {
            Shown += (Math.Clamp(share, 0, 1) - Shown) * MathF.Min(1, FillSpeed * dt);
        }
    }
}
