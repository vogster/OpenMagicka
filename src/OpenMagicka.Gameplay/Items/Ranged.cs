using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Items;

/// <summary>What an item throws (docs/engine/ENTITIES_AI.md §8.1.4).</summary>
/// <param name="Elevation">Radians above the facing when no velocity is given.</param>
/// <param name="ProjectileRadius">The missile's radius: its model's, or 0.25 without one.</param>
public sealed record RangedTemplate(float Range, IReadOnlyList<EventGroup> Events, float Elevation = 0, float Homing = 0, float Danger = 2,
    bool FacesVelocity = true, float ProjectileRadius = Ranged.DefaultRadius)
{
    public void Validate()
    {
        foreach (var (value, name) in new[] { (Range, nameof(Range)), (Elevation, nameof(Elevation)), (Homing, nameof(Homing)), (Danger, nameof(Danger)) })
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");
            }
        }
        if (Range < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Range), Range, "must not be negative");
        }
        if (!(ProjectileRadius > 0) || !float.IsFinite(ProjectileRadius))
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectileRadius), ProjectileRadius, "must be positive");
        }
        ArgumentNullException.ThrowIfNull(Events);
        foreach (var group in Events)
        {
            ArgumentNullException.ThrowIfNull(group, nameof(Events));
            group.Validate();
        }
    }
}

/// <summary>Throwing an item's missile (docs/engine/ENTITIES_AI.md §8.1.4).</summary>
public static class Ranged
{
    public const float DefaultRadius = 0.25f;

    /// <summary>The Y given to a throw with no elevation (not normalised, as the original).</summary>
    public const float FlatLift = 0.05f;

    /// <summary>
    /// Throws a missile from the item's tip: along <paramref name="velocity"/> turned from the holder's
    /// (or, <paramref name="itemAligned"/>, the item's) frame, else along the facing at the item's elevation.
    /// </summary>
    public static Missile? Launch(Character owner, Item item, Vector3? velocity, bool itemAligned)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Template.Ranged is not { } ranged || ranged.Events.Count == 0 || owner.Manager is not { } manager || owner.World.IsDisposed
            || !Passives.MayThrow(owner, item))
        {
            return null;
        }
        item.Cooldown = item.Template.CooldownTime;
        var aimed = item.AimedBy is var (ability, target) && target.Manager is not null && !target.IsDead ? (ability, target) : ((AI.RangedAbility, Entity)?)null;
        item.AimedBy = null;
        var throwing = velocity is { } given ? Turn(given, itemAligned ? item.Transform : owner.RootMatrix())
            : aimed is var (by, at) ? AimAt(owner, item, ranged, by, at) : Ahead(owner, ranged);
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return null;                                                      // no room: as the original's pool running dry
        }
        var gungnir = item.Template.PassiveAbility == PassiveAbility.Gungner;
        var missile = new Missile(owner.World, owner, item.Tip, throwing, gungnir ? Passives.GungnirRadius : ranged.ProjectileRadius, ranged.Events,
            target: gungnir ? null : aimed?.Item2, homing: gungnir || aimed is null ? 0 : ranged.Homing, danger: ranged.Danger, facesVelocity: gungnir || ranged.FacesVelocity)
        {
            Look = ranged,                                                    // its item's projectile model
        };
        try
        {
            manager.Add(missile);
        }
        catch
        {
            owner.World.Remove(missile.Body);
            throw;
        }
        item.Thrown = gungnir ? missile : item.Thrown;                       // before its first events, which may throw
        missile.Start();
        return missile;
    }

    /// <summary>A direction in a frame, without its translation or scale.</summary>
    internal static Vector3 Turn(Vector3 local, Matrix4x4 frame)
    {
        var (right, up, back) = (Axis(frame.M11, frame.M12, frame.M13), Axis(frame.M21, frame.M22, frame.M23), Axis(frame.M31, frame.M32, frame.M33));
        return local.X * right + local.Y * up + local.Z * back;

        static Vector3 Axis(float x, float y, float z)
        {
            var axis = new Vector3(x, y, z);
            return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.Zero;
        }
    }

    /// <summary>
    /// An NPC's aimed throw (§8.1.4): along the facing tipped up by the ability's and the item's
    /// elevations, at the speed that lands it on the target under gravity (twice the range when none
    /// does), both off by the ability's accuracy: a random share of what it lacks, of the speed and of a
    /// quarter turn.
    /// </summary>
    private static Vector3 AimAt(Character owner, Item item, RangedTemplate ranged, AI.RangedAbility ability, Entity target)
    {
        var origin = item.Position;
        var rise = target.Position.Y - origin.Y;
        var distance = Vector3.Distance(target.Position with { Y = 0 }, origin with { Y = 0 });
        var elevation = ability.Elevation + ranged.Elevation;
        var (sin, cos) = MathF.SinCos(elevation);
        var direction = new Vector3(owner.Body.Facing.X * cos, sin, owner.Body.Facing.Z * cos);
        var miss = ability.Accuracy * (float)(owner.NextRandom() * 2 - 1) * MathF.Max(1 - ability.Accuracy, 0);
        var gravity = -owner.World.Settings.Gravity.Y;
        var speed = MathF.Sqrt(gravity * distance * distance / (2 * (distance * sin / cos - rise) * cos * cos));
        if (!float.IsFinite(speed))
        {
            speed = ranged.Range * 2;
        }
        speed *= miss + 1;
        return Vector3.Transform(direction, Quaternion.CreateFromAxisAngle(Vector3.UnitY, miss * MathF.PI / 4)) * speed;
    }

    /// <summary>The facing tipped up by the elevation (or lifted by 0.05), times twice the range.</summary>
    private static Vector3 Ahead(Character owner, RangedTemplate ranged)
    {
        var direction = owner.Body.Facing;
        if (ranged.Elevation > 0)
        {
            var (sin, cos) = MathF.SinCos(ranged.Elevation);
            direction = new Vector3(direction.X * cos, sin, direction.Z * cos);
        }
        else
        {
            direction.Y = FlatLift;
        }
        return direction * ranged.Range * 2;
    }
}
