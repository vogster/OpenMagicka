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

/// <summary>A quad of an atlas: its corners in pixels, its atlas corners (0…1), its colour.</summary>
public readonly record struct GuiSprite(Vector2 TopLeft, Vector2 BottomRight, Vector2 UvTopLeft, Vector2 UvBottomRight, Vector4 Colour);

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
            "TextureOffset", "ScaleToHDR", "Saturation"]);
        return effect;
    }

    public GameEffect Effect => effect;

    /// <summary>Whether colours are written as the scene's (1 − 2^−c, which the filter undoes): on in the scene, off straight onto the screen (the menu).</summary>
    public bool ScaleToHdr { get; init; } = true;

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
        parameters.SetValue("Saturation", 1f);
        parameters.SetValue("VertexColorEnabled", true);
        parameters.SetValue("ScaleToHDR", ScaleToHdr);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, corners, 0, corners.Length / 3);
        }
    }

    /// <summary>Textured quads of the atlas, each its corners in pixels, its atlas corners (0…1) and its colour.</summary>
    /// <param name="saturation">Their colour's saturation (1 as it is, 0 grey).</param>
    public void DrawSprites(GraphicsDevice device, Texture2D atlas, IReadOnlyList<GuiSprite> sprites, int width, int height, float saturation = 1)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(sprites);
        if (sprites.Count == 0)
        {
            return;
        }
        if (vertices.Length < sprites.Count * 4)
        {
            vertices = new GuiTextVertex[sprites.Count * 4];
            indices = new short[sprites.Count * 6];
        }
        for (var i = 0; i < sprites.Count; i++)
        {
            var (a, b, uvA, uvB, colour) = sprites[i];
            vertices[i * 4] = new GuiTextVertex(a, uvA, colour);
            vertices[i * 4 + 1] = new GuiTextVertex(new Vector2(b.X, a.Y), new Vector2(uvB.X, uvA.Y), colour);
            vertices[i * 4 + 2] = new GuiTextVertex(b, uvB, colour);
            vertices[i * 4 + 3] = new GuiTextVertex(new Vector2(a.X, b.Y), new Vector2(uvA.X, uvB.Y), colour);
            var (first, at) = ((short)(i * 4), i * 6);
            (indices[at], indices[at + 1], indices[at + 2], indices[at + 3], indices[at + 4], indices[at + 5]) =
                (first, (short)(first + 1), (short)(first + 2), (short)(first + 2), (short)(first + 3), first);
        }
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("TransformToScreen", ToScreen(width, height));
        parameters.SetValue("Transform", Matrix.Identity);
        parameters.SetValue("Color", Vector4.One);
        parameters.SetValue("TextureEnabled", true);
        parameters.SetValue("VertexColorEnabled", true);
        parameters.SetValue("ScaleToHDR", ScaleToHdr);
        parameters.SetValue("TextureScale", Vector2.One);
        parameters.SetValue("TextureOffset", Vector2.Zero);
        parameters.SetValue("Texture", atlas);
        parameters.SetValue("Saturation", saturation);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, sprites.Count * 4, indices, 0, sprites.Count * 2);
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
        parameters.SetValue("ScaleToHDR", ScaleToHdr);
        parameters.SetValue("TextureScale", Vector2.One);
        parameters.SetValue("TextureOffset", Vector2.Zero);
        parameters.SetValue("Texture", font.Atlas);
        parameters.SetValue("Saturation", 1f);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, count * 4, indices, 0, count * 2);
        }
    }
}
