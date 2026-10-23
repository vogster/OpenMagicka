namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// A forward projectile cast (docs/gameplay/SPELLS.md §5.3): one boulder with Earth, otherwise ice
/// shards, fired <see cref="Interval"/> apart.
/// </summary>
/// <param name="Count">How many projectiles; 0 when there is too little Ice for a shard.</param>
/// <param name="Speed">The launch speed of the last projectile; the others fly at <see cref="ProjectileRules.OtherSpeedFactor"/> times it.</param>
/// <param name="MagnitudeScale">Each projectile's share of the spell's damage.</param>
/// <param name="RemainderType">A boulder blasts with the damage of this type, <see cref="RemainderBlastRadius"/> wide, where it hits; null when it does not blast.</param>
/// <param name="Pierces">A boulder that does not blast goes through this many targets.</param>
/// <param name="Model">The missile model under Models/Missiles.</param>
public sealed record ProjectileVolley(
    int Count, float Speed, float Mass, float Radius, float Interval, float Lifetime, float MagnitudeScale,
    SpellType? RemainderType, float RemainderBlastRadius, int Pierces, string Model);

public static class ProjectileRules
{
    public const float Lifetime = 10;

    public const int BoulderPierces = 25;

    /// <summary>The underground wave of an Earth weapon cast.</summary>
    public const float WaveSpeed = 40;

    public const float WaveRange = 15;

    /// <summary>A stone's first damage entry scales with its speed over this.</summary>
    public const float StoneSpeedDivisor = 50;

    private static readonly string[] EarthModels = ["Earth0", "Earth1", "Earth2", "Earth2", "Earth3"];
    private static readonly string[] IceEarthModels = ["IceEarth0", "IceEarth1", "IceEarth2", "IceEarth2", "IceEarth3"];

    /// <summary>
    /// The volley for a forward cast at the given charge (spell power). A boulder's blast is sized
    /// by the type of its elements without Earth, on all the magnitudes: with Ice it is a projectile
    /// blast sized by Earth or Ice, with Arcane a beam blast, with a spray element a spray blast;
    /// a blast of no size (Earth alone, or with Lightning) lets the boulder pierce instead.
    /// </summary>
    public static ProjectileVolley Force(Spell spell, float spellPower)
    {
        if (spell.Type != SpellType.Projectile)
        {
            throw new ArgumentException("not a projectile spell", nameof(spell));
        }
        var elements = spell.Element;
        var boulder = (elements & Elements.Earth) != 0;
        var count = boulder ? 1 : (int)(3 * spell.Ice);
        var speed = 25 + 85 * spellPower;
        if (boulder)
        {
            speed *= 0.5f + 0.5f / spell.Earth;
        }
        var remainderType = SpellTypes.Of(elements & ~Elements.Earth);
        var splash = boulder ? spell.BlastSizeAs(remainderType) * 10 : 0;
        return new ProjectileVolley(
            count, speed,
            Mass: 1 + 50 * spell.Earth,
            Radius: 0.075f + 0.075f * spell.Earth,
            Interval: count == 0 ? 0 : (0.0875f + spell.TotalMagnitude / 5 * 0.2625f) / count,
            Lifetime,
            MagnitudeScale: count == 0 ? 0 : 1f / count,
            RemainderType: splash > 0 ? remainderType : null,
            RemainderBlastRadius: splash,
            Pierces: boulder && splash <= 0 ? BoulderPierces : 0,
            Model(spell));
    }

    /// <summary>All but the last projectile turn up to this far either way at random: less with more charge.</summary>
    public static float Spread(float spellPower) => MathF.PI / 4 * (1 - spellPower);

    /// <summary>The speed factor of every projectile but the last, for a uniform random value in [0, 1).</summary>
    public static float OtherSpeedFactor(float random) => 1.25f + 0.75f * random;

    /// <summary>An area cast's ground blast: no falloff, and levitating targets are spared.</summary>
    public static float AreaRadius(Spell spell) => spell.BlastSize * 10;

    /// <summary>The ice blade of a weapon cast with Ice.</summary>
    public static float IceBladeLength(Spell spell) => 3.75f + 1.25f * spell.Ice;

    private static string Model(Spell spell)
    {
        var (earth, ice) = ((int)spell.Earth, (int)spell.Ice);
        return ((spell.Element & Elements.Earth) != 0, (spell.Element & Elements.Ice) != 0) switch
        {
            (true, true) => IceEarthModels[Math.Clamp(ice + earth - 1, 0, 4)],
            (true, false) => EarthModels[Math.Clamp(earth - 1, 0, 4)],
            _ => "Ice3",
        };
    }
}
