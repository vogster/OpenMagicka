using System.Numerics;

namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// The colours the spells are drawn and lit with (docs/gameplay/SPELLS.md §14), as the original's: one per
/// element (above 1 where it glows), and a spell's own colour, an average over its elements.
/// </summary>
public static class SpellColours
{
    public static readonly Vector3 Arcane = new(2, 0.4f, 0.6f);
    public static readonly Vector3 Life = new(0.2f, 1.6f, 0.2f);
    public static readonly Vector3 Lightning = new(0.75f, 0.5f, 1);
    public static readonly Vector3 Fire = new(1.8f, 0.6f, 0.4f);
    public static readonly Vector3 Cold = new(1, 1, 1.4f);
    public static readonly Vector3 Water = new(0, 0.7f, 1.3f);
    public static readonly Vector3 Ice = new(0.8f, 0.9f, 1.4f);
    public static readonly Vector3 Earth = new(0.3f, 0.2f, 0.1f);
    public static readonly Vector3 Shield = new(2, 1.5f, 1);

    /// <summary>
    /// A spell's colour: Lightning and Fire count by their magnitude, every other element it has (but Steam
    /// and Poison, which have none) once; black for a spell with none of them.
    /// </summary>
    public static Vector3 Of(Spell spell)
    {
        var (sum, weight) = (Vector3.Zero, 0f);
        void Add(Vector3 colour, float amount)
        {
            if (amount > 0)
            {
                (sum, weight) = (sum + colour * amount, weight + amount);
            }
        }
        Add(Lightning, spell.Lightning);
        Add(Fire, spell.Fire);
        Add(Cold, Once(spell.Cold));
        Add(Life, Once(spell.Life));
        Add(Arcane, Once(spell.Arcane));
        Add(Water, Once(spell.Water));
        Add(Ice, Once(spell.Ice));
        Add(Earth, Once(spell.Earth));
        Add(Shield, Once(spell.Shield));
        return weight > 0 ? sum / weight : Vector3.Zero;
    }

    private static float Once(float magnitude) => magnitude > 0 ? 1 : 0;
}
