using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The original HUD's fixed pieces (docs/engine/ARCHITECTURE.md §19): the element keys in the bottom-left corner (each a
/// drop shadow, its icon, a key plate with the bound key's name, all at the key's saturation and intensity), and the
/// bar across the top (a dark frame in three parts, the red fill inset 16 px, an ornament over it).
/// </summary>
public sealed partial class GuiPass
{
    /// <summary>The keys' rows' tops from the screen's bottom, and their pieces' regions: the plate, the shadow.</summary>
    public const float KeyTopRow = 140, KeyBottomRow = 80, KeyIcon = 50, KeyPlate = 25;

    public static readonly Rectangle Plate = new(0, 0, 25, 25), Shadow = new(25, 0, 25, 25);

    /// <summary>The top bar's height band and its strips (the frame's, the ornament's, the fill's).</summary>
    public const float TopBarTop = 8, TopBarBottom = 32, TopBarCap = 96, TopBarInset = 16;

    public static readonly Rectangle TopBarFrame = new(0, 48, 256, 24), TopBarOrnament = new(0, 24, 256, 24), TopBarFill = new(76, 72, 8, 24);

    /// <summary>A key's quads at its place on a screen that high: shadow, icon, plate (the label goes on the plate).</summary>
    public static void Key(Hud.ElementKey key, int height, int atlasWidth, int atlasHeight, List<GuiSprite> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var at = new Vector2(key.X, height - (key.Row == 0 ? KeyTopRow : KeyBottomRow));
        var colour = new Vector4(key.Intensity, key.Intensity, key.Intensity, 1);
        GuiSprite Quad(Vector2 from, float size, Rectangle region) => new(from, from + new Vector2(size),
            new Vector2(region.Left / (float)atlasWidth, region.Top / (float)atlasHeight), new Vector2(region.Right / (float)atlasWidth, region.Bottom / (float)atlasHeight),
            colour);
        into.Add(Quad(at + new Vector2(-5, 5), KeyIcon, Shadow));
        if (IconCell(key.Element, rowPitch: 50) is { } cell)
        {
            into.Add(Quad(at, KeyIcon, cell));
        }
        into.Add(Quad(at - new Vector2(12), KeyPlate, Plate));
    }

    /// <summary>The top bar's quads: the frame's three parts, the fill, the ornament's three parts.</summary>
    public static void TopBar(TopBarPicture bar, int width, int atlasWidth, int atlasHeight, List<GuiSprite> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var span = MathF.Floor(bar.Width * width);
        var left = width / 2f - span / 2;
        var white = new Vector4(1, 1, 1, bar.Alpha);
        Vector2 Uv(float x, float y) => new(x / atlasWidth, y / atlasHeight);
        void Part(float from, float to, float u, float v, Rectangle strip) =>
            into.Add(new GuiSprite(new Vector2(from, TopBarTop), new Vector2(to, TopBarBottom), Uv(u, strip.Top), Uv(v, strip.Bottom), white));
        void Strip(Rectangle strip)
        {
            Part(left, left + TopBarCap, strip.Left, strip.Left + 96, strip);
            Part(left + TopBarCap, left + span - TopBarCap, strip.Left + 96, strip.Left + 160, strip);
            Part(left + span - TopBarCap, left + span, strip.Left + 160, strip.Right, strip);
        }
        Strip(TopBarFrame);
        var fill = Math.Clamp(bar.Shown, 0, 1) * (span - 2 * TopBarInset);
        if (fill > 0)
        {
            into.Add(new GuiSprite(new Vector2(left + TopBarInset, TopBarTop), new Vector2(left + TopBarInset + fill, TopBarBottom),
                Uv(TopBarFill.Left, TopBarFill.Top), Uv(TopBarFill.Right, TopBarFill.Bottom), new Vector4(bar.Fill.X, bar.Fill.Y, bar.Fill.Z, bar.Alpha)));
        }
        Strip(TopBarOrnament);
    }

    private void DrawTopBar(GraphicsDevice device, TopBarPicture? bar, int width, int height)
    {
        if (bar is not { Alpha: > 0 } shown)
        {
            return;
        }
        sprites.Clear();
        TopBar(shown, width, hud.Width, hud.Height, sprites);
        Text.DrawSprites(device, hud, sprites, width, height);
    }

    private void DrawKeys(GraphicsDevice device, IReadOnlyList<Hud.ElementKey> keys, int width, int height)
    {
        var font = Fonts["Maiandra14"];
        foreach (var key in keys)
        {
            sprites.Clear();
            Key(key, height, hud.Width, hud.Height, sprites);
            Text.DrawSprites(device, hud, sprites, width, height, key.Saturation);
            var top = new Vector2(key.X + 2, height - (key.Row == 0 ? KeyTopRow : KeyBottomRow) - 12);
            Text.Draw(device, font, key.Label, new Vector4(1, 1, 1, 1), top, 1, int.MaxValue, width, height, Engine.Text.TextAlignment.Center,
                new Vector4(key.Intensity, key.Intensity, key.Intensity, 1));
        }
    }
}
