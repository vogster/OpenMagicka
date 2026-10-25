using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Input;

/// <summary>A pad's direction, as the spell tree takes it.</summary>
public enum ControllerDirection : byte { Center, Up, Down, Left, Right }

/// <summary>
/// The elements as pairs of directions (docs/gameplay/SPELLS.md §1.2): a pad gestures them with the right
/// stick, and the keyboard's element keys give the same pairs. Each player keeps the directions given
/// so far; each new one walks the tree from its root over them: a direction with no branch drops the
/// oldest kept and the walk goes on; a leaf is the element, and the kept directions are cleared. An
/// element the tutorial forbids is not given, and clears them too. As the original.
/// </summary>
public sealed class SpellCombos
{
    /// <summary>Each basic element's pair, as the original's.</summary>
    public static IReadOnlyDictionary<Elements, (ControllerDirection First, ControllerDirection Second)> Pairs { get; } =
        new Dictionary<Elements, (ControllerDirection, ControllerDirection)>
        {
            [Elements.Earth] = (ControllerDirection.Down, ControllerDirection.Left),
            [Elements.Water] = (ControllerDirection.Left, ControllerDirection.Up),
            [Elements.Cold] = (ControllerDirection.Right, ControllerDirection.Up),
            [Elements.Fire] = (ControllerDirection.Right, ControllerDirection.Down),
            [Elements.Lightning] = (ControllerDirection.Left, ControllerDirection.Down),
            [Elements.Arcane] = (ControllerDirection.Up, ControllerDirection.Right),
            [Elements.Life] = (ControllerDirection.Up, ControllerDirection.Left),
            [Elements.Shield] = (ControllerDirection.Down, ControllerDirection.Right),
        };

    private static readonly Dictionary<(ControllerDirection, ControllerDirection), Elements> Leaves =
        Pairs.ToDictionary(p => p.Value, p => p.Key);

    private static readonly HashSet<ControllerDirection> Branches = [.. Pairs.Values.Select(p => p.First)];

    private readonly List<ControllerDirection> kept = [];

    /// <summary>The directions kept so far.</summary>
    public IReadOnlyList<ControllerDirection> Kept => kept;

    /// <summary>A direction given; the element it completes (None while none is), when the tutorial allows it.</summary>
    public Elements Give(ControllerDirection direction, Func<Elements, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        kept.Add(direction);
        for (var i = 0; i < kept.Count;)
        {
            var (found, walked) = Walk(i);
            if (found is { } element)
            {
                kept.Clear();
                return allowed(element) ? element : Elements.None;
            }
            if (walked)
            {
                break;                                                        // a branch waits for its second direction
            }
            kept.RemoveAt(0);                                                 // no branch: the oldest goes, the walk starts again
            i = 0;
        }
        return Elements.None;
    }

    public void Clear() => kept.Clear();

    /// <summary>The walk from the root over the kept directions: the leaf reached, or whether every one followed a branch.</summary>
    private (Elements? Element, bool Walked) Walk(int start)
    {
        if (kept.Count - start == 1)
        {
            return (null, Branches.Contains(kept[start]));
        }
        return Leaves.TryGetValue((kept[start], kept[start + 1]), out var element) ? (element, true) : (null, false);
    }
}

/// <summary>
/// The element keys' cooldowns (the original's element manager): a key gives its element again only
/// 0.125 s after it last did, and not while the tutorial forbids it.
/// </summary>
public sealed class ElementCooldowns
{
    public const float Cooldown = 0.125f;

    private readonly Dictionary<Elements, float> left = Enum.GetValues<Elements>().Where(e => e is > Elements.None and <= Elements.Shield
        && System.Numerics.BitOperations.IsPow2((int)e)).ToDictionary(e => e, _ => 0f);

    public bool CanCast(Elements element, Elements disabled) => (element & disabled) == 0 && left.GetValueOrDefault(element) <= 0;

    public void Start(Elements element) => left[element] = Cooldown;

    public void Update(float dt)
    {
        foreach (var element in left.Keys)
        {
            left[element] -= dt;
        }
    }

    public void Reset()
    {
        foreach (var element in left.Keys)
        {
            left[element] = 0;
        }
    }
}
