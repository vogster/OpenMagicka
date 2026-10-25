using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Engine.Rendering;

/// <summary>A GUI text vertex, as the original's: its place in font pixels, its place in the atlas, its colour.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct GuiTextVertex(Vector2 position, Vector2 uv, Vector4 colour) : IVertexType
{
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.Color, 0));

    public readonly Vector2 Position = position;
    public readonly Vector2 Uv = uv;
    public readonly Vector4 Colour = colour;

    VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

/// <summary>A font with its atlas on the GPU.</summary>
public sealed record GuiFont(BitmapFont Metrics, Texture2D Atlas) : IDisposable
{
    public void Dispose() => Atlas.Dispose();
}

/// <summary>
/// Text drawn as the original draws it (docs/engine/ARCHITECTURE.md §18): its glyph quads through <c>GUIBasicEffect</c>'s 2D
/// technique, from font pixels to the screen, scaled about its anchor, vertex colours on, the colour written as the
/// scene's (1 − 2^−c, which the filter undoes). Render thread only.
/// </summary>
public sealed class GuiText(GameEffect effect)
{
    private readonly GameEffect effect = Checked(effect);
    private GuiTextVertex[] vertices = [];
    private short[] indices = [];

    private static GameEffect Checked(GameEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        EffectChecks.Require(effect, ["TransformToScreen", "Transform", "Color", "TextureEnabled", "VertexColorEnabled", "Texture", "TextureScale",
            "TextureOffset", "ScaleToHDR"]);
        return effect;
    }

    public GameEffect Effect => effect;

    /// <summary>Flat quads of a colour (no texture): each four corners in pixels.</summary>
    public void DrawQuads(GraphicsDevice device, IReadOnlyList<(Vector2 TopLeft, Vector2 BottomRight)> quads, Vector4 colour, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(quads);
        var corners = quads.SelectMany(q => new[]
        {
            new GuiTextVertex(q.TopLeft, Vector2.Zero, colour), new GuiTextVertex(new Vector2(q.BottomRight.X, q.TopLeft.Y), Vector2.Zero, colour),
            new GuiTextVertex(q.BottomRight, Vector2.Zero, colour), new GuiTextVertex(q.TopLeft, Vector2.Zero, colour),
            new GuiTextVertex(q.BottomRight, Vector2.Zero, colour), new GuiTextVertex(new Vector2(q.TopLeft.X, q.BottomRight.Y), Vector2.Zero, colour),
        }).ToArray();
        if (corners.Length == 0)
        {
            return;
        }
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("TransformToScreen", ToScreen(width, height));
        parameters.SetValue("Transform", Matrix.Identity);
        parameters.SetValue("Color", Vector4.One);
        parameters.SetValue("TextureEnabled", false);
        parameters.SetValue("VertexColorEnabled", true);
        parameters.SetValue("ScaleToHDR", true);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, corners, 0, corners.Length / 3);
        }
    }

    /// <summary>Pixels (y down) to clip space for a screen of that size.</summary>
    public static Matrix ToScreen(int width, int height) => Matrix.CreateScale(2f / width, -2f / height, 1) * Matrix.CreateTranslation(-1, 1, 0);

    /// <summary>The first <paramref name="shown"/> glyphs of a text, its anchor at a point, scaled about it.</summary>
    public void Draw(GraphicsDevice device, GuiFont font, string text, Vector4 colour, Vector2 at, float scale, int shown, int width, int height,
        TextAlignment alignment = TextAlignment.Left, Vector4? tint = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(font);
        var quads = TextLayout.Lay(font.Metrics, text, colour, alignment, font.Atlas.Width, font.Atlas.Height);
        var count = Math.Clamp(shown, 0, quads.Count);
        if (count == 0 || !(scale > 0))
        {
            return;
        }
        if (vertices.Length < count * 4)
        {
            vertices = new GuiTextVertex[count * 4];
            indices = new short[count * 6];
        }
        for (var i = 0; i < count; i++)
        {
            var q = quads[i];
            vertices[i * 4] = new GuiTextVertex(q.TopLeft, q.UvTopLeft, q.Colour);
            vertices[i * 4 + 1] = new GuiTextVertex(new Vector2(q.BottomRight.X, q.TopLeft.Y), new Vector2(q.UvBottomRight.X, q.UvTopLeft.Y), q.Colour);
            vertices[i * 4 + 2] = new GuiTextVertex(q.BottomRight, q.UvBottomRight, q.Colour);
            vertices[i * 4 + 3] = new GuiTextVertex(new Vector2(q.TopLeft.X, q.BottomRight.Y), new Vector2(q.UvTopLeft.X, q.UvBottomRight.Y), q.Colour);
            var (b, at6) = ((short)(i * 4), i * 6);
            (indices[at6], indices[at6 + 1], indices[at6 + 2], indices[at6 + 3], indices[at6 + 4], indices[at6 + 5]) =
                (b, (short)(b + 1), (short)(b + 2), (short)(b + 2), (short)(b + 3), b);
        }
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("TransformToScreen", ToScreen(width, height));
        parameters.SetValue("Transform", Matrix.CreateScale(scale, scale, 1) * Matrix.CreateTranslation(at.X, at.Y, 0));
        parameters.SetValue("Color", tint ?? Vector4.One);
        parameters.SetValue("TextureEnabled", true);
        parameters.SetValue("VertexColorEnabled", true);
        parameters.SetValue("ScaleToHDR", true);
        parameters.SetValue("TextureScale", Vector2.One);
        parameters.SetValue("TextureOffset", Vector2.Zero);
        parameters.SetValue("Texture", font.Atlas);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, count * 4, indices, 0, count * 2);
        }
    }
}
