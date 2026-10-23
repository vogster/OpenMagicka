using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Combat.AttackProperties;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>
/// The damage a spell deals, by spell type and cast (docs/gameplay/SPELLS.md §6): up to five
/// entries. Most strengths grow with the square root of the magnitude; boulders with its square.
/// </summary>
public static class SpellDamage
{
    public const float Earth = 150;
    public const float IceEarth = 275;
    public const float Lightning = 250;
    public const float Life = -180;
    public const float Arcane = 225;
    public const float Fire = 60;
    public const float Cold = 25;
    public const float Steam = 280;
    public const float WaterStrength = 70;

    public const float BarrierLightning = 130;
    public const float BarrierIce = 275;
    public const float BarrierLife = -600;
    public const float BarrierArcane = 225;

    public const float SelfFire = 60;
    public const float SelfSteam = 80;
    public const float SelfLightning = 80;
    public const float SelfArcane = 200;

    /// <summary>Pushes and knockbacks of imbued weapons, lightning and shields.</summary>
    public const float ArcanePush = 120;
    public const float ShieldWaterPush = 75;
    public const float ShieldKnockback = 500;
    public const float ShieldKnockbackMagnitude = 2;

    /// <summary>The damage per second of a status, before resistances.</summary>
    public const float StatusBurn = 60;
    public const float StatusPoison = 50;
    public const float StatusLife = -80;

