using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;

namespace OpenMagicka.Game.Play;

/// <summary>
/// A cutscene's presentation (docs/engine/ARCHITECTURE.md §18): its bars and skip prompt step with the level, and a
/// player's press asks to skip: on the keyboard a key the original's test lets through (its code shares no bit with
/// Escape's: Space, D, Home, the shifts…; the mouse never skips), on a pad Attack, Boost, Interact or Special.
/// </summary>
public sealed partial class LevelPlay
{
    /// <summary>The skip prompt's button: the space bar's glyph for the keyboard, the A button's for a pad.</summary>
    public const char SpaceBarGlyph = (char)0x32A, PadAGlyph = (char)0x324;

    private string? skipText;

    public CutsceneBars Bars { get; } = new();

    /// <summary>In a cutscene, or its bars still fully in (dialogs then play in its box, players' presses skip).</summary>
    public bool IsInCutscene => Bars.IsInCutscene(Level.Game);

    /// <summary>Whether a press skips: a key whose code shares no bit with Escape's (as the original's test), or a pad's.</summary>
    public static bool SkipsCutscene(ControlFrame? keys, PadFrame? pad) =>
        (keys is { } frame && frame.Keyboard.GetPressedKeys().Any(k => ((int)k & (int)Keys.Escape) == 0 && !frame.LastKeyboard.IsKeyDown(k)))
        || (pad is { } buttons && (buttons.IsPressed(ControllerFunction.Attack) || buttons.IsPressed(ControllerFunction.Boost)
            || buttons.IsPressed(ControllerFunction.Interact) || buttons.IsPressed(ControllerFunction.Special)));

    private void StepCutscene(float dt, ControlFrame? input, PadFrame? pad)
    {
        if (IsInCutscene && SkipsCutscene(input, pad) && Scene?.Scripts is { } scripts)
        {
            skipText ??= SkipText(pad is not null);                          // as the original: decided on the first ask
            scripts.SkipCutscene();
        }
        Bars.Update(dt, Level.Game, Level.Level.Stage);
    }

    /// <summary>The prompt's text: "#cut_press_skip" with its "#1;" the button's glyph.</summary>
    private string SkipText(bool padded) =>
        (Strings?.Get("#cut_press_skip") ?? "#1; Skip").Replace("#1;", (padded ? PadAGlyph : SpaceBarGlyph).ToString(), StringComparison.Ordinal);

    /// <summary>The bars and prompt, into the frame's pictures, while they show.</summary>
    public void WriteCutscene(GuiPictures into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (Bars.Amount > 0)
        {
            into.Letterbox = (Bars.Amount, Bars.Prompt, skipText ?? SkipText(false));
        }
    }
}
