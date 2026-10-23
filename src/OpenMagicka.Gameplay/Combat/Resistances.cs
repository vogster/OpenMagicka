using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>
/// A resistance (docs/gameplay/SPELLS.md §8): the magnitude is multiplied, the amount shifted; a
/// negative multiplier turns damage into healing. A status resistance forbids the element's status.
/// Note that <c>default(Resistance)</c> has a multiplier of 0: immunity.
/// </summary>
public readonly record struct Resistance(Elements Against, float Multiplier = 1, float Modifier = 0, bool StatusResistance = false);

/// <summary>One resistance per element, as a character template or a staff holds them. Immutable.</summary>
public sealed class ResistanceTable
{
    /// <summary>Each player beyond the first toughens enemy resistances by this share.</summary>
    public const float PlayerScaling = 0.00666f;

    public const int MaxPlayers = 4;

    private readonly Resistance[] entries;

    private ResistanceTable(Resistance[] entries) => this.entries = entries;

    public static ResistanceTable Neutral { get; } =
        new([.. Enumerable.Range(0, ElementIndex.Count).Select(i => new Resistance((Elements)(1 << i)))]);

    public Resistance this[Elements element] => entries[Index(element)];

    /// <summary>A copy with one element's resistance replaced.</summary>
    public ResistanceTable With(Elements element, float multiplier = 1, float modifier = 0, bool statusResistance = false)
    {
        var copy = (Resistance[])entries.Clone();
        copy[Index(element)] = new Resistance(element, Finite(multiplier, nameof(multiplier)), Finite(modifier, nameof(modifier)), statusResistance);
        return new ResistanceTable(copy);
    }

    /// <summary>
    /// An enemy template's resistances in a game of <paramref name="players"/>: positive values shrink
    /// by <c>k = 1 − (players − 1) · 0.00666</c>, negative ones grow by 1/k. The enemy takes less
    /// damage (a smaller multiplier and bonus) and heals more from what it absorbs.
    /// </summary>
    public ResistanceTable ScaledForPlayers(int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
        if (players == 1)
        {
            return this;
        }
        var k = 1 - (players - 1) * PlayerScaling;
        return new ResistanceTable([.. entries.Select(r => r with { Multiplier = Scale(r.Multiplier, k), Modifier = Scale(r.Modifier, k) })]);
    }

    private static float Scale(float value, float k) => value < 0 ? value / k : value * k;

    private static int Index(Elements element) => ElementIndex.IsSingle(element) && element != Elements.All
        ? ElementIndex.Of(element)
        : throw new ArgumentException($"{element} is not a single element", nameof(element));

    private static float Finite(float value, string name) =>
        float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must be finite");
}

/// <summary>How resistances change an entry of damage (docs/gameplay/SPELLS.md §8).</summary>
public static class Resistances
{
    /// <summary>
    /// A character's resistances: the template's and the staff's entry for each element of the damage,
    /// then each overlapping buff once, its multiplier keeping its sign. Armour piercing ignores
    /// whatever would protect (multipliers up to 1, modifiers up to 0). As in the original, each
    /// modifier checks the sign of the amount as it stands: a heal pushed to exactly 0 is then
    /// treated as damage by the next modifier, and so is an entry of amount 0.
    /// </summary>
    public static DamageEntry Apply(DamageEntry damage, ResistanceTable template, ResistanceTable? staff = null, IEnumerable<Resistance>? buffs = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        var piercing = damage.Attack.HasFlag(AttackProperties.ArmourPiercing);
        var state = new State(damage.Amount);
        for (var i = 0; i < ElementIndex.Count; i++)
        {
            var element = (Elements)(1 << i);
            if ((damage.Element & element) == 0)
            {
                continue;
            }
            state.Resist(template[element], piercing, life: element == Elements.Life, signed: false);
            if (staff is not null)
            {
                state.Resist(staff[element], piercing, life: element == Elements.Life, signed: false);
            }
        }
        foreach (var buff in buffs ?? [])
        {
            if (!float.IsFinite(buff.Multiplier) || !float.IsFinite(buff.Modifier))
            {
                throw new ArgumentOutOfRangeException(nameof(buffs), buff, "a resistance must be finite");
            }
            if ((buff.Against & damage.Element) != 0)
            {
                state.Resist(buff, piercing, life: (buff.Against & Elements.Life) != 0, signed: true);
            }
        }
        var amount = state.LifeInverted ? MathF.Abs(state.Amount) : state.Inverted ? -state.Amount : state.Amount;
        return damage with { Amount = amount, Magnitude = damage.Magnitude * state.Multiplier };
    }

    /// <summary>A physics prop's resistances: the largest multiplier among the damage's elements, and every modifier.</summary>
    public static DamageEntry ApplyInclusive(DamageEntry damage, ResistanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var multiplier = float.NegativeInfinity;
        var amount = damage.Amount;
        for (var i = 0; i < ElementIndex.Count; i++)
        {
            var element = (Elements)(1 << i);
            if ((damage.Element & element) != 0)
            {
                multiplier = MathF.Max(multiplier, table[element].Multiplier);
                amount = Shift(amount, table[element].Modifier);
            }
        }
        return float.IsNegativeInfinity(multiplier) ? damage : damage with { Amount = amount, Magnitude = damage.Magnitude * multiplier };
    }

    /// <summary>A modifier moves the amount towards or away from zero, but never across it.</summary>
    private static float Shift(float amount, float modifier) => amount >= 0 ? MathF.Max(0, amount + modifier) : MathF.Min(0, amount + modifier);

    private struct State(float amount)
    {
        public float Amount = amount;
        public float Multiplier = 1;
        public bool Inverted;
        public bool LifeInverted;

        public void Resist(Resistance resistance, bool piercing, bool life, bool signed)
        {
            if (!piercing || resistance.Multiplier > 1)
            {
                var negative = resistance.Multiplier < 0;
                LifeInverted |= negative && life;
                Inverted |= negative;
                Multiplier *= signed ? resistance.Multiplier : MathF.Abs(resistance.Multiplier);
            }
            if (!piercing || resistance.Modifier > 0)
            {
                Amount = Shift(Amount, resistance.Modifier);
            }
        }
    }
}
