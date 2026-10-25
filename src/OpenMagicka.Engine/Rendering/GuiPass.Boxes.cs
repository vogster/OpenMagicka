using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The speech boxes' sizes and places, as the original's (docs/engine/ARCHITECTURE.md §18). A box over the world is its
/// text in Maiandra14 (wrapped and least-sized in Maiandra16), a little left of and above its speaker's head, kept 5 %
/// inside the screen; the cutscene's box is in Maiandra16, 640 wide, low in the middle, above the bottom bar. A name sits
/// over the text, which is indented 20 px under it.
/// </summary>
public sealed partial class GuiPass
{
    /// <summary>Alpha blending, as the GUI pass's.</summary>
    private static readonly BlendState Blend = BlendState.NonPremultiplied;

    public const int CutsceneWidth = 640;

    /// <summary>A box's inner size: its text measured (at least its message's least size, grown for a name), the cutscene's 640 wide.</summary>
    public static Point Size(SpeechBoxPicture box, BitmapFont text, BitmapFont name)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(name);
        var least = box.MinSize;
        if (box.Name is { } named)
        {
            var (nameWidth, nameHeight) = name.Measure(named);
            least = new Point(Math.Max(least.X + 20, nameWidth), least.Y + nameHeight);
        }
        var (width, height) = box.Text.Length > 0 ? text.Measure(box.Text) : (0, 0);
        var size = new Point(Math.Max(width, least.X), Math.Max(height, least.Y));
        return box.Cutscene ? size with { X = CutsceneWidth } : size;
    }

    /// <summary>
    /// A box's centre on a screen of that size: over a projected point of the world (left by a third of its half width
    /// and 64 px, up by its half height and 64 px, scaled), at a point of the screen, or the cutscene's place; then kept
    /// inside (5 % margins, the cutscene's above and below the bars).
    /// </summary>
    public static Vector2 Place(SpeechBoxPicture box, Point size, Vector2? projected, int width, int height)
    {
        Vector2 centre;
        if (box.Cutscene)
        {
            centre = new Vector2(0.5f * width, 0.8f * height);
            return Clamp(centre, size, 0, MathF.Floor(0.1f * height), width, height);
        }
        if (box.OnScreen is { } point)
        {
            centre = new Vector2(MathF.Floor(width / 2f * (1 + point.X)), MathF.Floor(height / 2f * (1 + point.Y)));
        }
        else
        {
            var at = projected ?? Vector2.Zero;
            centre = at - new Vector2((size.X / 2f + 64) / 3 * box.Scale, (size.Y / 2f + 64) * box.Scale);
        }
        return box.ForceOnScreen ? Clamp(centre, size, MathF.Floor(0.05f * width), MathF.Floor(0.05f * height), width, height) : centre;
    }

    private static Vector2 Clamp(Vector2 centre, Point size, float marginX, float marginY, int width, int height)
    {
        var (halfX, halfY) = ((size.X + 32) / 2f, (size.Y + 32) / 2f);
        return new Vector2(Math.Clamp(centre.X, halfX + marginX, MathF.Max(halfX + marginX, width - halfX - marginX)),
            Math.Clamp(centre.Y, halfY + marginY, MathF.Max(halfY + marginY, height - halfY - marginY)));
    }

    /// <summary>The boxes, their frames then their names and texts, over the bound target.</summary>
    public void Draw(GraphicsDevice device, GuiPictures gui, Matrix viewProjection, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(gui);
        device.BlendState = Blend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        foreach (var box in gui.Boxes.Where(b => b.Scale > 1e-4f))
        {
            var textFont = Fonts[box.Cutscene ? "Maiandra16" : "Maiandra14"];
            var nameFont = Fonts["Maiandra16"];
            var size = Size(box, textFont.Metrics, nameFont.Metrics);
            var centre = Place(box, size, box.At is { } at ? Project(at, viewProjection, width, height) : null, width, height);
            DrawFrame(device, centre, size, box.Scale, width, height);
            var corner = centre - new Vector2(size.X, size.Y) / 2 * box.Scale;
            if (box.Name is { } name)
            {
                Text.Draw(device, nameFont, name, DialogColour, corner, box.Scale, int.MaxValue, width, height);
                corner = centre - new Vector2(size.X / 2f - 20, size.Y / 2f - nameFont.Metrics.LineHeight) * box.Scale;
            }
            Text.Draw(device, textFont, box.Text, DialogColour, corner, box.Scale, box.Visible, width, height);
        }
        if (gui.Prompt is { } shown)
        {
            DrawPrompt(device, shown, viewProjection, width, height);
        }
        if (gui.Letterbox is var (bars, prompt, skip))
        {
            DrawLetterbox(device, bars, prompt, skip, width, height);
        }
    }

    /// <summary>
    /// A cutscene's bars, black, a tenth of the screen high, sliding in from above and below; its skip prompt in Maiandra18
    /// at the bottom right (white, fading by its alpha twice over, its button glyph once, as the original's).
    /// </summary>
    private void DrawLetterbox(GraphicsDevice device, float bars, float prompt, string skip, int width, int height)
    {
        var bar = 0.1f * height;
        Text.DrawQuads(device, [(new Vector2(0, -bar + bar * bars), new Vector2(width, bar * bars)),
            (new Vector2(0, height - bar * bars), new Vector2(width, height - bar * bars + bar))], new Vector4(0, 0, 0, 1), width, height);
        if (prompt > 0)
        {
            var font = Fonts["Maiandra18"];
            var faded = new Vector4(1, 1, 1, prompt);
            Text.Draw(device, font, skip, faded, new Vector2(width - 20, height - 0.05f * height - font.Metrics.LineHeight / 2f), 1, int.MaxValue,
                width, height, TextAlignment.Right, faded);
        }
    }

    /// <summary>A point of the world on the screen, in pixels from the top left.</summary>
    public static Vector2 Project(Vector3 at, Matrix viewProjection, int width, int height)
    {
        var clip = Vector4.Transform(new Vector4(at, 1), viewProjection);
        var ndc = clip.W != 0 ? new Vector2(clip.X, clip.Y) / clip.W : Vector2.Zero;
        return new Vector2((ndc.X * 0.5f + 0.5f) * width, (-ndc.Y * 0.5f + 0.5f) * height);
    }
}
