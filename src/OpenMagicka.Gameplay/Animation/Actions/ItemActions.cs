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
        if (owner.HeldItem(Slot) is not { } item)
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

/// <summary>The gun in a slot fires a burst for the window (docs/engine/ENTITIES_AI.md §8.1.3).</summary>
public sealed class Gunfire(float start, float end, int slot, float accuracy) : AnimationAction(start, end)
{
    public int Slot { get; } = slot is >= 0 and < Character.Slots ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a slot");

    public float Accuracy { get; } = Finite(accuracy, nameof(accuracy));

    public override void Run(Character owner, bool first)
    {
        if (first && owner.HeldItem(Slot) is { } item)
        {
            Guns.Begin(owner, item, Accuracy);
        }
    }

    public override void Stop(Character owner)
    {
        if (owner.ItemIn(Slot) is { } item)
        {
            Guns.End(owner, item);
        }
    }
}

/// <summary>The item in a slot throws its missile (docs/engine/ENTITIES_AI.md §8.1.4).</summary>
/// <param name="velocity">In the holder's frame, or the item's when <paramref name="itemAligned"/>; null to throw ahead.</param>
public sealed class SpawnMissile(float start, float end, int slot, System.Numerics.Vector3? velocity, bool itemAligned) : AnimationAction(start, end)
{
    public int Slot { get; } = slot is >= 0 and < Character.Slots ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a slot");

    public System.Numerics.Vector3? Velocity { get; } = velocity is { } v ? Finite(v, nameof(velocity)) : null;

    public bool ItemAligned { get; } = itemAligned;

    public override void Run(Character owner, bool first)
    {
        if (first && owner.HeldItem(Slot) is { } item)
        {
            Ranged.Launch(owner, item, Velocity, ItemAligned);
        }
    }
}

/// <summary>The item in a slot is let go, with a velocity in the holder's frame (docs/engine/ENTITIES_AI.md §8.1.5).</summary>
public sealed class DetachItem(float start, float end, int slot, System.Numerics.Vector3 velocity) : AnimationAction(start, end)
{
    public int Slot { get; } = slot is >= 0 and < Character.Slots ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a slot");

    public System.Numerics.Vector3 Velocity { get; } = Finite(velocity, nameof(velocity));

    public override void Run(Character owner, bool first)
    {
        if (first)
        {
            owner.Release(Slot, Ranged.Turn(Velocity, owner.RootMatrix()));
        }
    }
}
