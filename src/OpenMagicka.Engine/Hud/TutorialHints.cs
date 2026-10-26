using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.Spells;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Hud;

/// <summary>
/// The tutorial's pieces of the screen (docs/engine/ARCHITECTURE.md §21), as the original shows them, stepped
/// with the game's time on the logic thread:
/// <list type="bullet">
/// <item>the darkening, half black at most, fading in and out over a quarter of a second;</item>
/// <item>the hint, fading in over a quarter of a second; another one waits for it to fade out first (the last asked
/// wins), and asking again for the one shown changes nothing;</item>
/// <item>the dialog hint, fading in over a quarter of a second, gone at once when closed.</item>
/// </list>
/// </summary>
public sealed class TutorialHints
{
    /// <summary>How fast the pieces fade (a quarter of a second, in or out).</summary>
    public const float FadeRate = 4;

    private TutorialDialogHint? dialog;

    /// <summary>How dark the screen is (0…1 of the darkening's most).</summary>
    public float Dark { get; private set; }

    /// <summary>The hint shown (it may be fading out for another), or none.</summary>
    public TutorialHint? Shown { get; private set; }

    public float HintAlpha { get; private set; }

    public TutorialDialogHint? Dialog => dialog;

    public float DialogAlpha { get; private set; }

    public void Step(float dt, ScriptTutorial tutorial)
    {
        ArgumentNullException.ThrowIfNull(tutorial);
        var fade = FadeRate * (float.IsFinite(dt) ? Math.Max(0, dt) : 0);
        Dark = Math.Clamp(Dark + (tutorial.IsDark ? fade : -fade), 0, 1);
        if (!Equals(tutorial.Hint, Shown) && HintAlpha <= 0)
        {
            Shown = tutorial.Hint;                                            // nothing left to fade out: the one asked, at once
        }
        if (!Equals(tutorial.Hint, Shown))
        {
            HintAlpha = Math.Max(0, HintAlpha - fade);
        }
        else if (Shown is not null)
        {
            HintAlpha = Math.Min(1, HintAlpha + fade);
        }
        if (!ReferenceEquals(tutorial.DialogHint, dialog))
        {
            (dialog, DialogAlpha) = (tutorial.DialogHint, 0);
        }
        if (dialog is not null)
        {
            DialogAlpha = Math.Min(1, DialogAlpha + fade);
        }
    }

    /// <summary>A hint's text key: its keyboard or pad form when it has them, by the first player's device.</summary>
    public static string HintKey(TutorialHint hint, bool pad)
    {
        ArgumentNullException.ThrowIfNull(hint);
        return hint.ForController ? hint.Key + (pad ? "_pad" : "_key") : hint.Key;
    }

    /// <summary>The element's name key over a dialog hint (<c>#element_life</c>…), or none.</summary>
    public static string? ElementKey(Elements element) => element is Elements.None or Elements.All || (element & (element - 1)) != 0
        ? null : "#element_" + element.ToString().ToLowerInvariant();

    /// <summary>What the screen shows of the tutorial, its texts looked up; none when nothing shows.</summary>
    public TutorialPicture? Picture(Func<string, string> text, bool pad)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Dark <= 0 && (Shown is null || HintAlpha <= 0) && dialog is null)
        {
            return null;
        }
        var hint = Shown is { } shown && HintAlpha > 0 ? text(HintKey(shown, pad)) : null;
        string? words = null;
        if (dialog is { } open)
        {
            words = text(open.Key);
            if (ElementKey(open.Element) is { } name)
            {
                words = text(name) + "\n\n" + words;
            }
        }
        return new TutorialPicture(Dark, hint, Shown?.Position ?? HintPosition.BottomRight, HintAlpha, words, dialog?.Element ?? Elements.None, dialog?.Magick ?? MagickType.None,
            DialogAlpha, dialog?.Scale ?? 1, dialog?.Size is { } size ? new Xna.Vector2(size.X, size.Y) : null);
    }
}
