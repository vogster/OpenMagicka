using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Cameras;

/// <summary>A player as the camera sees it: where it is, and the height of its feet when it stands on the ground.</summary>
public readonly record struct CameraSubject(Vector3 Position, float? Feet);

/// <summary>
/// Where the camera looks when it follows the players (docs/engine/ARCHITECTURE.md §6): the living
/// players' centre, pulled towards nearby enemies. The game core supplies the characters.
/// </summary>
public static class CameraFocus
{
    /// <summary>How fast an enemy's pull fades with its squared distance from the camera's ground point.</summary>
    public const float PullFalloff = 0.99775f;

    /// <summary>
    /// The local players' centre, or the network players' when there are no local ones, plus the
    /// enemies' pull; null when there are no players to follow.
    /// </summary>
    public static Vector3? Players(IReadOnlyList<CameraSubject> local, IReadOnlyList<CameraSubject> network, Vector3 ground, IReadOnlyList<Vector3> enemies)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(enemies);
        return (Centre(local) ?? Centre(network)) is { } centre ? centre + Pull(ground, enemies) : null;
    }

    /// <summary>
    /// The mean position, at the mean feet height of those on the ground; the mean height when none
    /// is or their feet heights add up to exactly zero (as the original does). Null without players.
    /// </summary>
    public static Vector3? Centre(IReadOnlyList<CameraSubject> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (players.Count == 0)
        {
            return null;
        }
        var sum = Vector3.Zero;
        var (feet, standing) = (0f, 0);
        foreach (var player in players)
        {
            sum += player.Position;
            if (player.Feet is { } height)
            {
                (feet, standing) = (feet + height, standing + 1);
            }
        }
        var centre = sum / players.Count;
        return MathF.Abs(feet) >= float.Epsilon ? centre with { Y = feet / standing } : centre;
    }

    /// <summary>Each enemy's offset from the ground point, weighted by <see cref="PullFalloff"/>^(squared distance), averaged.</summary>
    public static Vector3 Pull(Vector3 ground, IReadOnlyList<Vector3> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        if (enemies.Count == 0)
        {
            return Vector3.Zero;
        }
        var sum = Vector3.Zero;
        foreach (var enemy in enemies)
        {
            var offset = enemy - ground;
            sum += offset * (float)Math.Pow(PullFalloff, offset.LengthSquared());
        }
        return sum / enemies.Count;
    }
}
