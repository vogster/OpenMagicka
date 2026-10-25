using System.Numerics;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Scripting;

/// <summary><c>enableElement element=</c> and <c>disableElement</c>: the tutorial lets the players conjure the element, or not.</summary>
public abstract class ElementLockAction(bool enable) : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Elements Element { get; set; }

    protected override void Execute()
    {
        if (enable)
        {
            Scene.Game.Tutorial.Enable(Element);
        }
        else
        {
            Scene.Game.Tutorial.Disable(Element);
        }
    }
}

public sealed class EnableElementAction() : ElementLockAction(enable: true);

public sealed class DisableElementAction() : ElementLockAction(enable: false);

/// <summary>The cast types as the scripts name them: the original's, where a blast is the "none" cast.</summary>
public enum ScriptCastType
{
    None = 0,
    Blast = 0,
    Force = 1,
    Area = 2,
    Self = 3,
    Weapon = 4,
    Magick = 5,
}

/// <summary><c>enableCastType type=</c> and <c>disableCastType</c>: the tutorial lets the players cast that way, or not.</summary>
public abstract class CastTypeLockAction(bool enable) : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public ScriptCastType Type { get; set; }

    protected override void Execute()
    {
        if (enable)
        {
            Scene.Game.Tutorial.Enable((CastType)Type);
        }
        else
        {
            Scene.Game.Tutorial.Disable((CastType)Type);
        }
    }
}

public sealed class EnableCastTypeAction() : CastTypeLockAction(enable: true);

public sealed class DisableCastTypeAction() : CastTypeLockAction(enable: false);

/// <summary>A tutorial switch a replay sets again: push allowed or not, the darkening on or off.</summary>
public abstract class TutorialSwitchAction(Action<ScriptTutorial> change) : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => change(Scene.Game.Tutorial);
}

public sealed class EnablePushAction() : TutorialSwitchAction(t => t.IsPushDisabled = false);

public sealed class DisablePushAction() : TutorialSwitchAction(t => t.IsPushDisabled = true);

public sealed class EnableDarkAction() : TutorialSwitchAction(t => t.IsDark = true);

public sealed class DisableDarkAction() : TutorialSwitchAction(t => t.IsDark = false);

/// <summary><c>removeHint</c>: the tutorial's hint goes.</summary>
public sealed class RemoveHintAction() : TutorialSwitchAction(t => t.Hint = null);

/// <summary><c>removeDialogHint</c>: the tutorial's dialog hint goes.</summary>
public sealed class RemoveDialogHintAction() : TutorialSwitchAction(t => t.DialogHint = null);

/// <summary>
/// <c>setHint id= appendController= animation= position=</c>: the tutorial's hint shows the loc key's text
/// (its <c>_key</c> or <c>_pad</c> form with <c>appendController</c>), the clip and the place. On the
/// original's other HUD the bottom right moves to the centre right; ours has one HUD.
/// </summary>
public sealed class SetHintAction : ScriptAction
{
    public bool AppendController { get; set; }
    public string Id { get; set; } = "";
    public HintAnimations Animation { get; set; }
    public HintPosition Position { get; set; } = HintPosition.BottomRight;

    protected override void Execute() => Scene.Game.Tutorial.Hint = new TutorialHint(Id, AppendController, Animation, Position);
}

/// <summary>
/// <c>setDialogHint id= element= magick= trigger= scale= size=</c>: the tutorial's dialog hint over the
/// magick (when one is given) or the element, the loc key's text; its closing sets off the trigger. A
/// scale and a size are taken when over 0.
/// </summary>
public sealed class SetDialogHintAction : ScriptAction
{
    public string Trigger { get; set; } = "";
    public float Scale { get; set; }
    public Vector2 Size { get; set; }
    public Elements Element { get; set; }
    public MagickType Magick { get; set; }
    public int Custom { get; set; }
    public string Id { get; set; } = "";

    protected override void Execute()
    {
        Scene.Game.DialogHintDone = false;
        Scene.Game.Tutorial.DialogHint = new TutorialDialogHint(Magick == MagickType.None ? Element : Elements.None, Magick, Id,
            Trigger.Length > 0 ? Hash(Trigger) : 0, Scale > 0 ? Scale : null, Size.X > 0 && Size.Y > 0 ? Size : null);
    }
}

/// <summary>The tutorial's telemetry (<c>telemetryTutorialBegin</c>, <c>…Step</c>, <c>…End</c>, <c>…Skip</c>) and <c>printDebug</c>: nothing, as the original's release.</summary>
public sealed class NothingAction : ScriptAction
{
    public string TutorialName { get; set; } = "";
    public string StepName { get; set; } = "";
    public string Message { get; set; } = "";
    public string EntityPos { get; set; } = "";
    public string CounterName { get; set; } = "";
    public bool Break { get; set; }

    protected override void Execute()
    {
    }
}
