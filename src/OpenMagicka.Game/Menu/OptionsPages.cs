using System.Globalization;
using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;
using OpenMagicka.Game.Install;

namespace OpenMagicka.Game.Menu;

/// <summary>The options page (docs/engine/ARCHITECTURE.md §20): Game, Sound and Graphics, one a line in the page's middle.</summary>
public sealed class OptionsPage : MenuPage
{
    public const float Top = 341, Step = 48;

    private static readonly string[] Labels = ["#menu_opt_01", "#menu_opt_03", "#menu_opt_04"];

    public OptionsPage(TomeMenu menu)
        : base(menu)
    {
        Items.Add(new MenuItem(Line(0), () => Menu.Push(SettingsPage.Game(Menu))));
        Items.Add(new MenuItem(Line(1), () => Menu.Push(SettingsPage.Sound(Menu))));
        Items.Add(new MenuItem(Line(2), () => Menu.Push(SettingsPage.Graphics(Menu))));
        Items.Add(new MenuItem(BackPlate, Back));
        Selected = 0;
    }

    private static Rectangle Line(int row) => new(256, (int)(Top + row * Step), 512, (int)Step);

    public override void Draw(MenuPicture picture)
    {
        Title(picture, "#menu_main_06");
        for (var row = 0; row < Labels.Length; row++)
        {
            Text(picture, ItemFont, Menu.Text(Labels[row]), new Vector2(512, Top + row * Step), ColourOf(row));
        }
        BackArt(picture, Labels.Length);
    }
}

/// <summary>A setting's row: its label's key, its value as shown, and how it steps (−1 or +1) or is chosen.</summary>
public sealed record SettingRow(string Label, Func<GameSettings, string> Value, Func<GameSettings, int, GameSettings>? Turn, Action? Choose = null,
    string ValueFont = MenuPage.ItemFont)
{
    /// <summary>A setting with a range (a volume): shown between arrows, a click on the left one stepping down.</summary>
    public bool Ranged { get; init; }
}

/// <summary>
/// A page of settings (docs/engine/ARCHITECTURE.md §20): labels right-aligned to the page's middle, values left of
/// it; left and right (or a click) step a value, and each change is applied and saved at once.
/// </summary>
public sealed class SettingsPage : MenuPage
{
    public const float Top = 341, Step = 48, LabelX = 512, ValueX = 552;

    /// <summary>A ranged value's middle right of its start (between "&lt; 10 &gt;"'s arrows): a click left of it steps down.</summary>
    public const float RangeMiddle = 40;

    private readonly string title;
    private readonly IReadOnlyList<SettingRow> rows;

    private SettingsPage(TomeMenu menu, string title, Func<SettingsPage, IReadOnlyList<SettingRow>> rows)
        : base(menu)
    {
        this.title = title;
        this.rows = rows(this);
        for (var row = 0; row < this.rows.Count; row++)
        {
            var setting = this.rows[row];
            Items.Add(new MenuItem(new Rectangle(128, (int)(Top + row * Step), 768, (int)Step), setting.Choose,
                setting.Turn is { } turn ? way => Menu.Host.Apply(turn(Menu.Host.Settings, way)) : null)
            {
                Middle = setting.Ranged ? ValueX + RangeMiddle : null,
            });
        }
        Items.Add(new MenuItem(BackPlate, Back));
        Selected = 0;
    }

    private string OnOff(bool on) => Menu.Text(on ? "#menu_opt_alt_01" : "#menu_opt_alt_02");

    /// <summary>A ranged value between its arrows ("&lt; 7 &gt;"), the number in the middle.</summary>
    private static string Ranged(int value) => "<  " + value.ToString(CultureInfo.InvariantCulture).PadLeft(2) + "  >";

    private static int Stepped(int value, int way, int count) => ((value + way) % count + count) % count;

