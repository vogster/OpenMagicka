using System.Globalization;
using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Text;

public enum TextAlignment : byte { Left, Center, Right }

/// <summary>A glyph placed: its corners in font pixels from the text's anchor, its corners in the atlas (0…1), its colour.</summary>
public readonly record struct TextQuad(Vector2 TopLeft, Vector2 BottomRight, Vector2 UvTopLeft, Vector2 UvBottomRight, Vector4 Colour);

/// <summary>
/// Text laid out in glyph quads, as the original's text (docs/engine/ARCHITECTURE.md §18): the pen starts at the anchor
/// (the top of the first line), each glyph kerned against the one before and preceded by the font's spacing; a line
/// break starts the next line; each line is shifted by its alignment. <c>[c=r,g,b(,a)]</c> colours what follows (but
/// white-forced glyphs), <c>[/c]</c> restores the default, <c>[[</c> is a <c>[</c>, any other tag is hidden.
/// </summary>
public static class TextLayout
{
    public static List<TextQuad> Lay(BitmapFont font, string text, Vector4 colour, TextAlignment alignment, int atlasWidth, int atlasHeight)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        var quads = new List<TextQuad>(text.Length);
        var (x, y, previous, current, lineStart) = (0, 0, '\0', colour, 0);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0')
            {
                break;
            }
            if (c == '[')
            {
                if (i + 1 < text.Length && text[i + 1] == '[')
                {
                    i++;
                }
                else
                {
                    var close = text.IndexOf(']', i + 1);
                    var tag = close < 0 ? text[(i + 1)..] : text[(i + 1)..close];
                    current = tag == "/c" ? colour : Colour(tag) ?? current;
                    i = close < 0 ? text.Length : close;
                    continue;
                }
            }
            if (c == '\n')
            {
                Align(quads, lineStart, x, alignment);
                (x, y, lineStart) = (0, y + font.LineHeight, quads.Count);
                continue;
            }
            var glyph = font[c];
            x += font.Kern(previous, c) + font.Spacing;
            var left = new Vector2(x + glyph.LeftSideBearing, y);
            quads.Add(new TextQuad(left, left + new Vector2(glyph.Width, glyph.Height),
                new Vector2((float)glyph.X / atlasWidth, (float)glyph.Y / atlasHeight),
                new Vector2((float)(glyph.X + glyph.Width) / atlasWidth, (float)(glyph.Y + glyph.Height) / atlasHeight),
                glyph.ForceWhite ? Vector4.One : current));
            (x, previous) = (x + glyph.Advance, c);
        }
        Align(quads, lineStart, x, alignment);
        return quads;
    }

    /// <summary>A colour tag's colour (<c>c=r,g,b</c> or <c>c=r,g,b,a</c>); none for another tag.</summary>
    public static Vector4? Colour(string tag)
    {
        if (!tag.StartsWith("c=", StringComparison.Ordinal))
        {
            return null;
        }
        var parts = tag[2..].Split(',');
        var values = new float[4] { 0, 0, 0, 1 };
        if (parts.Length is not (3 or 4))
        {
            return null;
        }
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    /// <summary>A line's quads shifted by its alignment: half its width left (centre) or all of it (right), floored.</summary>
    private static void Align(List<TextQuad> quads, int start, int width, TextAlignment alignment)
    {
        var shift = alignment switch
        {
            TextAlignment.Center => MathF.Floor(-width * 0.5f),
            TextAlignment.Right => -width,
            _ => 0f,
        };
        if (shift == 0)
        {
            return;
        }
        var by = new Vector2(shift, 0);
        for (var i = start; i < quads.Count; i++)
        {
            quads[i] = quads[i] with { TopLeft = quads[i].TopLeft + by, BottomRight = quads[i].BottomRight + by };
        }
    }
}
