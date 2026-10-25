using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The original HUD drawn from its atlas <c>UI/HUD/hud</c> (512 × 512, docs/engine/ARCHITECTURE.md §19). A health bar
/// is a frame (its caps 4 wide, its middle 64, all 8 high, drawn white at the bar's alpha) and a fill over the middle
/// as long as its share, in its colour; each unit scaled by the bar's width and height, the bar centred on the
/// projected point, its top 16.5 px below it.
/// </summary>
public sealed partial class GuiPass
{
    public const string HudAtlas = "UI/HUD/hud";
    public const float BarLength = 64, BarCap = 4, BarHeight = 8, BarDrop = 16.5f;

    /// <summary>The bar's atlas regions (x, y, w, h in texels): the frame's caps and middle, and the fill.</summary>
    public static readonly Rectangle BarLeft = new(6, 73, 2, 6), BarMiddle = new(16, 73, 8, 6), BarRight = new(32, 73, 2, 6), BarFill = new(2, 81, 4, 6);

    private Texture2D hud = null!;
    private readonly List<GuiSprite> sprites = [];

    /// <summary>A bar's quads on a screen: the frame's three, then the fill's.</summary>
    public static void Bar(HealthBarPicture bar, Vector2 projected, int atlasWidth, int atlasHeight, List<GuiSprite> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var origin = projected + new Vector2(0.5f - BarLength / 2 * bar.Width, BarDrop + bar.Lower);
        Vector2 At(float x, float y) => origin + new Vector2(x * bar.Width, y * bar.Height);
        GuiSprite Quad(float from, float to, Rectangle region, Vector4 colour) => new(At(from, 0), At(to, BarHeight),
            new Vector2(region.Left / (float)atlasWidth, region.Top / (float)atlasHeight),
            new Vector2(region.Right / (float)atlasWidth, region.Bottom / (float)atlasHeight), colour);
        var frame = new Vector4(1, 1, 1, bar.Colour.W);
        into.Add(Quad(-BarCap, 0, BarLeft, frame));
        into.Add(Quad(0, BarLength, BarMiddle, frame));
        into.Add(Quad(BarLength, BarLength + BarCap, BarRight, frame));
        if (bar.Share > 0)
        {
            into.Add(Quad(0, BarLength * bar.Share, BarFill, bar.Colour));
        }
    }

    /// <summary>The queue's bracket (the right one mirrored), and an element icon's cell (5 a row, 50 × 49, from y 156).</summary>
    public static readonly Rectangle Bracket = new(248, 8, 8, 16);

    public const float IconSize = 25, BracketDistance = 62.5f, QueueDrop = 44, MagickNameDrop = 54, IconCellWidth = 50, IconCellHeight = 49, IconRows = 156;

    /// <summary>
    /// An element's icon cell: its flag's bit (Earth 0 … Steam 9, five a row, 50 wide); the rows are 49 apart and high for
    /// the queue, 50 for the keys (as the original's); none for no single element.
    /// </summary>
    public static Rectangle? IconCell(Gameplay.Spells.Elements element, int rowPitch = 49)
    {
        var value = (int)element;
        if (value == 0 || (value & (value - 1)) != 0 || value >= 1 << 10)
        {
            return null;
        }
        var index = System.Numerics.BitOperations.Log2((uint)value);
        return new Rectangle(index % 5 * (int)IconCellWidth, (int)IconRows + index / 5 * rowPitch, (int)IconCellWidth, rowPitch);
    }

    /// <summary>A queue's quads under its projected feet: the brackets, the icons, then the recipe's translucent rest.</summary>
    public static void Queue(QueuePicture queue, Vector2 feet, int atlasWidth, int atlasHeight, List<GuiSprite> into)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(into);
        var hints = queue.MagickAlpha > 0 ? queue.Recipe.Count - Math.Min(queue.RecipeFrom, queue.Recipe.Count) : 0;
        if (queue.Icons.Count == 0 && hints == 0)
        {
            return;
        }
        Vector2 Uv(float x, float y) => new(x / atlasWidth, y / atlasHeight);
        var y = feet.Y + QueueDrop;
        var (left, right) = (MathF.Floor(feet.X - BracketDistance), MathF.Floor(feet.X + BracketDistance));
        var half = new Vector2(Bracket.Width, Bracket.Height) / 2;
        into.Add(new GuiSprite(new Vector2(left, y) - half, new Vector2(left, y) + half, Uv(Bracket.Left, Bracket.Top), Uv(Bracket.Right, Bracket.Bottom), Vector4.One));
        into.Add(new GuiSprite(new Vector2(right, y) - half, new Vector2(right, y) + half, Uv(Bracket.Right, Bracket.Top), Uv(Bracket.Left, Bracket.Bottom), Vector4.One));
        void Icon(Gameplay.Spells.Elements element, Vector2 at, Vector2 scale, Vector4 colour)
        {
            if (IconCell(element) is { } cell)
            {
                var size = IconSize / 2 * scale;
                into.Add(new GuiSprite(at - size, at + size, Uv(cell.Left, cell.Top), Uv(cell.Right, cell.Bottom), colour));
            }
        }
        foreach (var icon in queue.Icons)
        {
            Icon(icon.Element, feet + new Vector2(icon.Offset.X, icon.Offset.Y), new Vector2(icon.Scale.X, icon.Scale.Y),
                new Vector4(icon.Colour.X, icon.Colour.Y, icon.Colour.Z, icon.Colour.W));
        }
        for (var i = queue.RecipeFrom; i < queue.Recipe.Count && i < Hud.QueueIcons.Slots && queue.MagickAlpha > 0; i++)
        {
            var slot = Hud.QueueIcons.SlotOffsets[i];
            Icon(queue.Recipe[i], feet + new Vector2(slot.X, slot.Y), Vector2.One, new Vector4(1, 1, 1, 0.5f * queue.MagickAlpha));
        }
    }

    private void DrawQueues(GraphicsDevice device, IReadOnlyList<QueuePicture> queues, Matrix viewProjection, int width, int height)
    {
        foreach (var queue in queues)
        {
            if (!InFront(queue.Feet, viewProjection))
            {
                continue;
            }
            var feet = Project(queue.Feet, viewProjection, width, height);
            sprites.Clear();
            Queue(queue, feet, hud.Width, hud.Height, sprites);
            if (sprites.Count == 0)
            {
                continue;
            }
            if (queue.MagickName is { Length: > 0 } name && queue.MagickAlpha > 0)
            {
                Text.Draw(device, Fonts["Maiandra14"], name, new Vector4(1, 1, 1, queue.MagickAlpha), feet + new Vector2(0, MagickNameDrop), 1, int.MaxValue,
                    width, height, Engine.Text.TextAlignment.Center);
            }
            Text.DrawSprites(device, hud, sprites, width, height);
        }
    }

    /// <summary>Whether a world point is before the camera (one behind it would project mirrored).</summary>
    private static bool InFront(Vector3 at, Matrix viewProjection) => Vector4.Transform(new Vector4(at, 1), viewProjection).W > 0;

    private void DrawBars(GraphicsDevice device, IReadOnlyList<HealthBarPicture> bars, Matrix viewProjection, int width, int height)
    {
        if (bars.Count == 0)
        {
            return;
        }
        sprites.Clear();
        foreach (var bar in bars)
        {
            if (bar.Colour.W > 0 && InFront(bar.At, viewProjection))
            {
                Bar(bar, Project(bar.At, viewProjection, width, height), hud.Width, hud.Height, sprites);
            }
        }
        Text.DrawSprites(device, hud, sprites, width, height);
    }
}
