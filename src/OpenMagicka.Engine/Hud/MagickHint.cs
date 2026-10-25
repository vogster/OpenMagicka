using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Hud;

/// <summary>
/// The selected magick under a wizard's queue (docs/engine/ARCHITECTURE.md §19): its name, and its recipe as
/// translucent icons where the queue has not reached yet. A new selection fades the old one out at 4 a second, then
/// itself in.
/// </summary>
public sealed class MagickHint
{
    public const float FadeSpeed = 4;

    public MagickType Shown { get; private set; }

    public float Alpha { get; private set; }

    /// <summary>The magick's recipe (none for no magick).</summary>
    public IReadOnlyList<Elements> Recipe => MagickBook.Sequences.GetValueOrDefault(Shown) ?? [];

    public void Update(float dt, MagickType selected)
    {
        if (selected != Shown)
        {
            Alpha = MathF.Max(0, Alpha - FadeSpeed * dt);
            if (Alpha <= 0)
            {
                Shown = selected;
            }
        }
        else if (Shown != MagickType.None)
        {
            Alpha = MathF.Min(1, Alpha + FadeSpeed * dt);
        }
    }

    /// <summary>A magick's name's loc key, as the original names them ("#magick_haste"; the enchantment's "#magick_performance").</summary>
    public static string NameKey(MagickType magick) =>
        magick == MagickType.PerformanceEnchantment ? "#magick_performance" : "#magick_" + magick.ToString().ToLowerInvariant();
}
