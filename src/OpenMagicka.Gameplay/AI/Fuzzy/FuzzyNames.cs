using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>The original's names for elements and statuses in expressions: any case, several joined by | (or a number).</summary>
internal static class FuzzyNames
{
    private static readonly Dictionary<string, int> ElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"] = 0, ["Earth"] = 0x1, ["Physical"] = 0x1, ["Water"] = 0x2, ["Cold"] = 0x4, ["Fire"] = 0x8, ["Lightning"] = 0x10,
        ["Arcane"] = 0x20, ["Life"] = 0x40, ["Shield"] = 0x80, ["Ice"] = 0x100, ["Steam"] = 0x200, ["Poison"] = 0x400,
        ["Offensive"] = 0xFF3F, ["Defensive"] = 0xB0, ["All"] = 0xFFFF, ["Magick"] = 0xFFFF, ["Basic"] = 0xFF, ["Instant"] = 0x371,
        ["InstantPhysical"] = 0x171, ["InstantNonPhysical"] = 0x270, ["StatusEffects"] = 0x64E, ["ShieldElements"] = 0xE0,
        ["PhysicalElements"] = 0x101, ["Beams"] = 0x60,
    };

    private static readonly Dictionary<string, int> StatusNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"] = 0, ["Burning"] = 1, ["Wet"] = 2, ["Frozen"] = 4, ["Cold"] = 8, ["Poisoned"] = 0x10, ["Healing"] = 0x20, ["Life"] = 0x20,
        ["Greased"] = 0x40, ["Steamed"] = 0x80, ["Bleeding"] = 0x100, ["NumberOfTypes"] = 0x200,
    };

    public static Elements Elements(string text) => (Elements)Flags(text, ElementNames, "element");

    public static StatusEffects Status(string text) => (StatusEffects)Flags(text, StatusNames, "status");

    private static int Flags(string text, Dictionary<string, int> names, string what)
    {
        var value = 0;
        foreach (var part in text.Split('|', ','))
        {
            var name = part.Trim();
            if (names.TryGetValue(name, out var flag) || int.TryParse(name, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out flag))
            {
                value |= flag;
            }
            else
            {
                throw new FormatException($"no {what} \"{name}\"");
            }
        }
        return value;
    }
}
