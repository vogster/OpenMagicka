using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// What the screen shows over the world this frame (docs/engine/ARCHITECTURE.md §18): the speech boxes, and in a cutscene
/// its bars and skip prompt. Filled on the logic thread, drawn on the render thread.
/// </summary>
public sealed class GuiPictures
{
    public List<SpeechBoxPicture> Boxes { get; } = [];

    /// <summary>The health bars under characters and shields (the HUD's, OM-54).</summary>
    public List<HealthBarPicture> Bars { get; } = [];

    /// <summary>The element queues under the wizards (the HUD's, OM-54).</summary>
    public List<QueuePicture> Queues { get; } = [];

    /// <summary>The element keys in the bottom-left corner (the HUD's, OM-54); none while the UI is off.</summary>
    public List<Hud.ElementKey> Keys { get; } = [];

    /// <summary>The boss fight's bar at the top, or the scripts' own bar (the HUD's, OM-54); none.</summary>
    public TopBarPicture? TopBar { get; set; }

    /// <summary>The damage and heal numbers over heads (the HUD's, OM-54).</summary>
    public List<Hud.FloatingNumber> Numbers { get; } = [];

    /// <summary>The pad's element wheel around its wizard's centre (the HUD's, OM-54); none.</summary>
    public (Vector3 Centre, List<Hud.WheelIcon> Icons)? Wheel { get; set; }

    /// <summary>How far the cutscene's bars are in (0…1), and how much its skip prompt shows; none outside a cutscene.</summary>
    public (float Bars, float Skip, string SkipText)? Letterbox { get; set; }

    /// <summary>The interact prompt: its text, at the mouse (pixels) or over the avatar (a pad's, with its button), how much it shows.</summary>
    public PromptPicture? Prompt { get; set; }

    /// <summary>The tutorial's darkening, hint and dialog hint (OM-53); none when nothing of it shows.</summary>
    public TutorialPicture? Tutorial { get; set; }

    /// <summary>
    /// The interface's size (OM-87): every piece in screen pixels drawn this much bigger, laid out on a screen this much
    /// smaller (the world's points projected onto it). Kept from frame to frame (not cleared).
    /// </summary>
    public float Scale { get; set; } = 1;

    public void Clear()
    {
        Boxes.Clear();
        Bars.Clear();
        Queues.Clear();
        Keys.Clear();
        TopBar = null;
        Numbers.Clear();
        Wheel = null;
        Letterbox = null;
        Prompt = null;
        Tutorial = null;
    }
}

/// <summary>
/// The tutorial on the screen: how dark it is (0…1), the hint's text, place and alpha, the dialog hint's text, its
/// element's icon or its magick's picture (none: neither), alpha, scale and size (none: its text's).
/// </summary>
public readonly record struct TutorialPicture(float Dark, string? Hint, Scripting.HintPosition Position, float HintAlpha, string? Dialog,
    Gameplay.Spells.Elements Element, Gameplay.Spells.MagickType Magick, float DialogAlpha, float Scale, Vector2? Size);

/// <summary>
/// A speech box to draw: its text (wrapped) and how many of its glyphs show, its speaker's name, how far it is open,
/// where (over a point of the world, at a point of the screen −1…1, or the cutscene's place at the bottom), the
/// smallest size its message's lines fit (in the wrapping font), and whether it keeps on the screen.
/// </summary>
public readonly record struct SpeechBoxPicture(string Text, int Visible, string? Name, float Scale, Vector3? At, Vector2? OnScreen, Point MinSize,
    bool ForceOnScreen, bool Cutscene);

/// <summary>The interact prompt ("Talk"): its text, at the mouse in pixels, or over a point of the world with a pad's button; how much it shows.</summary>
public readonly record struct PromptPicture(string Text, Vector2? Mouse, Vector3? Avatar, float Alpha);

/// <summary>
/// A health bar: the world point it hangs under (its top 16.5 px below it, centred), its share, its width (of the
/// 64-unit bar, at most 1.5) and height (of 8) scales, its colour (rgb may pass 1: the HUD's colours are bright) and
/// alpha, and how many pixels lower it sits (a stacked bar's).
/// </summary>
public readonly record struct HealthBarPicture(Vector3 At, float Share, float Width, float Height, Vector4 Colour, float Lower);

/// <summary>
/// A wizard's queue on the HUD: the world point of its feet, its icons, the selected magick's recipe from the slot
/// the queue has reached (translucent), its name and how much it shows.
/// </summary>
public sealed record QueuePicture(Vector3 Feet, IReadOnlyList<Hud.QueueIcon> Icons, IReadOnlyList<Gameplay.Spells.Elements> Recipe, int RecipeFrom,
    string? MagickName, float MagickAlpha);

/// <summary>A bar across the screen's top: its width (a share of the screen's), the share it shows, its fill's colour and its alpha.</summary>
public readonly record struct TopBarPicture(float Width, float Shown, Vector4 Fill, float Alpha);
