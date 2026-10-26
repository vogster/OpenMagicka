using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The tutorial's hints at play (docs/engine/ARCHITECTURE.md §21). A dialog hint is closed by a player as the
/// original's: a left or right click, a pad's face button (Attack, Special, Interact, Boost), or a key (but Escape, and
/// not during a cutscene, where keys skip it); not in its first second. Its closing sets its trigger off, which in the
/// tutorial gives the input back. The closing comes before the input's own limits (a dialog hint is up while the scene
/// has turned the input off), and the click or key still reaches the wizard.
/// </summary>
public sealed partial class LevelPlay
{
    private static readonly ControllerFunction[] ClosingButtons =
        [ControllerFunction.Attack, ControllerFunction.Special, ControllerFunction.Interact, ControllerFunction.Boost];

    private readonly TutorialHints hints = new();
    private readonly Dictionary<string, string> tutorialTexts = [];

    /// <summary>What the screen shows of the tutorial.</summary>
    public TutorialHints Hints => hints;

    /// <summary>A player's input closes the dialog hint up (when it may be closed), its trigger set off.</summary>
    private void CloseDialogHint(ControlFrame? input, PadFrame? pad)
    {
        var tutorial = Level.Game.Tutorial;
        if (tutorial.DialogHint is null || !Closes(input, pad, IsInCutscene))
        {
            return;
        }
        if (tutorial.Close() is { } trigger and not 0)
        {
            Scene?.Scripts?.ExecuteTrigger(trigger, null);
        }
    }

    /// <summary>Whether this tick's input closes a dialog hint (see the class).</summary>
    public static bool Closes(ControlFrame? input, PadFrame? pad, bool inCutscene)
    {
        if (pad is { } buttons && ClosingButtons.Any(buttons.IsPressed))
        {
            return true;
        }
        if (input is not { } frame)
        {
            return false;
        }
        static bool Clicked(ButtonState now, ButtonState before) => now == ButtonState.Pressed && before == ButtonState.Released;
        return Clicked(frame.Mouse.LeftButton, frame.LastMouse.LeftButton) || Clicked(frame.Mouse.RightButton, frame.LastMouse.RightButton)
            || (!inCutscene && frame.Keyboard.GetPressedKeys().Any(k => k != Keys.Escape && !frame.LastKeyboard.IsKeyDown(k)));
    }

    /// <summary>The hold-off and the pieces' fades step with the level's time.</summary>
    private void StepTutorial(float dt)
    {
        Level.Game.Tutorial.Step(dt);
        hints.Step(dt, Level.Game.Tutorial);
    }

    /// <summary>
    /// The tutorial's pieces into the frame's, their texts in the level's language (by the player's device), each
    /// <c>#key;</c> in them replaced by its string in white, as the original's (an element's "#TYPE;" and the like).
    /// </summary>
    public void WriteTutorial(Engine.Rendering.GuiPictures into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Tutorial = hints.Picture(TutorialText, padUsed);
    }

    /// <summary>A key's text, each <c>#key;</c> in it replaced, looked up once.</summary>
    private string TutorialText(string key)
    {
        if (!tutorialTexts.TryGetValue(key, out var text))
        {
            tutorialTexts[key] = text = Strings is { } strings ? strings.ParseReferences(strings.Get(key)) : key;
        }
        return text;
    }
}
