using System.Globalization;

namespace OpenMagicka.Engine.Text;

/// <summary>
/// Text that types itself out, as the original's dialog text (docs/engine/ARCHITECTURE.md §18): 40 glyphs a second, a
/// quarter second more after a punctuation mark followed by a space, and a pause of N seconds at each <c>[p=N]</c>;
/// other tags pass at once, a line break shows nothing. Several glyphs may come in one step.
/// </summary>
public sealed class TypingText
{
    public const float Speed = 40, PunctuationPause = 0.25f;

    private const string Punctuation = "!,.:;?…‼";

    private string text = "";
    private int index;
    private float next;

    public string Text => text;

    /// <summary>How many of its glyphs show.</summary>
    public int Visible { get; private set; }

    /// <summary>How many glyphs it has: each character outside the tags but line breaks, a <c>[[</c> one.</summary>
    public int Glyphs { get; private set; }

    public bool IsFinished => Visible >= Glyphs;

    /// <summary>A new text, typed from its start.</summary>
    public void Set(string value)
    {
        text = value ?? throw new ArgumentNullException(nameof(value));
        (index, next, Visible, Glyphs) = (0, 0, 0, Count(value));
    }

    /// <summary>All of it shows at once.</summary>
    public void Finish() => (Visible, index) = (Glyphs, text.Length);

    public void Update(float dt)
    {
        next -= dt;
        while (next < 0 && Visible < Glyphs && index < text.Length)
        {
            var c = text[index++];
            if (c == '[')
            {
                if (index < text.Length && text[index] == '[')
                {
                    index++;
                    Show(1 / Speed);
                    continue;
                }
                var close = text.IndexOf(']', index);
                var tag = close < 0 ? text[index..] : text[index..close];
                index = close < 0 ? text.Length : close + 1;
                if (tag.Length > 2 && tag[0] is 'p' or 'P' && tag[1] == '='
                    && float.TryParse(tag[2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pause))
                {
                    next += pause;                                            // ours: a malformed pause is passed, the original throws
                }
                continue;
            }
            if (c == '\n' || c == '\0')
            {
                continue;
            }
            var stops = Punctuation.Contains(c) && index < text.Length && char.IsWhiteSpace(text[index]);
            Show(stops ? PunctuationPause : 1 / Speed);
        }
    }

    private void Show(float wait) => (Visible, next) = (Visible + 1, next + wait);

    /// <summary>The glyphs a text places (what its layout makes quads of).</summary>
    public static int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (count, inTag) = (0, false);
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
                    (count, inTag) = (count + 1, false);
                }
                else if (c == ']')
                {
                    inTag = false;
                }
                continue;
            }
            if (c == '[')
            {
                inTag = true;
            }
            else if (c != '\n')
            {
                count++;
            }
        }
        return count;
    }
}
