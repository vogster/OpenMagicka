using System.Numerics;
using OpenMagicka.Engine.Cameras;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// <c>cameraMove target= offset= time= magnification= interpolation= lockInput=</c>: the camera moves to a
/// locator (none found: where it is) plus the offset, over the time; a magnification over 0 zooms; the
/// lock holds the players' input until the camera is released.
/// </summary>
public sealed class CameraMoveAction : ScriptAction
{
    public string Target { get; set; } = "";
    public Vector3 Offset { get; set; }
    public float Time { get; set; } = 1;
    public float Magnification { get; set; } = -1;
    public CameraInterpolation Interpolation { get; set; } = CameraInterpolation.Interpolated;
    public bool LockInput { get; set; }

    protected override void Execute()
    {
        var stage = Scene.Stage;
        var to = Scene.Places.LocatorTransform(Hash(Target)) is { } locator
            ? new Microsoft.Xna.Framework.Vector3(locator.M41, locator.M42, locator.M43)
            : stage.Camera.Ground;
        stage.Camera = stage.Camera.MoveTo(to + new Microsoft.Xna.Framework.Vector3(Offset.X, Offset.Y, Offset.Z), Time) with { Interpolation = Interpolation };
        if (Magnification > 0 && float.IsFinite(Magnification))
        {
            stage.Camera = stage.Camera with { TargetMagnification = Magnification };
        }
        Scene.LockInputToCamera(LockInput);
    }
}

/// <summary><c>cameraRelease time=</c>: back to following the players, unboxed and at the default zoom, the camera's lock gone.</summary>
public sealed class CameraReleaseAction : ScriptAction
{
    public float Time { get; set; } = 1;

    protected override void Execute()
    {
        Scene.Stage.Camera = Scene.Stage.Camera.Release(Time);
        Scene.LockInputToCamera(false);
    }
}

/// <summary><c>cameraShake magnitude= time= area=</c>: the camera shakes; at a place, the pads rumble too by the players' distance (OM-68).</summary>
public sealed class CameraShakeAction : ScriptAction
{
    public float Magnitude { get; set; }
    public float Time { get; set; }
    public string Area { get; set; } = "";

    protected override void Execute() => Scene.Stage.Camera = Scene.Stage.Camera.Shake(Magnitude, Time);
}

/// <summary><c>cameraMagnify magnification= time=</c>: the camera zooms to the magnification over the time.</summary>
public sealed class CameraMagnifyAction : ScriptAction
{
    public float Magnification { get; set; } = 1;
    public float Time { get; set; } = 1;

    public override void Initialize()
    {
        if (!(Magnification > 0) || !float.IsFinite(Magnification))
        {
            throw new InvalidDataException($"<cameraMagnify>: the magnification {Magnification} must be positive");
        }
    }

    protected override void Execute() => Scene.Stage.Camera = Scene.Stage.Camera with { TargetMagnification = Magnification, Time = Time };
}

/// <summary><c>fade fadeIn= color= time= override=</c>: the screen fades to the colour (in) or back (out); an override cancels the scene change's fade.</summary>
public sealed class FadeAction : ScriptAction
{
    public bool FadeIn { get; set; }
    public Vector4 Color { get; set; }
    public float Time { get; set; }
    public bool Override { get; set; }

    protected override void Execute() => Scene.Stage.Fade = new ScreenFade(FadeIn, Color, Time, Override);
}

/// <summary>
/// <c>cutscene onSkip= skipBarMove= killDialogs=</c>: a cutscene begins: the HUD goes, the dialogs end
/// (unless kept), and the world near the camera is calmed (<see cref="Gameplay.Entities.CutsceneCalm"/>).
/// Without <c>onSkip</c> it cannot be skipped.
/// </summary>
public sealed class CutsceneAction : ScriptAction
{
    /// <summary>A replay begins it again, the bars in at once (as the original).</summary>
    public override void Replay() => Scene.BeginCutscene(OnSkip.Length > 0 ? Hash(OnSkip) : 0, skipBarMove: true, KillDialogs);

    public string OnSkip { get; set; } = "";
    public bool SkipBarMove { get; set; }
    public bool KillDialogs { get; set; } = true;

    protected override void Execute() => Scene.BeginCutscene(OnSkip.Length > 0 ? Hash(OnSkip) : 0, SkipBarMove, KillDialogs);
}

