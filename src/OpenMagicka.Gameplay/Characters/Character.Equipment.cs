using System.Numerics;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>An item a template holds: its slot, the bone it hangs from and its rotation there.</summary>
public sealed record EquipmentTemplate(int Slot, string Bone, Vector3 Rotation, ItemTemplate Item)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Bone);
        ArgumentNullException.ThrowIfNull(Item);
        if (Slot is < 0 or >= Character.Slots)
        {
            throw new ArgumentOutOfRangeException(nameof(Slot), Slot, $"a slot is 0 to {Character.Slots - 1}");
        }
        CheckRotation(Rotation);
        Item.Validate();
    }

    internal static void CheckRotation(Vector3 rotation)
    {
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "must be finite");
        }
    }
}

/// <summary>One of a character's equipment slots; empty without an item.</summary>
/// <param name="Bone">The bone's index in the skeleton (−1 without one).</param>
/// <param name="Attach">The item's place relative to the bone's skinning transform (§4.3).</param>
public sealed record EquipmentSlot(string? BoneName, Vector3 Rotation, Item? Item, int Bone, Matrix4x4 Attach)
{
    public static EquipmentSlot Empty { get; } = new(null, Vector3.Zero, null, -1, Matrix4x4.Identity);
}

/// <summary>A character's equipment (docs/engine/ENTITIES_AI.md §8.1.1).</summary>
public sealed partial class Character
{
    public const int Slots = 8;

    /// <summary>The slot whose item resists with the template for a player's avatar.</summary>
    public const int ResistanceSlot = 1;

    /// <summary>Attacks from within this angle of the facing can be blocked.</summary>
    public const float BlockAngle = 1.0995574f;

    private readonly EquipmentSlot[] equipment = [.. Enumerable.Repeat(EquipmentSlot.Empty, Slots)];
    private bool isPlayer;

    public IReadOnlyList<EquipmentSlot> Equipment => equipment;

    /// <summary>A player's avatar: heavier characters never walk through it, and its slot-1 item resists with it.</summary>
    public bool IsPlayer
    {
        get => isPlayer;
        set
        {
            isPlayer = value;
            EquipmentChanged();
        }
    }

    /// <summary>The first slot holding a staff (spells come from it), or −1.</summary>
    public int SpellSource { get; private set; } = -1;

    /// <summary>Every equipped item's block value, added up: what a block stops.</summary>
    public int BlockValue => equipment.Sum(s => s.Item?.Template.BlockValue ?? 0);

    public Item? ItemIn(int slot) => equipment[CheckSlot(slot)].Item;

    /// <summary>The slot's item while it is in hand: a released one does nothing from its slot (§8.1.5).</summary>
    public Item? HeldItem(int slot) => ItemIn(slot) is { IsAttached: true } item ? item : null;

    /// <summary>Puts an item in a slot (replacing what was there) on the given bone.</summary>
    public void Equip(int slot, Item item, string bone, Vector3 rotation = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(bone);
        EquipmentTemplate.CheckRotation(rotation);
        var (index, attach) = AttachPoint(bone, rotation);
        if (equipment[CheckSlot(slot)].Item is { } replaced && !ReferenceEquals(replaced, item))
        {
            Guns.Stop(this, replaced);
        }
        equipment[slot] = new EquipmentSlot(bone, rotation, item, index, attach);
        item.Held();
        EquipmentChanged();
        item.Place(index >= 0 ? attach * skinned[index] : root);               // in hand at once; the others keep their sweep
    }

    /// <summary>Empties a slot; returns what it held.</summary>
    public Item? Unequip(int slot)
    {
        var item = equipment[CheckSlot(slot)].Item;
        if (item is not null)
        {
            Guns.Stop(this, item);                                            // a gun leaving the hand stops its burst
        }
        equipment[slot] = EquipmentSlot.Empty;
        EquipmentChanged();
        return item;
    }

    /// <summary>Held guns shoot when their timers come due (at the end of the frame, after the items moved).</summary>
    private void FireGuns(float dt)
    {
        foreach (var slot in equipment)
        {
            if (slot.Item is { IsAttached: true } item)
            {
                Guns.Update(this, item, dt);
            }
        }
    }

    public bool HasPassiveAbility(PassiveAbility ability) =>
        ability != PassiveAbility.None && equipment.Any(s => s.Item is { IsAttached: true } item && item.Template.PassiveAbility == ability);

    /// <summary>Whether an attack from this position comes from in front, within the block angle (none from the character's own spot).</summary>
    internal bool FacesForBlock(Vector3 attackPosition)
    {
        var towards = (attackPosition - Position) with { Y = 0 };
        var facing = Body.Facing with { Y = 0 };
        if (towards.LengthSquared() < float.Epsilon || facing.LengthSquared() < float.Epsilon)
        {
            return false;
        }
        var cos = Vector3.Dot(Vector3.Normalize(towards), Vector3.Normalize(facing));
        return MathF.Acos(Math.Clamp(cos, -1, 1)) <= BlockAngle;
    }

    /// <summary>The template's items in order: a later one in the same slot replaces an earlier one, as the original.</summary>
    private void EquipFrom(CharacterTemplate template)
    {
        foreach (var held in template.Equipment ?? [])
        {
            var (index, attach) = AttachPoint(held.Bone, held.Rotation);
            equipment[CheckSlot(held.Slot)] = new EquipmentSlot(held.Bone, held.Rotation, new Item(held.Item), index, attach);
        }
        EquipmentChanged();
    }

    /// <summary>The animation sets, the spell source and an avatar's slot-1 resistances follow the slots.</summary>
    private void EquipmentChanged()
    {
        SpellSource = Array.FindIndex(equipment, s => s.Item?.WeaponClass == WeaponClass.Staff);
        if (Animator is not null)
        {
            Animator.EquippedClasses = EquippedClasses();
        }
        Defences = Defences with { Staff = isPlayer ? equipment[ResistanceSlot].Item?.Template.Resistances : null };
    }

    /// <summary>The held weapon classes in slot order, staffs left out: they never pick a set.</summary>
    private int[] EquippedClasses() =>
        [.. equipment.Select(s => s.Item).OfType<Item>().Where(i => i.WeaponClass != WeaponClass.Staff).Select(i => (int)i.WeaponClass)];

    private static int CheckSlot(int slot) =>
        slot is >= 0 and < Slots ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, $"a slot is 0 to {Slots - 1}");
}
