using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Characters;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The interact prompt (docs/engine/ARCHITECTURE.md §18), as the original's: while the player could talk to a
/// character, its next talk's word ("#interact_NN", Talk's by default) shows at the mouse over it, or with a pad's
/// button under its wizard for the one ahead; not while presses wait or a dialog holds them, in a cutscene or with the
/// HUD off. It fades in and out at 3 a second. While its wizard is held (OM-89) it says "break free" instead, with the
/// space bar's glyph (a pad's B), dimmed to half while boosts wait (coming back to full by a thousandth a second).
/// </summary>
public sealed partial class LevelPlay
{
    /// <summary>How fast the prompt fades in and out (a second's share).</summary>
    public const float PromptFade = 3;

    /// <summary>How dim the break-free glyph is while boosts wait.</summary>
    public const float PromptButtonDimmed = 0.5f;

    private PromptPicture prompt;
    private float promptButton = 1;
    private bool breakingFree;

    /// <summary>The prompt as it shows now; none while it is faded out.</summary>
    public PromptPicture? Prompt => prompt.Alpha > 0 ? prompt : null;

    /// <summary>The loc key of an interaction's word: "#interact_" and its number in two digits.</summary>
    public static string InteractKey(InteractType type) => "#interact_" + ((int)type).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The prompt's glyph a frame on: back towards full brightness, all but a thousandth of the way a second.</summary>
    public static float ButtonGlow(float glow, float dt) => 1 + (glow - 1) * MathF.Pow(0.001f, dt);

    private void StepPrompt(float dt, ControlFrame? input, bool padded)
    {
        breakingFree = false;
        var shown = PromptFor(input, padded);
        promptButton = ButtonGlow(promptButton, dt);                    // after a dimming, as the original's
        prompt = shown is { } now
            ? now with { ButtonIntensity = breakingFree ? promptButton : 1, Alpha = Math.Min(1, prompt.Alpha + PromptFade * dt) }
            : prompt with { Alpha = Math.Max(0, prompt.Alpha - PromptFade * dt) };
    }

    /// <summary>The prompt to show this tick (its alpha left for the fade), or none.</summary>
    private PromptPicture? PromptFor(ControlFrame? input, bool padded)
    {
        if (Dialogs is not { } dialogs || Strings is not { } strings || dialogs.IsAwaitingInput || dialogs.CanAdvance(Player) || IsInCutscene
            || !Level.Level.Stage.IsUiEnabled || Player.Avatar is not { IsDead: false } avatar || Scene?.Scripts is not { } scripts)
        {
            return null;
        }
        if (avatar.IsGripped)
        {
            if (avatar.Boosts > 0)
            {
                promptButton = PromptButtonDimmed;
            }
            breakingFree = true;
            var breakFree = strings.Get(InteractKey(InteractType.BreakFree));
            return padded
                ? new PromptPicture(breakFree, null, ToXna(avatar.Position), prompt.Alpha, GuiPass.PadBGlyph)
                : input is { } held ? new PromptPicture(breakFree, new Xna.Vector2(held.Mouse.X, held.Mouse.Y), null, prompt.Alpha, GuiPass.SpaceBarGlyph)
                : null;
        }
        var talker = padded ? ControlTargets.Ahead(avatar, scripts) as Character : Controls.Hovered as Character;
        if (talker is null || (!padded && input is null))
        {
            return null;
        }
        var type = dialogs.Run(talker.Dialog)?.Peek?.Definition.IconText is { } icon and not InteractType.None ? icon : InteractType.Talk;
        var text = strings.Get(InteractKey(type));
        return padded
            ? new PromptPicture(text, null, ToXna(avatar.Position), prompt.Alpha, GuiPass.PadXGlyph)
            : new PromptPicture(text, new Xna.Vector2(input!.Value.Mouse.X, input.Value.Mouse.Y), null, prompt.Alpha);
    }
}