    /// <summary>
    /// The entries in the original's order. The original's literal empty slots are left out; an entry
    /// that happens to carry nothing (a Misc or StatusB block with nothing to do) is kept.
    /// </summary>
    public static IReadOnlyList<DamageEntry> For(Spell spell, SpellType type, CastType cast)
    {
        if (spell.Magick != MagickType.None)
        {
            throw new ArgumentException("a magick has no spell damage", nameof(spell));
        }
        var m = new Magnitudes(spell);
        var entries = new List<DamageEntry>(5);
        switch (type)
        {
            case SpellType.Spray:
                Spray(m, cast, entries);
                break;
            case SpellType.Projectile:
                Projectile(m, cast, entries);
                break;
            case SpellType.Shield:
                Shield(m, entries);
                break;
            case SpellType.Beam:
                Beam(m, cast, entries);
                break;
            case SpellType.Lightning:
                LightningStrike(m, entries);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "this spell type has no damage");
        }
        return entries;
    }

    private static float Scale(float strength, float magnitude) => strength * MathF.Sqrt(magnitude);

    private static float Min1(float magnitude) => MathF.Min(1, magnitude);

    private const Elements StatusMixers = Elements.Water | Elements.Cold | Elements.Fire | Elements.Steam | Elements.Poison;

    /// <summary>The statuses of sprays, beams and lightning: one entry for all of them, the same DPS each.</summary>
    private static void StatusMix(in Magnitudes m, List<DamageEntry> entries, bool waterHurts = false, bool steamWets = false)
    {
        if ((m.Element & ElementSets.StatusEffects) == 0 || (m.Element & StatusMixers) == 0)
        {
            return;
        }
        var attack = waterHurts && m.W > 0 ? Status | AttackProperties.Damage : Status;
        var elements = (m.Element & ElementSets.StatusEffects) | (steamWets && m.T > 0 ? Elements.Water : 0);
        entries.Add(new DamageEntry(attack, elements, StatusPoison * Min1(m.P) + StatusBurn * Min1(m.F), Min1(m.W + m.F + m.C + m.P + m.T)));
    }

    /// <summary>Healing over time; a spell on oneself heals for as long as its Life.</summary>
    private static void LifeStatus(in Magnitudes m, CastType cast, List<DamageEntry> entries)
    {
        if (m.L > 0)
        {
            entries.Add(new DamageEntry(Status, Elements.Life, Scale(StatusLife, m.L), cast == CastType.Self ? m.L : 1));
        }
    }

    /// <summary>The statuses of shields, imbued weapons and lightning ("CalculateStatus").</summary>
    private static DamageEntry StatusB(in Magnitudes m) => new(
        Status,
        (m.Element & (ElementSets.StatusEffects | Elements.Arcane)) | (m.T > 0 ? Elements.Water : 0),
        Scale(Fire, m.F),
        Min1(m.W + m.F + m.C + 0.5f * m.T));

    /// <summary>Pushes and knockdowns of imbued weapons and lightning.</summary>
    private static DamageEntry Misc(in Magnitudes m, CastType cast)
    {
        var attack = (m.Element & (Elements.Water | Elements.Arcane)) != 0 ? Pushed : AttackProperties.None;
        var elements = m.Element & Elements.Water;
        var amount = MathF.Truncate(WaterStrength * m.W);
        var magnitude = m.W;
        if (cast is CastType.Weapon or CastType.Area)
        {
            elements |= m.Element & (Elements.Earth | Elements.Arcane);
            amount += MathF.Truncate(ArcanePush * m.A);
            magnitude += m.E + m.A;
            attack |= m.E > 0 ? Knockdown : AttackProperties.None;
        }
        return new DamageEntry(attack, elements, amount, magnitude);
    }

    private static void Spray(in Magnitudes m, CastType cast, List<DamageEntry> entries)
    {
        if (cast == CastType.Magick)
        {
            return;
        }
        StatusMix(m, entries);
        LifeStatus(m, cast, entries);
        const Elements direct = Elements.Cold | Elements.Fire | Elements.Lightning | Elements.Steam;
        if ((m.Element & direct) != 0)
        {
            var self = cast == CastType.Self;
            var amount = Scale(self ? SelfFire : Fire, m.F) + Scale(Cold, m.C) + Scale(self ? SelfSteam : Steam, m.T);
            entries.Add(new DamageEntry(AttackProperties.Damage, m.Element & direct, amount, Min1(m.F + m.C + m.T + m.Z)));
        }
        if (m.W > 0 && cast != CastType.Self)
        {
            entries.Add(new DamageEntry(Pushed, Elements.Water, MathF.Truncate(WaterStrength * m.W), cast == CastType.Weapon ? m.W : 1 + m.W / 4));
            entries.Add(new DamageEntry(AttackProperties.Damage, Elements.Water, 0, 1));    // marks the target wet for lightning
        }
    }

    private static void Projectile(in Magnitudes m, CastType cast, List<DamageEntry> entries)
    {
        switch (cast)
        {
            case CastType.Force:
                ForceProjectile(m, entries);
                break;
            case CastType.Area:
                AreaProjectile(m, entries);
                break;
            case CastType.Weapon:
                if ((m.Element & Elements.Ice) != 0)
                {
                    entries.Add(new DamageEntry(AttackProperties.Damage | Piercing, Elements.Earth, 2 * Scale(IceEarth, m.I) + Scale(Earth, m.E), 1));
                }
                entries.Add(Misc(m, cast));
                entries.Add(StatusB(m));
                break;
        }
    }

    private static void ForceProjectile(in Magnitudes m, List<DamageEntry> entries)
    {
        if (m.E > 0)
        {
            var elements = (m.Element & ElementSets.Physical) | (m.I > 0 ? Elements.Cold : 0);
            entries.Add(new DamageEntry(AttackProperties.Damage, elements, Earth * m.E * m.E + IceEarth * m.I * m.I, 1));
        }
        else if (m.I > 0)
        {
            var amount = Scale(IceEarth, m.I) + Scale(Lightning, m.Z) + Scale(Arcane, m.A) + 2 * Scale(Life, m.L);   // Life twice, as the original
            entries.Add(new DamageEntry(AttackProperties.Damage, m.Element | Elements.Cold | Elements.Earth, amount, 1));
        }
        if (m.I > 0 && (m.Element & ElementSets.StatusEffects) != 0)
        {
            entries.Add(new DamageEntry(Status, m.Element & ElementSets.StatusEffects,
                StatusPoison * Min1(m.P) + StatusLife * Min1(m.L), Min1(m.W + m.C + m.P + m.L)));
        }
    }

    private static void AreaProjectile(in Magnitudes m, List<DamageEntry> entries)
    {
        const Elements statusSources = Elements.Water | Elements.Cold | Elements.Fire | Elements.Steam;
        if ((m.Element & ElementSets.StatusEffects) != 0 && (m.Element & statusSources) != 0)
        {
            entries.Add(new DamageEntry(Status, m.Element & ElementSets.StatusEffects, StatusBurn * Min1(m.F), Min1(m.W + m.F + m.C + m.T)));
        }
        LifeStatus(m, CastType.Area, entries);
        var amount = Scale(Fire, m.F) + Scale(Cold, m.C) + Scale(Steam, m.T) + Scale(Lightning, m.Z)
            + Scale(BarrierIce, m.I) + Scale(Life, m.L) + Scale(Arcane, m.A);
        entries.Add(new DamageEntry(AttackProperties.Damage, (m.Element & ElementSets.Instant) | Elements.Earth, amount,
            Min1(m.F + m.C + m.T + m.Z + m.I + m.A)));                                // Fire and Cold hurt, but are not in the mask
        if (m.W > 0)
        {
            entries.Add(new DamageEntry(Pushed, Elements.Water, MathF.Truncate(WaterStrength * m.W), m.W));
        }
        if (m.E > 0)
        {
            entries.Add(new DamageEntry(Knockdown, Elements.Earth, MathF.Truncate(Earth * m.E), m.E));
        }
    }

    private static void Shield(in Magnitudes m, List<DamageEntry> entries)
    {
        if (m.L > 0)
        {
            entries.Add(new DamageEntry(AttackProperties.Damage, m.Element & ElementSets.Beams, BarrierLife, 1));
        }
        else if (m.A > 0)
        {
            entries.Add(new DamageEntry(AttackProperties.Damage, Elements.Arcane, BarrierArcane, 1));
        }
        if ((m.Element & ElementSets.StatusEffects) != 0)
        {
            entries.Add(StatusB(m));
        }
        if (m.W > 0 && (m.E > 0 || m.I > 0))
        {
            var root = MathF.Sqrt(m.W);
            entries.Add(new DamageEntry(Pushed, Elements.Water, MathF.Truncate(ShieldWaterPush * root), root));
        }
        if (m.Z > 0)
        {
            entries.Add(new DamageEntry(AttackProperties.Damage, Elements.Lightning, Scale(BarrierLightning, m.Z), 1));
        }
        if (m.E + m.I <= 0 && m.A > 0)
        {
            entries.Add(new DamageEntry(Knockback, Elements.None, ShieldKnockback, ShieldKnockbackMagnitude));
        }
    }

    private static void Beam(in Magnitudes m, CastType cast, List<DamageEntry> entries)
    {
        StatusMix(m, entries, waterHurts: true, steamWets: true);
        LifeStatus(m, cast, entries);
        var self = cast == CastType.Self;
        var amount = Scale(self ? SelfFire : Fire, m.F) + Scale(Cold, m.C) + Scale(self ? SelfLightning : Lightning * 0.5f, m.Z)
            + Scale(self ? SelfSteam : Steam, m.T) + Life * Min1(m.L) + (self ? SelfArcane : Arcane) * Min1(m.A);   // more Arcane or Life lasts longer, not harder
        entries.Add(new DamageEntry(AttackProperties.Damage, m.Element & (ElementSets.InstantNonPhysical | Elements.Cold | Elements.Fire), amount, 1));
        if (m.W > 0)
        {
            entries.Add(new DamageEntry(Pushed, Elements.Water, MathF.Truncate(WaterStrength * m.W), m.W));
        }
    }

    private static void LightningStrike(in Magnitudes m, List<DamageEntry> entries)
    {
        entries.Add(new DamageEntry(AttackProperties.Damage, m.Element, Scale(Lightning, m.Z) + Scale(Arcane, m.A) + Scale(Life, m.L), 1));
        StatusMix(m, entries, waterHurts: true);
        entries.Add(Misc(m, CastType.Area));
        if ((m.Element & ElementSets.StatusEffects) != 0 || m.T > 0)
        {
            entries.Add(StatusB(m));
        }
    }

    /// <summary>The spell's magnitudes, none below zero (and no NaN).</summary>
    private readonly struct Magnitudes(Spell spell)
    {
        public readonly Elements Element = spell.Element;
        public readonly float E = Positive(spell.Earth), W = Positive(spell.Water), C = Positive(spell.Cold), F = Positive(spell.Fire);
        public readonly float Z = Positive(spell.Lightning), A = Positive(spell.Arcane), L = Positive(spell.Life);
        public readonly float I = Positive(spell.Ice), T = Positive(spell.Steam), P = Positive(spell.Poison);

        private static float Positive(float magnitude) => magnitude > 0 ? magnitude : 0;
    }
}
