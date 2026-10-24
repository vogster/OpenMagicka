using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Animation.Actions;

/// <summary>The item in a slot strikes for the window (docs/engine/ENTITIES_AI.md §8.1.2).</summary>
public sealed class DealDamage(float start, float end, int slot, DamageTargets targets) : AnimationAction(start, end)
{
    public int Slot { get; } = slot is >= 0 and < Character.Slots ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a slot");

    public DamageTargets Targets { get; } = targets;

    public override void Run(Character owner, bool first)
    {
        if (owner.ItemIn(Slot) is not { } item)
        {
            return;
        }
        if (first)
        {
            Melee.Begin(owner, item);
        }
        Melee.Strike(owner, item, Targets);
    }

    public override void Stop(Character owner)
    {
        if (owner.ItemIn(Slot) is { } item)
        {
            Melee.End(owner, item);
        }
    }
}
