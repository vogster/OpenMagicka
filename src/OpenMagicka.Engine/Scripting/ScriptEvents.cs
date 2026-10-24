using System.Globalization;
using System.Numerics;
using System.Xml;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Gameplay.AI;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// The AI events a script gives a character (LEVELS_TRIGGERS.md §5.7): <c>move</c>, <c>animation</c>,
/// <c>face</c>, <c>kill</c>, <c>loop</c>, read as the original reads them when the scene is built. A
/// walk to an area takes one random point of it then, for good; a name that is neither a locator nor an
/// area walks to the origin. An unknown event or attribute, or a direction of no length, fails the loading.
/// </summary>
public static class ScriptEvents
{
    /// <summary>The events among a node's children (comments and text skipped).</summary>
    public static AiEvent[] Read(ScriptNode parent, ScriptPlaces places, Random random)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return [.. parent.Children.Where(c => c.Type == XmlNodeType.Element).Select(c => Event(c, places, random))];
    }

    public static AiEvent Event(ScriptNode node, ScriptPlaces places, Random random)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(random);
        try
        {
            return node.Name.ToLowerInvariant() switch
            {
                "move" => Move(node, places, random),
                "animation" => Animation(node),
                "face" => Face(node),
                "kill" => Kill(node),
                "loop" => Loop(node),
                _ => throw new InvalidDataException($"no AI event \"{node.Name}\""),
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"<{node.Name}>: {e.Message}", e);
        }
    }

    private static MoveAiEvent Move(ScriptNode node, ScriptPlaces places, Random random)
    {
        var (waypoint, direction, fixedDirection, speed, delay, trigger) = (Vector3.Zero, Vector3.Zero, false, 1f, 0f, 0);
        foreach (var attribute in node.Attributes)
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "position" when Vector(attribute.Value) is { } point:
                    waypoint = point;
                    break;
                case "position":
                    var key = LegacyStringHash.Lower(attribute.Value);
                    if (places.LocatorTransform(key) is { } locator)
                    {
                        (waypoint, direction) = (locator.Translation, ScriptPlaces.Forward(locator));
                    }
                    else if (key != ScriptPlaces.Any && places.Area(key) is { } area)
                    {
                        waypoint = area.RandomPoint(random);
                    }
                    break;
                case "facingdirection" when Vector(attribute.Value) is { } way:
                    (fixedDirection, direction) = (true, Unit(way));
                    break;
                case "facingdirection":
                    fixedDirection = bool.Parse(attribute.Value);
                    break;
                case "delay":
                    delay = Number(attribute.Value);
                    break;
                case "speed":
                    speed = Number(attribute.Value);
                    break;
                case "trigger":
                    trigger = LegacyStringHash.Lower(attribute.Value);
                    break;
                default:
                    throw Invalid(attribute);
            }
        }
        return new MoveAiEvent(trigger, delay, waypoint, speed, fixedDirection, direction);
    }

    private static AnimationAiEvent Animation(ScriptNode node)
    {
        var (clip, idle, blend, delay, trigger) = ("", (string?)null, 0f, 0f, 0);
        foreach (var attribute in node.Attributes)
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "animation":
                    clip = attribute.Value.ToLowerInvariant();
                    break;
                case "idleanimation":
                    idle = attribute.Value.ToLowerInvariant();
                    break;
                case "blendtime":
                    blend = Number(attribute.Value);
                    break;
                case "delay":
                    delay = Number(attribute.Value);
                    break;
                case "trigger":
                    trigger = LegacyStringHash.Lower(attribute.Value);
                    break;
                default:
                    throw Invalid(attribute);
            }
        }
        return new AnimationAiEvent(trigger, delay, clip, blend, idle);
    }

    private static FaceAiEvent Face(ScriptNode node)
    {
        var (target, direction, speed, delay, trigger) = (0, Vector3.Zero, 1f, 0f, 0);
        foreach (var attribute in node.Attributes)
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "facingdirection" or "target" when Vector(attribute.Value) is { } way:
                    direction = Unit(way);
                    break;
                case "facingdirection" or "target":
                    target = LegacyStringHash.Lower(attribute.Value);
                    break;
                case "delay":
                    delay = Number(attribute.Value);
                    break;
                case "speed":
                    speed = Number(attribute.Value);
                    break;
                case "trigger":
                    trigger = LegacyStringHash.Lower(attribute.Value);
                    break;
                default:
                    throw Invalid(attribute);
            }
        }
        return new FaceAiEvent(trigger, delay, target, direction, speed);
    }

    private static KillAiEvent Kill(ScriptNode node)
    {
        var (remove, delay, trigger) = (true, 0f, 0);
        foreach (var attribute in node.Attributes)
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "remove":
                    remove = bool.Parse(attribute.Value);
                    break;
                case "delay":
                    delay = Number(attribute.Value);
                    break;
                case "trigger":
                    trigger = LegacyStringHash.Lower(attribute.Value);
                    break;
                default:
                    throw Invalid(attribute);
            }
        }
        return new KillAiEvent(trigger, delay, remove);
    }

    private static LoopAiEvent Loop(ScriptNode node)
    {
        var (reverse, delay, trigger) = (false, 0f, 0);
        foreach (var attribute in node.Attributes)
        {
            switch (attribute.Name.ToLowerInvariant())
            {
                case "type":
                    reverse = attribute.Value.ToLowerInvariant() switch
                    {
                        "loop" => false,
                        "reverse" => true,
                        _ => throw new FormatException($"no loop type \"{attribute.Value}\""),
                    };
                    break;
                case "delay":
                    delay = Number(attribute.Value);
                    break;
                case "trigger":
                    trigger = LegacyStringHash.Lower(attribute.Value);
                    break;
                default:
                    throw Invalid(attribute);
            }
        }
        return new LoopAiEvent(trigger, delay, reverse);
    }

    /// <summary>"x,y,z" as a vector; anything else is a name.</summary>
    private static Vector3? Vector(string text) =>
        text.Split(',') is [var x, var y, var z] ? new Vector3(Number(x), Number(y), Number(z)) : null;

    private static Vector3 Unit(Vector3 way) =>
        way.LengthSquared() > 1e-12f && float.IsFinite(way.LengthSquared()) ? Vector3.Normalize(way) : throw new FormatException($"the direction {way} has no length");

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static InvalidDataException Invalid(ScriptAttribute attribute) => new($"no attribute \"{attribute.Name}\" in an AI event");
}
