using System.Numerics;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Scripting;

/// <summary>What a tutorial hint shows playing beside its text (the original's flags, by name in any case).</summary>
[Flags]
public enum HintAnimations
{
    None = 0,
    Interact = 0x1,
    NavMagick = 0x2,
    Conjure = 0x4,
    ConjureWater = 0x8,
    ConjureLife = 0x10,
    ConjureShield = 0x20,
    ConjureCold = 0x40,
    ConjureLightning = 0x80,
    ConjureArcane = 0x100,
    ConjureEarth = 0x200,
    ConjureFire = 0x400,
    CastForce = 0x800,
    CastArea = 0x1000,
    CastSelf = 0x2000,
    CastMagick = 0x4000,
    Block = 0x8000,
    Attack = 0x10000,
}

/// <summary>Where on the screen a hint stands.</summary>
public enum HintPosition { TopLeft, Top, TopRight, CenterLeft, Center, CenterRight, BottomLeft, Bottom, BottomRight }

/// <summary>
/// A tutorial hint: the text of a loc key (with <see cref="ForController"/>, its <c>_key</c> or <c>_pad</c>
/// form by the first local player's device), a clip beside it and where it stands. The HUD draws it (OM-49).
/// </summary>
public sealed record TutorialHint(string Key, bool ForController, HintAnimations Animation, HintPosition Position);

/// <summary>
/// A tutorial's dialog hint over an element or a magick: the text of a loc key, the trigger its closing
/// sets off, and its scale and size when given. The HUD draws it; a player's click or key closes it (OM-53).
/// </summary>
public sealed record TutorialDialogHint(Elements Element, MagickType Magick, string Key, int Trigger, float? Scale, Vector2? Size);

/// <summary>
/// The tutorial's state (the original's tutorial manager): the elements, cast types and push the players
/// may not use, the screen's darkening, and the hints up. It outlives a level's checkpoint, as the
/// original's; the players' controls read it (OM-56).
/// </summary>
public sealed class ScriptTutorial
{
    private readonly HashSet<CastType> castTypes = [];

    /// <summary>The elements the players may not conjure.</summary>
    public Elements DisabledElements { get; private set; }

    /// <summary>The cast types the players may not use.</summary>
    public IReadOnlySet<CastType> DisabledCastTypes => castTypes;

    public bool IsPushDisabled { get; set; }

    /// <summary>The screen is darkened around the tutorial's hints.</summary>
    public bool IsDark { get; set; }

    public TutorialHint? Hint { get; set; }

    public TutorialDialogHint? DialogHint { get; private set; }

    /// <summary>How long a new dialog hint ignores what would close it (the original's second), in game time.</summary>
    public const float HoldOff = 1;

    /// <summary>The time left before the dialog hint can be closed.</summary>
    public float Holding { get; private set; }

    /// <summary>A dialog hint comes up (in place of one up, whose trigger then never goes off), darkening the screen.</summary>
    public void Show(TutorialDialogHint hint)
    {
        DialogHint = hint ?? throw new ArgumentNullException(nameof(hint));
        Holding = HoldOff;
        IsDark = true;
    }

    /// <summary>The hold-off runs with the game's time (it stands while the game is paused).</summary>
    public void Step(float dt) => Holding = Math.Max(0, Holding - (float.IsFinite(dt) ? Math.Max(0, dt) : 0));

    /// <summary>
    /// The dialog hint is closed (a player's input, or <c>removeDialogHint</c>): the trigger to set off (0: none), or
    /// none when there is no hint or it is still holding off (the input is dropped, as the original's).
    /// </summary>
    public int? Close()
    {
        if (DialogHint is not { } hint || Holding > 0)
        {
            return null;
        }
        DialogHint = null;
        IsDark = false;
        return hint.Trigger;
    }

    /// <summary>The scene goes (or a checkpoint comes back): the dialog hint goes, its trigger never set off, and the darkening.</summary>
    public void Forget()
    {
        DialogHint = null;
        IsDark = false;
        Holding = 0;
    }

    public void Enable(Elements element) => DisabledElements &= ~element;

    public void Disable(Elements element) => DisabledElements |= element;

    public void Enable(CastType type) => castTypes.Remove(type);

    public void Disable(CastType type) => castTypes.Add(type);
}
