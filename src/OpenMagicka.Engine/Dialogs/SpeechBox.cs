using System.Numerics;
using OpenMagicka.Engine.Text;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Engine.Dialogs;

/// <summary>Where a dialog plays: on an entity (its owner), at a point in the world, or at a point of the screen (−1…1, y down).</summary>
public readonly record struct DialogAnchor(Entity? Owner, Vector3? At = null, Vector2? OnScreen = null);

/// <summary>
/// A speech box, as the original's (docs/engine/ARCHITECTURE.md §18): it pops open (0 to 1 in 0.25 s) and shut (in
/// 0.125 s), types its line once it is open, and, with a time to live, counts it down once the line is typed out; a
/// line with none waits for the player. The renderer measures and places it.
/// </summary>
public sealed class SpeechBox
{
    public const float Grow = 4, Shrink = 8;

    public TypingText Text { get; } = new();

    /// <summary>The speaker's name above the text, when the loc table has one for it.</summary>
    public string? Name { get; private set; }

    public DialogAnchor Anchor { get; private set; }

    /// <summary>The smallest box its message's lines fit (in the wrapping font).</summary>
    public (int Width, int Height) MinSize { get; private set; }

    public bool ForceOnScreen { get; private set; } = true;

    public bool IsGrowing { get; private set; }

    public float Scale { get; private set; }

    /// <summary>The line's time left once typed out; it advances by itself below 0 when it was given one.</summary>
    public float TimeToLive { get; private set; } = -1;

    public bool AdvancesByItself { get; private set; }

    public bool IsVisible => IsGrowing || Scale > 1e-4f;

    /// <summary>Opening, shutting or typing: a press finishes this before it goes on.</summary>
    public bool IsAnimating => (IsGrowing && Scale < 0.99f) || (!IsGrowing && Scale > 1e-4f) || !Text.IsFinished;

    /// <summary>A line to show: a shut box pops open anew, an open one keeps its size and types the new line.</summary>
    public void Fill(string text, string? name, (int, int) minSize, bool forceOnScreen, DialogAnchor anchor, float timeToLive)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsGrowing)
        {
            Scale = 0;
        }
        (IsGrowing, ForceOnScreen, TimeToLive, AdvancesByItself, Name, MinSize, Anchor) = (true, forceOnScreen, timeToLive, timeToLive > 0, name, minSize, anchor);
        Text.Set(text);
    }

    public void Hide() => IsGrowing = false;

    /// <summary>Its opening or shutting done at once and its line typed out; the line no longer advances by itself.</summary>
    public void Finish()
    {
        Scale = IsGrowing ? 1 : 0;
        Text.Finish();
        AdvancesByItself = false;
    }

    /// <summary>Its opening done at once and its line typed out (ending: its time is left as it is).</summary>
    public void Snap()
    {
        Scale = IsGrowing ? 1 : 0;
        Text.Finish();
    }

    /// <summary>A step; true when its line's time ran out and it should go on.</summary>
    public bool Update(float dt)
    {
        Scale = IsGrowing ? MathF.Min(Scale + Grow * dt, 1) : MathF.Max(Scale - Shrink * dt, 0);
        if (Scale > 0.99f)
        {
            Text.Update(dt);
        }
        if (!Text.IsFinished)
        {
            return false;
        }
        TimeToLive -= dt;
        return AdvancesByItself && TimeToLive < 0;
    }
}
