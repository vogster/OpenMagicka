namespace OpenMagicka.Gameplay.Spells;

public enum LightningPattern
{
    /// <summary>Along the caster's facing.</summary>
    Facing,
    /// <summary>All around the caster, a step of <see cref="LightningRules.CircleStep"/>.</summary>
    Circle,
    /// <summary>One bolt into the caster.</summary>
    Self,
}

/// <param name="MagnitudeScale">Each bolt carries the spell's damage times this.</param>
public sealed record LightningStrike(int BoltsPerSecond, float Range, float MagnitudeScale, LightningPattern Pattern);

/// <summary>Lightning (docs/gameplay/SPELLS.md §5.5). Range buffs of the caster apply on top.</summary>
public static class LightningRules
{
    /// <summary>The first target is sought this far either side of the bolt's direction; the chain goes on within <see cref="ChainSector"/>.</summary>
    public const float FirstTargetSector = MathF.PI / 2;

    public const float ChainSector = MathF.PI / 4;

    /// <summary>A bolt that hits nothing wanders up to this far off its direction.</summary>
    public const float MissWobble = MathF.PI / 16;

    public const float CircleStep = MathF.PI / 4;

    /// <summary>
    /// The strike for a cast. <paramref name="wet"/>: the caster is wet and has no item allowing
    /// lightning while wet; such a caster strikes all around with a range of 2.
    /// </summary>
    public static LightningStrike For(Spell spell, CastType cast, bool wet)
    {
        var strike = cast switch
        {
            CastType.Force or CastType.Weapon => new LightningStrike(8, 4 + 0.8f * spell.Lightning, 1f / 8, LightningPattern.Facing),
            CastType.Area => new LightningStrike(32, 2 + 0.4f * spell.Lightning, 1f / 16, LightningPattern.Circle),
            CastType.Self => new LightningStrike(1, 0, 1, LightningPattern.Self),
            _ => throw new ArgumentOutOfRangeException(nameof(cast), cast, "not a lightning cast"),
        };
        return wet && strike.Pattern != LightningPattern.Self ? strike with { Range = 2, Pattern = LightningPattern.Circle } : strike;
    }
}
