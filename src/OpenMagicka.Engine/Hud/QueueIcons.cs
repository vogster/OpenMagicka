using System.Numerics;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Hud;

/// <summary>An element icon of the queue under a wizard: its element, its place from the wizard's feet (pixels), its scale and colour.</summary>
public readonly record struct QueueIcon(Elements Element, Vector2 Offset, Vector2 Scale, Vector4 Colour);

/// <summary>
/// The element queue under a wizard, as the original HUD animates it (docs/engine/ARCHITECTURE.md §19). A new element
/// pops in at its slot, grows past full size glowing white, then settles; when one leaves the rest slide left. A
/// merging element flies onto its partner, which turns into the result while the flier glows and fades; cancelling
/// elements fly together and shrink away. A cleared queue shrinks and fades. Everything eases towards its target by
/// 1 − 0.0025^dt each step. Every entry has an icon, one of no single element (a magick's) an undrawn one, so the
/// changes' positions keep matching. Logic thread.
/// </summary>
public sealed class QueueIcons
{
    public const int Slots = 5;
    public const float Ease = 0.0025f, GrowScale = 1.5f, Close = 0.1f, ClosePlace = 0.75f;

    /// <summary>The slots' places under the feet.</summary>
    public static readonly Vector2[] SlotOffsets = [new(-50, 44), new(-25, 44), new(0, 44), new(25, 44), new(50, 44)];

    public static readonly Vector4 Glow = new(6, 6, 6, 1), Normal = new(1, 1, 1, 1), Clear = new(1, 1, 1, 0);

    [Flags]
    private enum Motion : byte { None = 0, Grow = 1, Glow = 2, Shrink = 4, Fade = 8 }

    private sealed class Icon(Elements element, Vector2 offset)
    {
        public Elements Element { get; set; } = element;
        public Vector2 Offset { get; set; } = offset;
        public Vector2 Scale { get; set; }
        public Vector4 Colour { get; set; } = Clear;
        public Motion Motion { get; set; } = Motion.Grow | Motion.Glow;
        public bool Removed { get; set; }
        public bool NoSlot { get; set; }
        public Icon? Target { get; set; }
        public Elements? Result { get; set; }
        public bool Arrived { get; set; }
        public bool Cleared { get; set; }
    }

    private readonly List<Icon> icons = [];
    private readonly List<Icon> fliers = [];
    private readonly List<Elements> logical = [];

    /// <summary>The queue as the changes have made it (what it should show once the animation settles).</summary>
    public IReadOnlyList<Elements> Elements => logical;

    public bool IsEmpty => icons.Count == 0 && fliers.Count == 0;

    /// <summary>How many icons are in the queue (not leaving): a magick's recipe hint shows from there on.</summary>
    public int Staying
    {
        get
        {
            var staying = 0;
            foreach (var icon in icons)
            {
                staying += icon.Removed ? 0 : 1;
            }
            return staying;
        }
    }

