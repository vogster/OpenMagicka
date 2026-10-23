namespace OpenMagicka.Gameplay.Spells;

/// <summary>Blasts (docs/gameplay/SPELLS.md §5.8).</summary>
public static class BlastRules
{
    /// <summary>A ground blast shakes the camera this hard for <see cref="GroundShakeDuration"/>; a full blast shakes it radius/3 for <see cref="FullShakeDuration"/>.</summary>
    public const float GroundShake = 2;

    public const float GroundShakeDuration = 0.9f;

    public const float FullShakeDuration = 0.3f;

    /// <summary>
    /// A full blast's magnitude at a distance (to the centre, minus the target's radius):
    /// <c>1 − (d / radius)³</c>, or no falloff for damage holding Arcane or Life. A blast of no
    /// size reaches only what touches its centre.
    /// </summary>
    public static float Falloff(float distance, float radius, bool arcaneOrLife)
    {
        if (arcaneOrLife)
        {
            return 1;
        }
        if (radius <= 0)
        {
            return distance <= 0 ? 1 : 0;
        }
        return 1 - MathF.Pow(Math.Clamp(distance / radius, 0, 1), 3);
    }

    public static float FullShake(float radius) => radius / 3;
}