/// <summary><c>endCutscene skipBarMove=</c>: the cutscene ends, the HUD back, the dialogs ended.</summary>
public sealed class EndCutsceneAction : ScriptAction
{
    /// <summary>A replay ends it again, the bars out at once (as the original).</summary>
    public override void Replay() => Scene.EndCutscene(skipBarMove: true);

    public bool SkipBarMove { get; set; }

    protected override void Execute() => Scene.EndCutscene(SkipBarMove);
}

/// <summary><c>disableInput</c>: the level holds the players' input back.</summary>
public sealed class DisableInputAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Stage.LimitInput(Scene.Level);
}

/// <summary><c>enableInput</c>: the level's hold on input goes (others stay).</summary>
public sealed class EnableInputAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Stage.UnlimitInput(Scene.Level);
}

/// <summary><c>cameraLock area= time=</c>: the camera is kept in the trigger area's box, moving there over the time.</summary>
public sealed class CameraLockAction : ScriptAction
{
    public float Time { get; set; } = 1;
    public string Area { get; set; } = "";

    protected override void Execute()
    {
        if (Scene.Places.Area(Hash(Area)) is { } area && area.Key != ScriptPlaces.Any)
        {
            var box = new CameraBox(new Microsoft.Xna.Framework.Vector3(area.Corner.X, area.Corner.Y, area.Corner.Z),
                new Microsoft.Xna.Framework.Vector3(area.Sides.X, area.Sides.Y, area.Sides.Z),
                new Microsoft.Xna.Framework.Quaternion(area.Orientation.X, area.Orientation.Y, area.Orientation.Z, area.Orientation.W));
            Scene.Stage.Camera = Scene.Stage.Camera.LockOn(box, Time);
        }
    }
}

/// <summary><c>cameraBias bias= time=</c>: the camera's point shifts by the bias over the time.</summary>
public sealed class CameraBiasAction : ScriptAction
{
    public Vector3 Bias { get; set; }
    public float Time { get; set; } = 20;

    protected override void Execute() =>
        Scene.Stage.Camera = Scene.Stage.Camera.WithBias(new Microsoft.Xna.Framework.Vector3(Bias.X, Bias.Y, Bias.Z), Time);
}

/// <summary><c>cameraFollow id=</c>: the camera follows the named entity (none: it follows nothing, holding still).</summary>
public sealed class CameraFollowAction : ScriptAction
{
    public string Id { get; set; } = "";

    protected override void Execute()
    {
        Scene.Stage.Followed = Scene.World.Entities.FindByUniqueId(Hash(Id));
        Scene.Stage.Camera = Scene.Stage.Camera.Follow();
    }
}

/// <summary>
/// <c>cameraPosition target= offset= magnification=</c>: the camera stands at once at the locator (none: where
/// it stands) plus the offset; a magnification over 0 zooms at once.
/// </summary>
public sealed class CameraPositionAction : ScriptAction
{
    public string Target { get; set; } = "";
    public Vector3 Offset { get; set; }
    public float Magnification { get; set; } = -1;

    protected override void Execute()
    {
        var stage = Scene.Stage;
        var at = Scene.Places.LocatorPosition(Hash(Target)) is { } locator ? new Microsoft.Xna.Framework.Vector3(locator.X, locator.Y, locator.Z) : stage.Camera.Ground;
        stage.Camera = stage.Camera.At(at + new Microsoft.Xna.Framework.Vector3(Offset.X, Offset.Y, Offset.Z));
        if (Magnification > 0 && float.IsFinite(Magnification))
        {
            stage.Camera = stage.Camera with { Magnification = Magnification, TargetMagnification = Magnification };
        }
    }
}

/// <summary><c>addCameraEffect effect=</c>: the camera's visual effect comes on.</summary>
public sealed class AddCameraEffectAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Effect { get; set; } = "";

    protected override void Execute() => Scene.Stage.AddCameraEffect(Effect);
}

/// <summary><c>removeCameraEffect effect=</c>: the camera's visual effect goes (none named: all of them).</summary>
public sealed class RemoveCameraEffectAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Effect { get; set; } = "";

    protected override void Execute() => Scene.Stage.RemoveCameraEffect(Effect);
}