    /// <summary>A conjure's changes: an element a full queue refused (it joins nothing) only pops.</summary>
    public void ApplyAll(IReadOnlyList<QueueChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes is [QueueAdded refused] && Staying >= Slots)
        {
            if (icons.Count <= Slots)
            {
                icons.Add(new Icon(refused.Element, Vector2.Zero) { NoSlot = true, Removed = true });   // it pops, and holds no place
            }
            return;
        }
        foreach (var change in changes)
        {
            Apply(change);
        }
    }

    public void Apply(QueueChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        switch (change)
        {
            case QueueAdded added:
                logical.Add(added.Element);
                var staying = Staying;
                if (staying > Slots)
                {
                    break;
                }
                icons.Add(new Icon(added.Element, staying < Slots ? SlotOffsets[staying] : Vector2.Zero) { NoSlot = staying >= Slots });
                break;
            case QueueMerged merged:
                Fly(merged.Source, merged.Target, merged.Result);
                Resolve(merged.Source, merged.Target, merged.Result);
                break;
            case QueueCancelled cancelled:
                Fly(cancelled.Source, cancelled.Target, null);
                Resolve(cancelled.Source, cancelled.Target, null);
                break;
        }
    }

    /// <summary>Every icon shrinks and fades away (the queue was cast, given away or emptied).</summary>
    public void ClearAll()
    {
        logical.Clear();
        foreach (var icon in icons.Concat(fliers))
        {
            (icon.Motion, icon.Removed, icon.Cleared) = (Motion.Shrink | Motion.Fade, true, true);
        }
    }

    public void Update(float dt)
    {
        var ease = 1 - MathF.Pow(Ease, Math.Max(dt, 0));
        var slot = 0;
        for (var index = 0; index < icons.Count; index++)
        {
            var icon = icons[index];
            StepColour(icon, ease);
            if (icon.Motion.HasFlag(Motion.Grow))
            {
                Grow(icon, ease);
            }
            else if (icon.Motion.HasFlag(Motion.Shrink))
            {
                icon.Scale = Toward(icon.Scale, Vector2.Zero, ease);
                if (icon.Scale.LengthSquared() < Close)
                {
                    icons.RemoveAt(index--);
                    fliers.RemoveAll(f => ReferenceEquals(f.Target, icon));
                    continue;
                }
            }
            else if (icon.NoSlot)
            {
                (icon.Motion, icon.Removed) = (Motion.Shrink | Motion.Fade, true);         // no room: it pops and goes
            }
            else if (!icon.Removed)
            {
                icon.Offset = Toward(icon.Offset, SlotOffsets[Math.Min(slot, Slots - 1)], ease);
                icon.Scale = Toward(icon.Scale, Vector2.One, ease);
            }
            slot += icon.Removed ? 0 : 1;
        }
        for (var index = fliers.Count - 1; index >= 0; index--)
        {
            StepFlier(fliers[index], ease);
        }
    }

    /// <summary>The icons to draw, the fliers over the queue.</summary>
    public void Write(List<QueueIcon> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var icon in icons.Concat(fliers))
        {
            into.Add(new QueueIcon(icon.Element, icon.Offset, icon.Scale, icon.Colour));
        }
    }

    /// <summary>The icon at a queue position: the position-th of those staying (the joining one past the end).</summary>
    private Icon? At(int position)
    {
        var seen = 0;
        foreach (var icon in icons)
        {
            if (!icon.Removed && seen++ == position)
            {
                return icon;
            }
        }
        return null;
    }

    private void Fly(int source, int target, Elements? result)
    {
        if (At(source) is not { } flier || At(target) is not { } partner)
        {
            if (At(source) is { } lost)
            {
                (lost.Motion, lost.Removed) = (Motion.Shrink | Motion.Fade, true);   // nowhere to go: it leaves
            }
            return;
        }
        icons.Remove(flier);
        (flier.Target, flier.Result, flier.Removed) = (partner, result, true);
        fliers.Add(flier);
        if (result is null)
        {
            partner.Removed = true;                                           // the others start sliding at once
        }
    }

    private void Resolve(int source, int target, Elements? result)
    {
        if (source < logical.Count && target < logical.Count)
        {
            if (result is { } merged)
            {
                logical[target] = merged;
                logical.RemoveAt(source);
            }
            else
            {
                logical.RemoveAt(Math.Max(source, target));
                logical.RemoveAt(Math.Min(source, target));
            }
        }
        else if (target < logical.Count)
        {
            if (result is { } merged)
            {
                logical[target] = merged;                                     // the joining one (past a full queue) merged in
            }
            else
            {
                logical.RemoveAt(target);
            }
        }
    }

    private void StepFlier(Icon flier, float ease)
    {
        StepColour(flier, ease);
        if (flier.Cleared)
        {
            flier.Scale = Toward(flier.Scale, Vector2.Zero, ease);              // the queue went while it flew: it goes too
            if (flier.Scale.LengthSquared() < Close)
            {
                fliers.Remove(flier);
            }
            return;
        }
        if (flier.Motion.HasFlag(Motion.Grow))
        {
            Grow(flier, ease);
            return;
        }
        if (!flier.Arrived && flier.Target is { } target)
        {
            flier.Offset = Toward(flier.Offset, target.Offset, ease);
            flier.Scale = Toward(flier.Scale, Vector2.One, ease);
            if (Vector2.DistanceSquared(flier.Offset, target.Offset) < ClosePlace)
            {
                flier.Arrived = true;
                if (flier.Result is { } result)
                {
                    flier.Motion = Motion.Glow;
                    target.Element = result;                                  // the partner becomes what they make
                }
                else
                {
                    flier.Motion = Motion.Glow | Motion.Shrink;
                    target.Motion = Motion.Shrink | Motion.Fade;
                }
            }
            return;
        }
        if (flier.Motion.HasFlag(Motion.Shrink))
        {
            flier.Scale = Toward(flier.Scale, Vector2.Zero, ease);
            if (flier.Scale.LengthSquared() < Close)
            {
                fliers.Remove(flier);
            }
        }
        else if (flier.Motion.HasFlag(Motion.Glow) && Vector4.DistanceSquared(flier.Colour, Glow) < Close)
        {
            flier.Motion = Motion.Fade;
        }
        else if (flier.Motion.HasFlag(Motion.Fade) && flier.Colour.W < 0.05f)
        {
            fliers.Remove(flier);
        }
    }

    private static void StepColour(Icon icon, float ease) =>
        icon.Colour = Toward(icon.Colour, icon.Motion.HasFlag(Motion.Glow) ? Glow : icon.Motion.HasFlag(Motion.Fade) ? Clear : Normal, ease);

    private static void Grow(Icon icon, float ease)
    {
        icon.Scale = Toward(icon.Scale, new Vector2(GrowScale), ease);
        if (Vector2.DistanceSquared(icon.Scale, new Vector2(GrowScale)) < Close)
        {
            icon.Motion &= ~(Motion.Grow | Motion.Glow);
        }
    }

    private static Vector2 Toward(Vector2 from, Vector2 to, float ease) => from + (to - from) * ease;

    private static Vector4 Toward(Vector4 from, Vector4 to, float ease) => from + (to - from) * ease;
}
