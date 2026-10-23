using System.Collections.Frozen;

namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// The player magicks and the queues that cast them (docs/gameplay/SPELLS.md §10): each is a
/// sequence of queue entries after merges, held in a prefix tree. Whether a magick is unlocked is
/// decided elsewhere.
/// </summary>
public static class MagickBook
{
    private const Elements E = Elements.Earth;
    private const Elements W = Elements.Water;
    private const Elements C = Elements.Cold;
    private const Elements F = Elements.Fire;
    private const Elements Z = Elements.Lightning;
    private const Elements A = Elements.Arcane;
    private const Elements L = Elements.Life;
    private const Elements S = Elements.Shield;
    private const Elements I = Elements.Ice;
    private const Elements T = Elements.Steam;

    public static IReadOnlyDictionary<MagickType, IReadOnlyList<Elements>> Sequences { get; } = new Dictionary<MagickType, IReadOnlyList<Elements>>
    {
        [MagickType.Revive] = [L, Z],
        [MagickType.Grease] = [W, E, L],
        [MagickType.Haste] = [Z, A, F],
        [MagickType.Invisibility] = [A, S, T, A],
        [MagickType.Teleport] = [Z, A, Z],
        [MagickType.Fear] = [C, A, S],
        [MagickType.Charm] = [L, S, E],
        [MagickType.ThunderB] = [T, Z, A, Z],
        [MagickType.Rain] = [W, T],
        [MagickType.Tornado] = [E, T, W, T],
        [MagickType.Blizzard] = [C, I, C],
        [MagickType.MeteorS] = [F, E, T, E, F],
        [MagickType.Conflagration] = [T, F, T, F, T],
        [MagickType.ThunderS] = [T, T, Z, A, Z],
        [MagickType.TimeWarp] = [C, S],
        [MagickType.Vortex] = [I, A, I, S, I],
        [MagickType.SUndead] = [I, E, A, C],
        [MagickType.SElemental] = [A, S, E, T, A],
        [MagickType.SDeath] = [A, C, I, C, A],
        [MagickType.SPhoenix] = [L, Z, F],
        [MagickType.Nullify] = [A, S],
        [MagickType.Corporealize] = [A, T, Z, S, A],
        [MagickType.CTD] = [Z, Z, F, L],
        [MagickType.Napalm] = [T, E, L, F, F],
        [MagickType.Portal] = [T, Z, S],
        [MagickType.TractorPull] = [E, A],
        [MagickType.ProppMagick] = [F, T, A],
        [MagickType.Levitate] = [T, A, T],
        [MagickType.ChainLightning] = [Z, Z, Z],
        [MagickType.Confuse] = [A, S, Z],
        [MagickType.Wave] = [E, T, E, T, E],
        [MagickType.PerformanceEnchantment] = [L, F, Z, F, L],
        [MagickType.JudgementSpray] = [I, I, A, S],
        [MagickType.Amalgameddon] = [A, W, Z],
    }.ToFrozenDictionary();

    private static readonly Node Root = Build();

    /// <summary>The magick whose whole path the queue spells out, or None (a magick entry never matches).</summary>
    public static MagickType Recognize(IReadOnlyList<Spell> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var node = Root;
        foreach (var entry in queue)
        {
            node = node.Child(entry.Element);
            if (node is null)
            {
                return MagickType.None;
            }
        }
        return node.Magick;
    }

    /// <summary>
    /// The magick the HUD hints at: the last one reached. Where the path breaks, the hint becomes
    /// the magick of the node reached so far and the walk restarts from the root with the next entry.
    /// </summary>
    public static MagickType Hint(IReadOnlyList<Spell> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var node = Root;
        var hint = MagickType.None;
        foreach (var entry in queue)
        {
            var child = node.Child(entry.Element);
            if (child is null)
            {
                (hint, node) = (node.Magick, Root);
            }
            else
            {
                (hint, node) = (child.Magick, child);
            }
        }
        return hint;
    }

    private static Node Build()
    {
        var root = new Node();
        foreach (var (magick, sequence) in Sequences)
        {
            sequence.Aggregate(root, (node, element) => node.ChildOrNew(element)).Magick = magick;
        }
        return root;
    }

    /// <summary>A tree node: one child slot per element index, and the magick its path casts.</summary>
    private sealed class Node
    {
        private readonly Node?[] children = new Node?[ElementIndex.Count];

        public MagickType Magick { get; set; }

        public Node? Child(Elements element)
        {
            if (element == Elements.None)
            {
                return null;
            }
            var index = ElementIndex.Of(element);
            return index < ElementIndex.Count ? children[index] : null;
        }

        public Node ChildOrNew(Elements element) => children[ElementIndex.Of(element)] ??= new Node();
    }
}
