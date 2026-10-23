namespace OpenMagicka.Engine.Rendering;

/// <summary>Checks made when a pass is built, so a different effect fails there (and the viewer falls back) rather than in every frame.</summary>
internal static class EffectChecks
{
    public static void Require(GameEffect effect, IEnumerable<string> names)
    {
        var parameters = new FnaEffectParameters(effect.Effect);
        var missing = names.Where(name => !parameters.Contains(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"an effect has no parameter {string.Join(", ", missing)}");
        }
    }
}
