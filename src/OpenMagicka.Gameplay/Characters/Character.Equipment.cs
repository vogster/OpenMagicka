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
public sealed record EquipmentSlot(string? Bone, Vector3 Rotation, Item? Item)
{
    public static EquipmentSlot Empty { get; } = new(null, Vector3.Zero, null);
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

    /// <summary>Puts an item in a slot (replacing what was there) on the given bone.</summary>
    public void Equip(int slot, Item item, string bone, Vector3 rotation = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(bone);
        EquipmentTemplate.CheckRotation(rotation);
        equipment[CheckSlot(slot)] = new EquipmentSlot(bone, rotation, item);
        EquipmentChanged();
    }

    /// <summary>Empties a slot; returns what it held.</summary>
    public Item? Unequip(int slot)
    {
        var item = equipment[CheckSlot(slot)].Item;
        equipment[slot] = EquipmentSlot.Empty;
        EquipmentChanged();
        return item;
    }

    public bool HasPassiveAbility(PassiveAbility ability) =>
        ability != PassiveAbility.None && equipment.Any(s => s.Item?.Template.PassiveAbility == ability);

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
            equipment[CheckSlot(held.Slot)] = new EquipmentSlot(held.Bone, held.Rotation, new Item(held.Item));
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
