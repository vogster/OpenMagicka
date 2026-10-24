using System.Numerics;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Animation.Actions;

/// <summary>Pushes the body along with the clip: a velocity in the character's frame (−Z forward), times the animation speed.</summary>
public sealed class Move(float start, float end, Vector3 velocity) : AnimationAction(start, end)
{
    public Vector3 Velocity { get; } = Finite(velocity, nameof(velocity));

    public override void Run(Character owner, bool first)
    {
        var forward = owner.Body.Facing;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var world = Velocity.X * right + Velocity.Y * Vector3.UnitY - Velocity.Z * forward;
        owner.Body.AddExtraForce(world * owner.AnimationSpeed);
    }
}

/// <summary>A non-player character leaps at its target (ENTITIES_AI §4.2, with the original's squared distance).</summary>
public sealed class Jump(float start, float end, float elevationDegrees, float? minRange, float? maxRange) : AnimationAction(start, end)
{
    public float Elevation { get; } = Finite(elevationDegrees, nameof(elevationDegrees)) * MathF.PI / 180;

    public float? MinRange { get; } = minRange is { } min ? Finite(min, nameof(minRange)) : null;

    public float? MaxRange { get; } = maxRange is { } max ? Finite(max, nameof(maxRange)) : null;

    public override void Run(Character owner, bool first)
    {
        if (!first || owner.IsPlayer || owner.IsDead || owner.IsEntangled)
        {
            return;
        }
        var (min, max) = (MinRange ?? 0, MaxRange ?? 0);
        var aim = owner.Target?.Position ?? owner.Body.Facing * (min + (max - min) * 0.5f);   // as the original: a point, not an offset
        var lead = owner.Target?.Velocity ?? Vector3.Zero;
        if (lead.LengthSquared() > float.Epsilon)
        {
            aim += lead * 0.5f;
        }
        var delta = aim - owner.Position;
        var distance = delta.LengthSquared();                                  // as the original: squared
        if (distance < float.Epsilon)
        {
            return;
        }
        distance *= 1.1f;
        distance = MinRange is { } low ? MathF.Max(low, distance) : distance;
        distance = MaxRange is { } high ? MathF.Min(high, distance) : distance;
        owner.Body.AllowMove = true;
        owner.Jump(Vector3.Normalize(delta) * distance, Elevation);
    }
}

/// <summary>A lower capsule for the window (a crouch or a duck), the template's again after.</summary>
public sealed class Crouch(float start, float end, float radius, float length) : AnimationAction(start, end)
{
    public float Radius { get; } = Finite(radius, nameof(radius));

    public float Length { get; } = Finite(length, nameof(length));

    public override void Run(Character owner, bool first)
    {
        if (first)
        {
            owner.SetCapsule(Radius, Length);
        }
    }

    public override void Stop(Character owner) => owner.ResetCapsule();
}
