using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

/// <summary>Everything that resists for a character: its template, its staff (avatars) and its resistance buffs.</summary>
public sealed record Defences(ResistanceTable Template, ResistanceTable? Staff = null, IReadOnlyList<Resistance>? Buffs = null)
{
    public static Defences None { get; } = new(ResistanceTable.Neutral);

    /// <summary>An attack's entry after the resistances (docs/gameplay/SPELLS.md §8).</summary>
    public DamageEntry Resist(DamageEntry damage) => Resistances.Apply(damage, Template, Staff, Buffs);

    /// <summary>Only the template's status resistance forbids a status.</summary>
    public bool ForbidsStatus(Elements element) => Template[element].StatusResistance;

    /// <summary>
    /// The resistance a new status of <paramref name="element"/> meets: the template's multiplier ×
    /// the staff's × every buff that covers the element whole; the modifiers added up the same way.
    /// </summary>
    public (float Multiplier, float Modifier) ForStatus(Elements element)
    {
        var (multiplier, modifier) = (Template[element].Multiplier, Template[element].Modifier);
        if (Staff is not null)
        {
            (multiplier, modifier) = (multiplier * Staff[element].Multiplier, modifier + Staff[element].Modifier);
        }
        foreach (var buff in Covering(element))
        {
            (multiplier, modifier) = (multiplier * buff.Multiplier, modifier + buff.Modifier);
        }
        return (multiplier, modifier);
    }

    /// <summary>
    /// A rough measure for AI and some magicks: <c>1 − clamp(Σmodifier/300 + Σmultiplier, −1, 1)</c>
    /// over the elements, 0 for no resistance, 1 for immunity. As in the original, frost takes 350 off
    /// every element's modifier when the query includes Earth, and grease doubles every multiplier
    /// when it includes Fire.
    /// </summary>
    public float ResistanceAgainst(Elements elements, StatusEffects statuses)
    {
        var (multipliers, modifiers) = (0f, 0f);
        for (var i = 0; i < ElementIndex.Count; i++)
        {
            var element = (Elements)(1 << i);
            if ((elements & element) == 0)
            {
                continue;
            }
            var (multiplier, modifier) = (Template[element].Multiplier, Template[element].Modifier);
            modifier -= statuses.HasFlag(StatusEffects.Frozen) && elements.HasFlag(Elements.Earth) ? 350 : 0;
            multiplier *= statuses.HasFlag(StatusEffects.Greased) && elements.HasFlag(Elements.Fire) ? 2 : 1;
            if (Staff is not null)
            {
                (multiplier, modifier) = (multiplier * Staff[element].Multiplier, modifier + Staff[element].Modifier);
            }
            foreach (var buff in Covering(element))
            {
                (multiplier, modifier) = (multiplier * buff.Multiplier, modifier + buff.Modifier);
            }
            (multipliers, modifiers) = (multipliers + multiplier, modifiers + modifier);
        }
        return 1 - Math.Clamp(modifiers / 300 + multipliers, -1, 1);
    }

    private IEnumerable<Resistance> Covering(Elements element) => (Buffs ?? []).Where(b => (b.Against & element) == element).Select(Checked);

    private static Resistance Checked(Resistance buff) =>
        float.IsFinite(buff.Multiplier) && float.IsFinite(buff.Modifier) ? buff : throw new ArgumentOutOfRangeException(nameof(Buffs), buff, "a resistance must be finite");
}
