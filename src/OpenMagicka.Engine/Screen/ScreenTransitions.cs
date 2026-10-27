using System.Numerics;

namespace OpenMagicka.Engine.Screen;

/// <summary>The original's transitions: none, a fade to or from a colour, a cross-fade from the last frame captured.</summary>
public enum TransitionKind : byte { None, Fade, CrossFade }

/// <summary>Which way a transition runs: out covers the screen (toward its colour), in uncovers it.</summary>
public enum TransitionDirection : byte { Out, In }

/// <summary>A transition as a frame draws it: its kind, how covered the screen is (0…1) and its colour.</summary>
public readonly record struct TransitionPicture(TransitionKind Kind, float Amount, Vector3 Colour)
{
    public static TransitionPicture None => default;
}

/// <summary>
/// The screen's one transition (OM-67, docs/engine/ARCHITECTURE.md §3.2), as the original's render manager keeps it,
/// stepped on the logic thread: a begin starts it out (covering, from clear), an end in (uncovering, from covered),
/// either always from its start whatever the screen shows (an end on a clear screen flashes the colour), replacing
/// any running; none drops it. When it reaches its end the step says so, once, in either direction; an in is then
/// gone, an out stays, the screen covered, till another starts. The colour is kept as bytes, its alpha left out.
/// </summary>
public sealed class ScreenTransitions
{
    private TransitionKind kind;
    private TransitionDirection direction;
    private float amount, time;
    private Vector3 colour;

    public bool IsActive => kind != TransitionKind.None;

    /// <summary>An out has reached its colour: the screen is covered, and stays so till another starts.</summary>
    public bool IsCovered => IsActive && direction == TransitionDirection.Out && amount >= 1;

    public TransitionPicture Picture => IsActive ? new TransitionPicture(kind, amount, colour) : TransitionPicture.None;

    /// <summary>The screen goes to the colour over the time.</summary>
    public void Begin(TransitionKind kind, Vector3 colour, float time) => Start(kind, colour, time, TransitionDirection.Out);

    /// <summary>The screen comes back from the colour over the time.</summary>
    public void End(TransitionKind kind, Vector3 colour, float time) => Start(kind, colour, time, TransitionDirection.In);

    /// <summary>The transition goes, with no end said.</summary>
    public void Clear() => kind = TransitionKind.None;

    private void Start(TransitionKind kind, Vector3 colour, float time, TransitionDirection direction)
    {
        if (kind == TransitionKind.None)
        {
            Clear();
            return;
        }
        static float Byte(float c) => MathF.Round(Math.Clamp(float.IsFinite(c) ? c : 0, 0, 1) * 255) / 255;
        (this.kind, this.colour, this.time, this.direction) = (kind, new Vector3(Byte(colour.X), Byte(colour.Y), Byte(colour.Z)), time, direction);
        amount = direction == TransitionDirection.Out ? 0 : 1;
    }

    private bool Finished => direction == TransitionDirection.Out ? amount >= 1 : amount <= 0;

    /// <summary>
    /// A step of time: the direction it reached its end in this step, or none. A time of 0 ends in one step; ours: a negative
    /// or endless time too (the original's would never end), and a step not a number is none.
    /// </summary>
    public TransitionDirection? Step(float dt)
    {
        if (!IsActive)
        {
            return null;
        }
        var was = Finished;
        var step = time > 0 && float.IsFinite(time) ? (float.IsFinite(dt) ? Math.Max(0, dt) : 0) / time : 1;
        amount = Math.Clamp(amount + (direction == TransitionDirection.Out ? step : -step), 0, 1);
        if (was || !Finished)
        {
            return null;
        }
        if (direction == TransitionDirection.In)
        {
            Clear();
        }
        return direction;
    }
}
