using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Engine.Scripting;

/// <summary>Putting items in avatars' hands: the slot a weapon or a staff goes in, on the bone that slot uses.</summary>
internal static class ScriptItems
{
    public const int WeaponSlot = 0, StaffSlot = 1;

    /// <summary>An item of the game's wizard items, else of the NPCs'; null when there is none.</summary>
    public static ItemTemplate? Find(ScriptScene scene, string name, bool npcToo) =>
        scene.Assets?.Item("data/items/wizard/" + name) ?? (npcToo ? scene.Assets?.Item("data/items/npc/" + name) : null);

    /// <summary>A new item of the template in the slot, on the bone the slot (or the character's template) uses; false with none.</summary>
    public static bool Equip(Character character, int slot, ItemTemplate item)
    {
        var held = character.Equipment[slot];
        var given = character.Template.Equipment?.FirstOrDefault(e => e.Slot == slot);
        var (bone, rotation) = held.BoneName is { } named ? (named, held.Rotation) : given is { } own ? (own.Bone, own.Rotation) : (null, default);
        if (bone is null || !character.HasBone(bone))
        {
            return false;
        }
        character.Equip(slot, new Item(item), bone, rotation);
        return true;
    }
}

/// <summary>
/// <c>assignItem id= player= area= animation=</c>: the living avatar of the player (1–4), or the first in
/// the area, or (with neither: <c>area="all"</c>) every living one, takes the item <c>Data/Items/Wizard/&lt;id&gt;</c>:
/// a staff in the staff's slot, anything else in the weapon's; <c>staff_default</c> and
/// <c>weapon_default</c> give back the template's own. The avatar plays the clip given (not on a replay).
/// The original also gives back a profile's own choice of staff and weapon, and remembers the new one
/// for the next level; ours waits for the profiles (OM-55). An item the game lacks fails the loading.
/// </summary>
public sealed class AssignItemAction : ScriptAction
{
    public const string DefaultStaff = "staff_default", DefaultWeapon = "weapon_default";

    private ItemTemplate? item;

    public string Id { get; set; } = "";
    public int Player { get; set; }
    public string Area { get; set; } = "all";
    public string Animation { get; set; } = "";

    public override void Initialize()
    {
        if (Id is not (DefaultStaff or DefaultWeapon) && Scene.Assets is not null)
        {
            item = ScriptItems.Find(Scene, Id, npcToo: false) ?? throw new InvalidDataException($"<assignItem id=\"{Id}\">: no such item");
        }
    }

    /// <summary>A replay gives it again, with no clip (as the original).</summary>
    public override void Replay() => Assign(animate: false);

    protected override void Execute() => Assign(animate: true);

    private void Assign(bool animate)
    {
        var avatars = Area == "all" && Player == 0 ? ScriptTargets.Avatars(Scene)
            : Player is >= 1 and <= 4 ? Scene.World.Session?.Players.Where(p => p is { Playing: true, Avatar: not null } && p.Id == Player - 1).Select(p => p.Avatar!) ?? []
            : Player == 0 && Scene.Places.Area(Hash(Area)) is { } area ? area.Characters.Where(c => c.IsPlayer).Take(1)
            : [];
        foreach (var avatar in avatars.Where(a => !a.IsDead).ToList())
        {
            var (slot, given) = Id switch
            {
                DefaultStaff => (ScriptItems.StaffSlot, Own(avatar, ScriptItems.StaffSlot)),
                DefaultWeapon => (ScriptItems.WeaponSlot, Own(avatar, ScriptItems.WeaponSlot)),
                _ => (item?.WeaponClass == WeaponClass.Staff ? ScriptItems.StaffSlot : ScriptItems.WeaponSlot, item),
            };
            if (given is not null && ScriptItems.Equip(avatar, slot, given) && animate && Animation is not ("" or "none"))
            {
                avatar.PlayAttack(Animation, allowRotate: true);
            }
        }
    }

    private static ItemTemplate? Own(Character avatar, int slot) => avatar.Template.Equipment?.FirstOrDefault(e => e.Slot == slot)?.Item;
}

/// <summary>
/// <c>addItemToAvatars item= bone=</c>: each playing avatar with the bone holds the item (a wizard's, else an
/// NPC's) in its first empty slot. An item the game lacks gives none, as the original.
/// </summary>
public sealed class AddItemToAvatarsAction : ScriptAction
{
    private ItemTemplate? item;

    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Item { get; set; } = "";
    public string Bone { get; set; } = "";

    public override void Initialize() => item = Item.Length > 0 ? ScriptItems.Find(Scene, Item, npcToo: true) : null;

    protected override void Execute()
    {
        if (item is null || Bone.Length == 0)
        {
            return;
        }
        foreach (var avatar in ScriptTargets.Avatars(Scene))
        {
            var empty = Enumerable.Range(0, Character.Slots).FirstOrDefault(s => avatar.Equipment[s].Item is null, -1);
            if (empty >= 0 && avatar.HasBone(Bone))
            {
                avatar.Equip(empty, new Item(item), Bone);
            }
        }
    }
}

/// <summary>
/// <c>removeItemFromAvatars item=</c>: each slot of a playing avatar holding the item (by its name, any
/// case) takes back the template's own item for it, or empties when the template has none.
/// </summary>
public sealed class RemoveItemFromAvatarsAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Item { get; set; } = "";

    protected override void Execute()
    {
        foreach (var avatar in ScriptTargets.Avatars(Scene))
        {
            for (var slot = 0; slot < Character.Slots; slot++)
            {
                if (avatar.Equipment[slot].Item is not { } held || !string.Equals(held.Template.Id, Item, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (avatar.Template.Equipment?.FirstOrDefault(e => e.Slot == slot) is { } own)
                {
                    avatar.Equip(slot, new Item(own.Item), own.Bone, own.Rotation);
                }
                else
                {
                    avatar.Unequip(slot);
                }
            }
        }
    }
}
