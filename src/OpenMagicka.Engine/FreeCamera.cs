using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine;

/// <summary>
/// A fly-through camera for the viewer: a position and yaw/pitch angles (radians; yaw 0 looks
/// along -Z, positive yaw turns left), Y up. Immutable: moves return a new camera.
/// </summary>
public sealed record FreeCamera(Vector3 Position, float Yaw, float Pitch)
{
    /// <summary>Just short of straight up or down, where the view matrix would degenerate.</summary>
    public const float MaxPitch = 1.55f;

    public Vector3 Forward => new(-MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.Up));

    public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.Up);

    /// <summary>Moves by the given amounts (-1..1 each) along the view direction, its right and world up.</summary>
    public FreeCamera Move(float forward, float right, float up, float dt, float speed) =>
        this with { Position = Position + (Forward * forward + Right * right + Vector3.Up * up) * speed * dt };

    public FreeCamera Turn(float yaw, float pitch) =>
        this with { Yaw = Yaw + yaw, Pitch = Math.Clamp(Pitch + pitch, -MaxPitch, MaxPitch) };

    public static FreeCamera Lerp(FreeCamera a, FreeCamera b, float amount)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new FreeCamera(Vector3.Lerp(a.Position, b.Position, amount),
            MathHelper.Lerp(a.Yaw, b.Yaw, amount), MathHelper.Lerp(a.Pitch, b.Pitch, amount));
    }

    /// <summary>A camera above and in front of a box, looking at its centre, far enough to see most of it.</summary>
    public static FreeCamera Framing(BoundingBox box)
    {
        var center = (box.Min + box.Max) / 2;
        var size = Math.Max((box.Max - box.Min).Length(), 1);
        var position = center + new Vector3(0, 0.6f, 0.8f) * size * 0.6f;
        var direction = Vector3.Normalize(center - position);
        return new FreeCamera(position, MathF.Atan2(-direction.X, -direction.Z), MathF.Asin(direction.Y));
    }
}
