using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Cameras;

public enum CameraBehaviour : byte
{
    FollowPlayers,
    FollowEntity,
    /// <summary>Holds its target; the original never sets it.</summary>
    Locked,
    MoveToTarget,
}

public enum CameraInterpolation : byte
{
    /// <summary>A steady 100 units per second, divided by the camera's time.</summary>
    Linear,
    /// <summary>A spring that keeps last frame's speed.</summary>
    Interpolated,
}

/// <summary>What the game tells the camera each frame.</summary>
public sealed record CameraInputs
{
    public static CameraInputs None { get; } = new();

    /// <summary>The players' centre plus the enemies' pull (<see cref="CameraFocus.Players"/>); null with no player to follow.</summary>
    public Vector3? Players { get; init; }

    /// <summary>The followed entity's position; null when it is gone.</summary>
    public Vector3? Followed { get; init; }

    /// <summary>The living local players' positions, for the dynamic magnification.</summary>
    public IReadOnlyList<Vector3> LocalPlayers { get; init; } = [];
}

/// <summary>
/// The game's camera, the original's <c>MagickCamera</c> (docs/engine/ARCHITECTURE.md §6): a ground
/// point the eye hangs above and behind at a fixed angle, eased towards a target (the players, an
/// entity or a scripted point) and kept on the level's camera mesh or in a script's box, with a
/// zoom (magnification), a bias and a shake. Immutable: <see cref="Update"/> and the commands return
/// a new camera. Stopping at the frame's edges when a player is about to leave it needs the physics
/// (not done yet).
/// </summary>
public sealed record GameCamera
{
    public const float NearPlane = 105;
    public const float FarPlane = Rendering.DeferredRenderer.FarPlane;
    public const float EyeOfTheBeholderDistance = 200;

    /// <summary>The eye's offset from the ground point.</summary>
    public static readonly Vector3 Offset = new(0, 144, 171);

    public static readonly float DefaultFov = MathHelper.ToRadians(5);

    /// <summary>Forward tilted 40° down; the camera never turns.</summary>
    public static readonly Vector3 Direction = Vector3.TransformNormal(Vector3.Forward, Matrix.CreateFromAxisAngle(Vector3.Right, MathHelper.ToRadians(-40)));

    private const float LinearSpeed = 100;
    private const float Easing = 0.02f;
    private const float ShakeFrequency = 60;
    private const float ShakeScale = 0.0333f;

    public static GameCamera Initial { get; } = new();

    public Vector3 Ground { get; init; }

    public Vector3 Target { get; init; }

    /// <summary>The ground point's last movement (one frame's, not per second).</summary>
    public Vector3 Movement { get; init; }

    /// <summary>Where the eye was placed by the last update.</summary>
    public Vector3 Eye { get; init; } = Offset;

    public float Magnification { get; init; } = 1;

    public float TargetMagnification { get; init; } = 1;

    /// <summary>The scene's magnification (its <c>Magnify</c> setting); see <see cref="WithDefaultMagnification"/>.</summary>
    public float DefaultMagnification { get; init; } = 1;

    /// <summary>How slowly the camera follows (seconds, roughly); 0 jumps.</summary>
    public float Time { get; init; } = 1;

    public CameraBehaviour Behaviour { get; init; }

    public CameraInterpolation Interpolation { get; init; } = CameraInterpolation.Interpolated;

    public Vector3 Bias { get; init; }

    public Vector3 TargetBias { get; init; }

    public float BiasTime { get; init; } = 10;

    public float PlayerMagnification { get; init; } = 1;

    public float PlayerMagnificationTime { get; init; }

    /// <summary>The zoom range the camera picks from to keep the players in view; null when off.</summary>
    public (float Min, float Max)? DynamicMagnification { get; init; }

    public float ShakeMagnitude { get; init; }

    public float ShakeDuration { get; init; }

    public float ShakeTime { get; init; }

    /// <summary>The script's lock box; takes the place of the mesh while set.</summary>
    public CameraBox? Box { get; init; }

    public CameraMesh? Mesh { get; init; }

    public float Fov => DefaultFov / Magnification;

    public Vector3 EyeOfTheBeholder => Eye + Direction * EyeOfTheBeholderDistance;

    public Matrix View => Matrix.CreateLookAt(Eye, Eye + Direction, Vector3.Up);

    public Matrix Projection(float aspectRatio) => Matrix.CreatePerspectiveFieldOfView(Fov, aspectRatio, NearPlane, FarPlane);

    /// <summary>One frame of <paramref name="dt"/> seconds, in the original's order.</summary>
    public GameCamera Update(float dt, CameraInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (!(dt >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "a frame lasts zero or more seconds");
        }
        var (behaviour, target, time) = NextTarget(inputs, dt);
        // A bias time of zero (or less) takes the new bias at once instead of dividing by it.
        var bias = BiasTime > 0 ? Vector3.Lerp(TargetBias, Bias, MathF.Pow(Easing, dt / BiasTime)) : TargetBias;
        var player = behaviour == CameraBehaviour.FollowPlayers && PlayerMagnificationTime > 0 ? PlayerMagnification : 1;
        var (ground, magnification) = time > 0
            ? (Step(target, time, dt), MathHelper.Lerp(TargetMagnification * player, Magnification, MathF.Pow(Easing, dt / time)))
            : (target, TargetMagnification);
        var targetMagnification = DynamicMagnification is { } range && behaviour == CameraBehaviour.FollowPlayers
            ? Math.Clamp(0.5f * DefaultFov / WidestAngle(inputs.LocalPlayers), range.Min, range.Max)
            : TargetMagnification;
        var movement = ground - Ground;
        var eye = ground + Offset;
        if (ShakeTime > 0 && dt > float.Epsilon)
        {
            // After the eye is placed: the shake shows next frame.
            ground.Y += MathF.Sin(MathHelper.WrapAngle(ShakeTime * ShakeFrequency)) * ShakeMagnitude * (ShakeTime / ShakeDuration) * ShakeScale;
        }
        return this with
        {
            Behaviour = behaviour, Target = target, Time = time, Bias = bias,
            Ground = ground, Movement = movement, Eye = eye,
            Magnification = magnification, TargetMagnification = targetMagnification,
            PlayerMagnificationTime = PlayerMagnificationTime - dt, ShakeTime = ShakeTime - dt,
        };
    }

