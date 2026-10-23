namespace OpenMagicka.Gameplay.Spells;

/// <summary>How casting goes, whatever the spell (docs/gameplay/SPELLS.md §4).</summary>
public static class CastRules
{
    /// <summary>Damage and contact ticks of continuous effects, and the magnitude each one carries.</summary>
    public const float TickInterval = 0.25f;

    public const float TickMagnitude = 0.25f;

    /// <summary>Turning while charging is slowed to this.</summary>
    public const float ChargeTurnSpeed = 0.125f;

    /// <summary>The charge of a projectile cast by anything but a player (or a boss's spell zone, which charges too).</summary>
    public const float NonPlayerProjectileCharge = 0.5f;

    /// <summary>Effect pools hold this many instances of each type.</summary>
    public const int PoolSize = 16;

    /// <summary>A forward projectile and a forward or area push are charged before they go.</summary>
    public static bool Charges(SpellType type, CastType cast) =>
        (type == SpellType.Projectile && cast == CastType.Force)
        || (type == SpellType.Push && cast is CastType.Force or CastType.Area);

    /// <summary>The spell power while charging: up linearly over the charge clip, then full.</summary>
    public static float Charge(float elapsed, float clipDuration) =>
        clipDuration <= 0 ? 1 : Math.Clamp(elapsed / clipDuration, 0, 1);

    /// <summary>The minimum and maximum time an effect lives.</summary>
    public static (float Min, float Max) EffectLifetime(CastType cast) => cast switch
    {
        CastType.Force or CastType.Area or CastType.Weapon => (0.5f, 1.5f),
        CastType.Self => (0.2f, 0.2f),
        _ => throw new ArgumentOutOfRangeException(nameof(cast), cast, "no spell effect for this cast"),
    };

    /// <summary>Everything but projectiles goes on while the button is held.</summary>
    public static bool IsContinuous(SpellType type) => type != SpellType.Projectile;

    /// <summary>
    /// A continuous effect stops when the button was released after its minimum time, when the
    /// caster died, or when the caster is no longer casting.
    /// </summary>
    public static bool Interrupted(float alive, float minimum, bool held, bool casterAlive, bool stillCasting) =>
        (!held && alive >= minimum) || !casterAlive || !stillCasting;

    /// <summary>How fast the caster can turn while casting, as a share of the normal speed.</summary>
    public static float TurnSpeed(SpellType type) => type switch
    {
        SpellType.Spray => 0.6f,
        SpellType.Lightning or SpellType.Shield => 0.1f,
        SpellType.Beam => 0.0325f,
        SpellType.Push or SpellType.Projectile => 0,
        _ => 0.75f,
    };

    /// <summary>A player's weapon cast does not cast: it moves the queue into the held weapon.</summary>
    public static bool ImbuesWeapon(CastType cast, bool isAvatar) => cast == CastType.Weapon && isAvatar;

    /// <summary>
    /// Imbuing: each entry of the player's queue goes into the weapon's queue normalised to a total
    /// magnitude of 1, with the queue rules; the player's queue is emptied. A magick is not imbued.
    /// </summary>
    public static (ElementQueue Player, ElementQueue Weapon) Imbue(ElementQueue player, ElementQueue weapon)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(weapon);
        if (player.Entries.Any(e => e.Magick != MagickType.None))
        {
            throw new ArgumentException("a magick cannot be imbued", nameof(player));
        }
        var imbued = player.Entries.Aggregate(weapon, (queue, entry) => queue.Add(entry.Normalized()).Queue);
        return (player.Cleared(), imbued);
    }
}