    public static SettingsPage Game(TomeMenu menu) => new(menu, "#menu_opt_12", page =>
    [
        new SettingRow("#menu_opt_game_02", s => page.OnOff(s.DamageNumbers), (s, _) => s with { DamageNumbers = !s.DamageNumbers }),
        new SettingRow("#menu_opt_game_03", s => page.Menu.Text(s.HealthBars switch
        {
            HealthBarOption.PlayersOnly => "#menu_opt_alt_03",
            HealthBarOption.Off => "#menu_opt_alt_02",
            _ => "#menu_opt_alt_01",
        }), (s, way) => s with { HealthBars = (HealthBarOption)Stepped((int)s.HealthBars, way, 3) }),
        new SettingRow("#menu_opt_game_04", s => page.OnOff(s.SpellWheel), (s, _) => s with { SpellWheel = !s.SpellWheel }),
        new SettingRow("#menu_opt_game_05", s => LanguageName(s.Language), null, page.ChooseLanguage, MenuPass.LanguageFont),
    ]);

    public static SettingsPage Sound(TomeMenu menu) => new(menu, "#menu_opt_08", page =>
    [
        new SettingRow("#menu_opt_sfx_01", s => Ranged(s.MusicVolume),
            (s, way) => s with { MusicVolume = Math.Clamp(s.MusicVolume + way, 0, GameSettings.MaxVolume) }) { Ranged = true },
        new SettingRow("#menu_opt_sfx_02", s => Ranged(s.SoundVolume), (s, way) =>
        {
            page.Menu.Cue("misc_thud01");
            return s with { SoundVolume = Math.Clamp(s.SoundVolume + way, 0, GameSettings.MaxVolume) };
        }) { Ranged = true },
    ]);

    public static SettingsPage Graphics(TomeMenu menu) => new(menu, "#menu_opt_10", page =>
    [
        new SettingRow("#menu_opt_gfx_01", s => page.OnOff(!s.Fullscreen), (s, _) => s with { Fullscreen = !s.Fullscreen }),
        new SettingRow("#menu_opt_gfx_02", s => s.Width is { } w && s.Height is { } h ? $"{w}x{h}" : "-", null, page.ChooseResolution),
        new SettingRow("#menu_vsync", s => page.OnOff(s.VSync), (s, _) => s with { VSync = !s.VSync }),
    ]);

    /// <summary>The languages' own names, as the original's language box shows them.</summary>
    public static string LanguageName(string folder) => folder switch
    {
        "eng" => "English",
        "deu" => "Deutsch",
        "fra" => "Fran" + (char)0xE7 + "ais",
        "hun" => "Magyar",
        "ita" => "Italiano",
        "pol" => "Polski",
        "rus" => new string([(char)0x420, (char)0x443, (char)0x441, (char)0x441, (char)0x43A, (char)0x438, (char)0x439]),
        "spa" => "Espa" + (char)0xF1 + "ol",
        _ => folder,
    };

    private void ChooseLanguage()
    {
        var languages = Menu.Host.Languages;
        var current = languages.ToList().IndexOf(Menu.Host.Settings.Language);
        Menu.Ask("", languages.Select(LanguageName).ToList(), Math.Max(0, current), chosen =>
        {
            if (chosen is { } at)
            {
                Menu.Host.Apply(Menu.Host.Settings with { Language = languages[at] });
            }
        }, MenuPass.LanguageFont);
    }

    private void ChooseResolution()
    {
        var sizes = Menu.Host.Resolutions;
        var settings = Menu.Host.Settings;
        var current = sizes.ToList().FindIndex(p => p.X == settings.Width && p.Y == settings.Height);
        Menu.Ask("", sizes.Select(p => $"{p.X}x{p.Y}").ToList(), Math.Max(0, current), chosen =>
        {
            if (chosen is { } at)
            {
                Menu.Host.Apply(Menu.Host.Settings with { Width = sizes[at].X, Height = sizes[at].Y });
            }
        });
    }

    public override void Draw(MenuPicture picture)
    {
        Title(picture, title);
        var settings = Menu.Host.Settings;
        for (var row = 0; row < rows.Count; row++)
        {
            var y = Top + row * Step;
            Text(picture, ItemFont, Menu.Text(rows[row].Label), new Vector2(LabelX, y), ColourOf(row), TextAlignment.Right);
            Text(picture, rows[row].ValueFont, rows[row].Value(settings), new Vector2(ValueX, y), ColourOf(row), TextAlignment.Left);
        }
        BackArt(picture, rows.Count);
    }
}
