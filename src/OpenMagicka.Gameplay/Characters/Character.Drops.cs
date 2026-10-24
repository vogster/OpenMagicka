using System.Numerics;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>Items leaving a character's hands and coming into them (docs/engine/ENTITIES_AI.md §8.1.5).</summary>
public sealed partial class Character
{
    /// <summary>How far an avatar looks for an item to pick up.</summary>
    public const float PickUpReach = 2.5f;

    /// <summary>
    /// The slot lets go of its item where it is now: a pickable item leaves a still copy in the world, any
    /// other itself, with <paramref name="velocity"/> (as the original, the copy never gets it); the slot keeps
    /// its item, detached. Null when there is nothing in hand, no model, or no room.
    /// </summary>
    public WorldItem? Release(int slot, Vector3 velocity = default)
    {
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "must be finite");
        }
        if (ItemIn(slot) is not { IsAttached: true, Template.Bounds: not null } item || !CanDrop())
        {
            return null;
        }
        var pickable = item.Template.Pickable;
        var dropped = Drop(item, pickable ? new Item(item.Template) { SpecialCooldown = item.SpecialCooldown } : item, pickable ? Vector3.Zero : velocity);
        EquipmentChanged();
        return dropped;
    }

    /// <summary>Every filled slot lets go (the dead state's entry, unless undying).</summary>
    internal void ReleaseAll()
    {
        for (var slot = 0; slot < Slots; slot++)
        {
            Release(slot);
        }
    }

    /// <summary>
    /// The first pickable item lying within reach that is within the character's own radius (flat) or,
    /// looking <paramref name="ahead"/>, in front; not looking ahead, simply the first. One pass, as the original.
    /// </summary>
    public WorldItem? FindPickUp(bool ahead)
    {
        if (Manager is not { } manager)
        {
            return null;
        }
        foreach (var entity in manager.Query(Position, PickUpReach))
        {
            if (entity is not WorldItem { IsPickable: true, IsDead: false } item)
            {
                continue;
            }
            var flat = (item.Position - Position) with { Y = 0 };
            var distance = flat.Length();
            if (distance <= Radius || !ahead || (distance > 0 && Vector3.Dot(Body.Facing, flat / distance) > 0))
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// Takes up a lying item: a staff to slot 1, anything else to slot 0, where the held item trades places
    /// with it (one already released stays where it lies); a bound held item, or a slot with no bone, refuses.
    /// The character then plays its pick-up.
    /// </summary>
    public bool PickUp(WorldItem lying)
    {
        ArgumentNullException.ThrowIfNull(lying);
        var slot = lying.Item.WeaponClass == WeaponClass.Staff ? ResistanceSlot : 0;
        var held = equipment[slot];
        if (!lying.IsPickable || lying.IsDead || !ReferenceEquals(lying.Manager, Manager) || held.Item?.Template.Bound == true || held.BoneName is null)
        {
            return false;
        }
        if (held.Item is { IsAttached: true, Template.Bounds: not null } old && CanDrop())
        {
            Drop(old, old, Vector3.Zero);                                     // with no room, the old one is simply gone
        }
        lying.Take();
        RaiseScriptTrigger(lying.OnPickupTrigger);
        Equip(slot, lying.Item, held.BoneName, held.Rotation);
        PlayAttack(slot == ResistanceSlot ? "pickup_staff" : "pickup_weapon", allowRotate: false);
        return true;
    }

    private bool CanDrop() => Manager is { } manager && !world.IsDisposed && !world.IsStepping && manager.Entities.Count < EntityManager.Capacity;

    /// <summary>The held item stops and detaches; <paramref name="lying"/> (it, or its copy) lies where it was.</summary>
    private WorldItem Drop(Item held, Item lying, Vector3 velocity)
    {
        var dropped = new WorldItem(world, lying, held.Transform, velocity, Position);
        try
        {
            Manager!.Add(dropped);
        }
        catch
        {
            world.Remove(dropped.Body);
            throw;
        }
        Guns.Stop(this, held);
        held.Detach();
        RaiseCue(new Animation.ItemEffectEndCue(held));
        RaiseCue(new Animation.ItemEffectEndCue(held.PassiveSource));
        return dropped;
    }
}
