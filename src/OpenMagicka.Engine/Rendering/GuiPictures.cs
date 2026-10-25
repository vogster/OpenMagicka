using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// What the screen shows over the world this frame (docs/engine/ARCHITECTURE.md §18): the speech boxes, and in a cutscene
/// its bars and skip prompt. Filled on the logic thread, drawn on the render thread.
/// </summary>
public sealed class GuiPictures
{
    public List<SpeechBoxPicture> Boxes { get; } = [];

    /// <summary>How far the cutscene's bars are in (0…1), and how much its skip prompt shows; none outside a cutscene.</summary>
    public (float Bars, float Skip, string SkipText)? Letterbox { get; set; }

    /// <summary>The interact prompt: its text, at the mouse (pixels) or over the avatar (a pad's, with its button), how much it shows.</summary>
    public PromptPicture? Prompt { get; set; }

    public void Clear()
    {
        Boxes.Clear();
        Letterbox = null;
        Prompt = null;
    }
}

/// <summary>
/// A speech box to draw: its text (wrapped) and how many of its glyphs show, its speaker's name, how far it is open,
/// where (over a point of the world, at a point of the screen −1…1, or the cutscene's place at the bottom), the
/// smallest size its message's lines fit (in the wrapping font), and whether it keeps on the screen.
/// </summary>
public readonly record struct SpeechBoxPicture(string Text, int Visible, string? Name, float Scale, Vector3? At, Vector2? OnScreen, Point MinSize,
    bool ForceOnScreen, bool Cutscene);

/// <summary>The interact prompt ("Talk"): its text, at the mouse in pixels, or over a point of the world with a pad's button; how much it shows.</summary>
public readonly record struct PromptPicture(string Text, Vector2? Mouse, Vector3? Avatar, float Alpha);
