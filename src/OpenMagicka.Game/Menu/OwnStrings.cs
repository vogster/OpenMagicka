using OpenMagicka.Content.Localization;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The texts of OpenMagicka's own options, which the original lacks (OM-87): added under the game's strings, in the
/// language's words where we have them, else in English's. Keys start "#om_".
/// </summary>
public static class OwnStrings
{
    public const string InterfaceSize = "#om_interface_size", Auto = "#om_auto";

    private static readonly Dictionary<string, Dictionary<string, string>> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [LanguagePack.Fallback] = new() { [InterfaceSize] = "Interface Size", [Auto] = "Auto" },
        ["rus"] = new() { [InterfaceSize] = "Размер интерфейса", [Auto] = "Авто" },
        ["deu"] = new() { [InterfaceSize] = "Oberflächengröße", [Auto] = "Auto" },
        ["fra"] = new() { [InterfaceSize] = "Taille de l'interface", [Auto] = "Auto" },
        ["spa"] = new() { [InterfaceSize] = "Tamaño de la interfaz", [Auto] = "Auto" },
        ["ita"] = new() { [InterfaceSize] = "Dimensione interfaccia", [Auto] = "Auto" },
        ["pol"] = new() { [InterfaceSize] = "Rozmiar interfejsu", [Auto] = "Auto" },
        ["hun"] = new() { [InterfaceSize] = "Felület mérete", [Auto] = "Auto" },
    };

    /// <summary>Our texts in a language (English's for a key or a language without them).</summary>
    public static IEnumerable<LocEntry> For(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        var own = Languages.GetValueOrDefault(language) ?? [];
        return Languages[LanguagePack.Fallback].Select(e => new LocEntry(e.Key, own.GetValueOrDefault(e.Key) ?? e.Value));
    }
}
