using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Props;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// <c>stopAnimation name= children=</c>: the animated part (a path, "parent/child") stops where it is, its
/// children with it unless told not. A part the level lacks crashes the original; here nothing stops.
/// </summary>
public sealed class StopAnimationAction : ScriptAction
{
    public string Name { get; set; } = "";
    public bool Children { get; set; } = true;

    protected override void Execute() => Scene.Places.Part(Name)?.Stop(Children);
}

/// <summary>
/// <c>resumeAnimation name= length= speed= loop= children=</c>: the part plays on from where it is, for the
/// length (none: to the clip's end), at the speed; a loop given is taken. A negative length fails the
/// loading (the original throws as it runs).
/// </summary>
public sealed class ResumeAnimationAction : ScriptAction
{
    public string Name { get; set; } = "";
    public float Speed { get; set; } = 1;
    public float Length { get; set; }
    public bool Children { get; set; } = true;
    public bool? Loop { get; set; }

    public override void Initialize()
    {
        if (!(Length >= 0) || !float.IsFinite(Length) || !float.IsFinite(Speed))
        {
            throw new InvalidDataException($"<resumeAnimation name=\"{Name}\">: the length must be finite and not negative, the speed finite");
        }
    }

    protected override void Execute() => Scene.Places.Part(Name)?.Resume(Children, Length, Speed, Loop);
}

/// <summary><c>highlightAnim name= time=</c>: the part shows highlighted for the time (the renderer draws it).</summary>
public sealed class HighlightAnimAction : ScriptAction
{
    public string Name { get; set; } = "";
    public float Time { get; set; } = 1;

    public override void Initialize()
    {
        if (!float.IsFinite(Time))
        {
            throw new InvalidDataException("<highlightAnim>: the time must be finite");
        }
    }

    protected override void Execute() => Scene.Places.Part(Name)?.Highlight(Time);
}

/// <summary><c>highlightPhys id= time=</c>: the named physics prop shows highlighted for the time.</summary>
public sealed class HighlightPhysAction : ScriptAction
{
    public string Id { get; set; } = "";
    public float Time { get; set; }

    public override void Initialize()
    {
        if (!float.IsFinite(Time))
        {
            throw new InvalidDataException("<highlightPhys>: the time must be finite");
        }
    }

    protected override void Execute()
    {
        if (Scene.World.Entities.FindByUniqueId(Hash(Id)) is PhysicsProp prop)
        {
            prop.Highlight(Time);
        }
    }
}

/// <summary><c>setDialog id= dialog=</c>: talking to the named character starts the dialog.</summary>
public sealed class SetDialogAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Dialog { get; set; } = "";
    public string Id { get; set; } = "";

    protected override void Execute()
    {
        if (Scene.World.Entities.FindByUniqueId(Hash(Id)) is Character character)
        {
            character.Dialog = Dialog.Length > 0 ? Hash(Dialog) : 0;
        }
    }
}
