using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The original HUD's floating numbers and pad wheel (docs/engine/ARCHITECTURE.md §19). A number is its glyphs from
/// <c>UI/HUD/Digits</c> (22 × 32 each: 0–9, then '+'), scaled and centred on its projected point. The wheel's
/// icons are 50 × 50 around the projected centre of the wizard.
/// </summary>
public sealed partial class GuiPass
{
    public const string DigitsAtlas = "UI/HUD/Digits";
    public const float DigitWidth = 22, DigitHeight = 32, WheelIcon = 50;

    private Texture2D digits = null!;

    /// <summary>A glyph's cell: a digit's, or the plus sign's after them; none for another character.</summary>
    public static int? DigitCell(char glyph) => glyph is >= '0' and <= '9' ? glyph - '0' : glyph == '+' ? 10 : null;

    /// <summary>A number's glyph quads, centred on its projected point.</summary>
    public static void Number(Hud.FloatingNumber number, Vector2 at, int atlasWidth, int atlasHeight, List<GuiSprite> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var (glyph, height) = (DigitWidth * number.Scale, DigitHeight * number.Scale);
        var left = at.X - glyph * number.Text.Length / 2;
        var colour = new Vector4(number.Colour.X, number.Colour.Y, number.Colour.Z, number.Alpha);
        for (var i = 0; i < number.Text.Length; i++)
        {
            if (DigitCell(number.Text[i]) is { } cell)
            {
                var from = new Vector2(left + i * glyph, at.Y - height / 2);
                into.Add(new GuiSprite(from, from + new Vector2(glyph, height), new Vector2(cell * DigitWidth / atlasWidth, 0),
                    new Vector2((cell + 1) * DigitWidth / atlasWidth, DigitHeight / atlasHeight), colour));
            }
        }
    }

    private void DrawNumbers(GraphicsDevice device, IReadOnlyList<Hud.FloatingNumber> numbers, Matrix viewProjection, int width, int height)
    {
        if (numbers.Count == 0)
        {
            return;
        }
        sprites.Clear();
        foreach (var number in numbers)
        {
            var at = new Vector3(number.At.X, number.At.Y, number.At.Z);
            if (InFront(at, viewProjection))
            {
                Number(number, Project(at, viewProjection, width, height), digits.Width, digits.Height, sprites);
            }
        }
        Text.DrawSprites(device, digits, sprites, width, height);
    }

    private void DrawWheel(GraphicsDevice device, (Vector3 Centre, List<Hud.WheelIcon> Icons)? wheel, Matrix viewProjection, int width, int height)
    {
        if (wheel is not var (centre, icons) || icons.Count == 0 || !InFront(centre, viewProjection))
        {
            return;
        }
        var at = Project(centre, viewProjection, width, height);
        sprites.Clear();
        foreach (var icon in icons)
        {
            if (IconCell(icon.Element) is { } cell)
            {
                var middle = at + new Vector2(icon.Offset.X, icon.Offset.Y);
                sprites.Add(new GuiSprite(middle - new Vector2(WheelIcon / 2), middle + new Vector2(WheelIcon / 2),
                    new Vector2(cell.Left / (float)hud.Width, cell.Top / (float)hud.Height), new Vector2(cell.Right / (float)hud.Width, cell.Bottom / (float)hud.Height),
                    new Vector4(1, 1, 1, icon.Alpha)));
            }
        }
        Text.DrawSprites(device, hud, sprites, width, height);
    }
}
