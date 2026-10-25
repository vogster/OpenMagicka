using OpenMagicka.Content.PolygonHead;

namespace OpenMagicka.Engine.Text;

/// <summary>A glyph of a bitmap font: its cell in the atlas, how far the pen moves, where it starts, and whether colour codes leave it white.</summary>
public readonly record struct Glyph(int X, int Y, int Width, int Height, int Advance, int LeftSideBearing, bool ForceWhite);

/// <summary>
/// A bitmap font's metrics, as the original's text engine uses them (docs/engine/ARCHITECTURE.md §18): a glyph per UTF-16
/// character (else the default character's, else the first), kerning by pairs, and the font's spacing, which the
/// files do not hold (the original sets it per font: <see cref="SpacingOf"/>). Layout counts in font pixels; it
/// never scales with the screen.
/// </summary>
public sealed class BitmapFont
{
    private readonly Dictionary<char, Glyph> glyphs = [];
    private readonly Dictionary<(char, char), int> kerning = [];
    private readonly Glyph fallback;

    public BitmapFont(BitmapFontContent content, int spacing)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Glyphs.Count == 0)
        {
            throw new InvalidDataException("a font without glyphs");
        }
        foreach (var glyph in content.Glyphs.Where(g => g.Character.IsBmp))
        {
            glyphs.TryAdd((char)glyph.Character.Value, new Glyph(glyph.X, glyph.Y, glyph.Width, glyph.Height, glyph.AdvanceWidth, glyph.LeftSideBearing,
                glyph.ForceWhite));
        }
        foreach (var pair in content.Kerning.Where(k => k.First.IsBmp && k.Second.IsBmp))
        {
            kerning.TryAdd(((char)pair.First.Value, (char)pair.Second.Value), pair.Amount);
        }
        var first = content.Glyphs[0];
        fallback = content.DefaultCharacter.IsBmp && glyphs.TryGetValue((char)content.DefaultCharacter.Value, out var byDefault)
            ? byDefault
            : new Glyph(first.X, first.Y, first.Width, first.Height, first.AdvanceWidth, first.LeftSideBearing, first.ForceWhite);
        (LineHeight, Spacing) = (content.LineHeight, spacing);
    }

    public int LineHeight { get; }

    /// <summary>Added before each glyph.</summary>
    public int Spacing { get; }

    /// <summary>The spacing the original gives each of its fonts (0 for one it does not name).</summary>
    public static int SpacingOf(string font) => font.ToLowerInvariant() switch
    {
        "maiandra16" or "maiandra18" or "stonecross28" or "stonecross36" or "stonecross50" or "menudefault" or "versustext"
            or "pdxuifont" or "pdxuismallfont" or "pdxuiboldfont" => 1,
        _ => 0,
    };

    public Glyph this[char c] => glyphs.TryGetValue(c, out var glyph) ? glyph : fallback;

    public int Kern(char previous, char c) => kerning.GetValueOrDefault((previous, c));

    /// <summary>The width a glyph adds after another: kerning, its advance and the spacing.</summary>
    private int Width(char previous, char c) => Kern(previous, c) + this[c].Advance + Spacing;

    /// <summary>
    /// The text's size: the widest line (one trailing spacing taken off) by the lines' height. Tags take no room and
    /// <c>[[</c> is a <c>[</c>. As the original, a line break is measured as a glyph too (the default character's
    /// advance, at the start of the next line).
    /// </summary>
    public (int Width, int Height) Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (widest, width, height, previous, inTag) = (0, 0, LineHeight, '\0', false);
        foreach (var c in text)
        {
            if (c == '\0')
            {
                break;
            }
            if (inTag)
            {
                if (c == '[')
                {
                    (width, previous, inTag) = (width + Width(previous, '['), '[', false);
                }
                else if (c == ']')
                {
                    inTag = false;
                }
                continue;
            }
            if (c == '\n')
            {
                (widest, width, height) = (Math.Max(widest, width), 0, height + LineHeight);
            }
            if (c == '[')
            {
                inTag = true;
                continue;
            }
            (width, previous) = (width + Width(previous, c), c);
        }
        var measured = Math.Max(widest, width);
        return (measured > 1 ? measured - Spacing : measured, height);
    }

    /// <summary>
    /// The text broken into lines no wider than the width where it can be, as the original's wrap: each line breaks at
    /// its last space that still fits (a line with none is never broken); written breaks stay; a first word wider than
    /// the width widens every line, the whole wrapped again.
    /// </summary>
    public string Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        while (true)
        {
            var (wrapped, wider) = WrapOnce(text, width);
            if (wider <= width)
            {
                return wrapped;
            }
            width = wider;
        }
    }

    private (string Text, int Width) WrapOnce(string text, int width)
    {
        var output = new System.Text.StringBuilder(text.Length + 8);
        var position = 0;
        while (position < text.Length)
        {
            var (cut, cutWidth, run, previous, inTag) = (text.Length, 0, 0, '\0', false);
            var i = position;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (inTag)
                {
                    if (c == '[')
                    {
                        (run, previous, inTag) = (run + Width(previous, '['), '[', false);
                    }
                    else if (c == ']')
                    {
                        inTag = false;
                    }
                }
                else
                {
                    if (c == ' ' && (run <= width || i < cut))
                    {
                        (cut, cutWidth) = (i, run);
                    }
                    if (c == '\0')
                    {
                        break;
                    }
                    if (c == '\n')
                    {
                        cut = i;
                        break;
                    }
                    if (c == '[')
                    {
                        inTag = true;
                    }
                    else
                    {
                        (run, previous) = (run + Width(previous, c), c);
                    }
                }
                if (run > width && i >= cut)
                {
                    break;
                }
            }
            if (i >= text.Length && run <= width)
            {
                (cut, cutWidth) = (text.Length, run);
            }
            if (cutWidth > width)
            {
                return ("", cutWidth);
            }
            output.Append(text, position, cut - position);
            if (cut < text.Length)
            {
                output.Append('\n');
            }
            position = cut + 1;
        }
        return (output.ToString(), width);
    }
}