    /// <summary>
    /// Back to following the players: the given time, interpolated, nothing followed; by default
    /// without the box and at the default zoom. The original has three variants: both resets, the
    /// zoom reset only, and neither.
    /// </summary>
    public GameCamera Release(float time, bool releaseBox = true, bool resetMagnification = true) => this with
    {
        Time = time, Behaviour = CameraBehaviour.FollowPlayers, Interpolation = CameraInterpolation.Interpolated,
        Box = releaseBox ? null : Box,
        TargetMagnification = resetMagnification ? DefaultMagnification : TargetMagnification,
    };

    public GameCamera LockOn(CameraBox box, float time) => this with { Box = box, Time = time };

    /// <summary>Follows an entity, whose position comes in <see cref="CameraInputs.Followed"/>.</summary>
    public GameCamera Follow() => this with { Behaviour = CameraBehaviour.FollowEntity };

    public GameCamera MoveTo(Vector3 target, float time) => this with { Behaviour = CameraBehaviour.MoveToTarget, Target = target, Time = time };

    /// <summary>Puts the ground point and the target at <paramref name="position"/>.</summary>
    public GameCamera At(Vector3 position) => this with { Ground = position, Target = position };

    /// <summary>Puts the ground point at <paramref name="position"/>, snapped to the box or mesh when asked; the target stays.</summary>
    public GameCamera Place(Vector3 position, bool snap) => this with { Ground = snap ? Snap(position) : position };

    public GameCamera Shake(float magnitude, float time) => this with { ShakeMagnitude = magnitude, ShakeDuration = time, ShakeTime = time };

    public GameCamera WithBias(Vector3 bias, float time) => this with { TargetBias = bias, BiasTime = time };

    public GameCamera WithPlayerMagnification(float magnification, float time) => this with { PlayerMagnification = magnification, PlayerMagnificationTime = time };

    public GameCamera WithDynamicMagnification(float min, float max)
    {
        RequirePositive(min, nameof(min));
        RequirePositive(max, nameof(max));
        if (min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), min, $"the smallest magnification is above the largest ({max})");
        }
        return this with { DynamicMagnification = (min, max) };
    }

    /// <summary>A new default zoom; the target follows when it was still at the old default.</summary>
    public GameCamera WithDefaultMagnification(float value)
    {
        RequirePositive(value, nameof(value));
        return this with
        {
            DefaultMagnification = value,
            TargetMagnification = MathF.Abs(TargetMagnification - DefaultMagnification) < 0.0001f ? value : TargetMagnification,
        };
    }

    /// <summary>A zoom must be positive and finite: the field of view is divided by it.</summary>
    private static void RequirePositive(float magnification, string name)
    {
        if (!(magnification > 0) || !float.IsFinite(magnification))
        {
            throw new ArgumentOutOfRangeException(name, magnification, "a magnification is positive and finite");
        }
    }

    private (CameraBehaviour Behaviour, Vector3 Target, float Time) NextTarget(CameraInputs inputs, float dt)
    {
        switch (Behaviour)
        {
            case CameraBehaviour.FollowPlayers:
                return (Behaviour, Snap(inputs.Players is { } centre ? centre + Bias : Ground), Time + (1 - Time) * dt);
            case CameraBehaviour.FollowEntity:
                // When the entity is gone the target stays as it was, unsnapped, as in the original.
                // When the entity is gone the target stays as it was, unsnapped, as in the original.
                return inputs.Followed is { } followed
                    ? (Behaviour, Snap(followed), Time)
                    : (CameraBehaviour.FollowPlayers, Target, Time);
            default:
                return (Behaviour, Target, Time);
        }
    }

    private Vector3 Step(Vector3 target, float time, float dt)
    {
        var toTarget = target - Ground;
        if (Interpolation == CameraInterpolation.Linear)
        {
            var reach = LinearSpeed * dt / time;
            return reach < toTarget.Length() ? Ground + toTarget * (reach / toTarget.Length()) : target;
        }
        if (dt <= float.Epsilon)
        {
            return Ground;
        }
        var speed = Movement / dt;
        var step = (speed + toTarget * (2 / time) - speed * 0.5f) * dt;
        return step.LengthSquared() > toTarget.LengthSquared() ? target : Ground + step;
    }

    /// <summary>The widest angle between the view and a player, seen from the last frame's eye; 0 without players.</summary>
    private float WidestAngle(IReadOnlyList<Vector3> players)
    {
        var widest = 0f;
        foreach (var player in players)
        {
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Direction, Vector3.Normalize(player - Eye)), -1, 1));
            if (angle >= widest)   // a player at the eye (NaN) does not count
            {
                widest = angle;
            }
        }
        return widest;
    }

    private Vector3 Snap(Vector3 position) => Box is { } box ? box.ClosestPoint(position) : Mesh?.ClosestPoint(position) ?? position;
}
