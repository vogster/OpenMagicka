using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The campaign's three save slots (docs/engine/ARCHITECTURE.md §20): an empty one starts a new campaign, a filled one
/// shows its chapter and time and goes on with it; its delete icon (or the Delete key) empties it after asking.
/// </summary>
public sealed class SlotsPage : MenuPage
{
    public static readonly Rectangle SlotArt = new(0, 0, 784, 240), DeleteArt = new(1536, 96, 64, 64);
    public static readonly int[] Centres = [192, 448, 704];

    /// <summary>The items as the original's: the delete icons first (they lie on their plates), then the slots, then back.</summary>
    public const int FirstSlot = CampaignSaves.Slots, BackItem = 2 * CampaignSaves.Slots;

    public SlotsPage(TomeMenu menu)
        : base(menu)
    {
        for (var slot = 0; slot < CampaignSaves.Slots; slot++)
        {
            var at = slot;
            Items.Add(new MenuItem(new Rectangle((int)DeleteAt(slot).X, (int)DeleteAt(slot).Y, DeleteArt.Width, DeleteArt.Height), () => Delete(at)));
        }
        for (var slot = 0; slot < CampaignSaves.Slots; slot++)
        {
            var at = slot;
            Items.Add(new MenuItem(new Rectangle(512 - 384, Centres[slot] - 128, SlotArt.Width, SlotArt.Height), () => Open(at)));
        }
        Items.Add(new MenuItem(BackPlate, Back));
        Selected = FirstSlot;
    }

    /// <summary>A slot's delete icon (top left).</summary>
    public static Vector2 DeleteAt(int slot) => new(852, 236 + 256 * slot);

    public override void Enter()
    {
        for (var slot = 0; slot < CampaignSaves.Slots; slot++)
        {
            Items[slot].Enabled = Menu.Host.Saves[slot] is not null;
        }
    }

    /// <summary>The Delete key on a filled slot asks to empty it.</summary>
    protected override void Delete()
    {
        if (Selected is >= FirstSlot and < BackItem && Menu.Host.Saves[Selected - FirstSlot] is not null)
        {
            Delete(Selected - FirstSlot);
        }
    }

    private void Open(int slot)
    {
        if (Menu.Host.Saves[slot] is null)
        {
            Menu.Host.Saves.Set(slot, new CampaignSlot(0, 0));
        }
        Menu.Push(new LobbyPage(Menu, slot));
    }

    private void Delete(int slot) => Menu.Ask(Menu.Text("#add_menu_rus_del"), [Menu.Text("#add_menu_yes"), Menu.Text("#add_menu_no")], 1, answer =>
    {
        if (answer == 0)
        {
            Menu.Host.Saves.Set(slot, null);
            Enter();
            Selected = FirstSlot + slot;
        }
    });

    public override void Draw(MenuPicture picture)
    {
        for (var slot = 0; slot < CampaignSaves.Slots; slot++)
        {
            var (x, y) = (512f, (float)Centres[slot]);
            var saved = Menu.Host.Saves[slot];
            var item = FirstSlot + slot;
            var lit = item == Selected ? 1.1f : 1;
            Art(picture, MenuAtlas.Pages, SlotArt, new Vector2(x - 384, y - 128), new Vector4(lit, lit, lit, 1), saved is null ? 0 : item == Selected ? 1.2f : 1);
            if (saved is null)
            {
                Text(picture, ItemFont, Menu.Text("#add_menu_newcamp"), new Vector2(x - 288, y - 52), ColourOf(item), TextAlignment.Left);
                continue;
            }
            Text(picture, ItemFont, Menu.Text(Chapters.NameKey(saved.Chapter)) + ":", new Vector2(x - 288, y - 52), ColourOf(item), TextAlignment.Left);
            Text(picture, ItemFont, Menu.Text(Chapters.TitleKey(saved.Chapter)), new Vector2(x - 224, y - 12), ColourOf(item), TextAlignment.Left);
            Text(picture, BoxFont, Chapters.Time(saved.Seconds), new Vector2(x - 32, y + 40), ColourOf(item), TextAlignment.Left);
            Art(picture, MenuAtlas.Pages, DeleteArt, DeleteAt(slot), slot == Selected ? new Vector4(1.3f, 1.3f, 1.3f, 1) : Vector4.One);
        }
        BackArt(picture, BackItem);
    }
}

/// <summary>
/// A slot's lobby (docs/engine/ARCHITECTURE.md §20), as far as one local player needs it: the chapter it is at (which
/// can be changed to any chapter reached), and Start.
/// </summary>
public sealed class LobbyPage : MenuPage
{
    private readonly int slot;

    public LobbyPage(TomeMenu menu, int slot)
        : base(menu)
    {
        this.slot = slot;
        Items.Add(new MenuItem(new Rectangle(64, 64, 896, 160), ChangeChapter));
        Items.Add(new MenuItem(new Rectangle(640, 860, 320, 72), () => Menu.Host.Play(slot, Saved.Chapter)));
        Items.Add(new MenuItem(BackPlate, Back));
        Selected = 1;
    }

    private CampaignSlot Saved => Menu.Host.Saves[slot] ?? new CampaignSlot(0, 0);

    private void ChangeChapter()
    {
        var saved = Saved;
        var chapters = Enumerable.Range(0, saved.Reached + 1).Select(c => $"{Menu.Text(Chapters.NameKey(c))}: {Menu.Text(Chapters.TitleKey(c))}").ToList();
        Menu.Ask("", chapters, saved.Chapter, chosen =>
        {
            if (chosen is { } chapter && chapter != saved.Chapter)
            {
                Menu.Host.Saves.Set(slot, saved with { Chapter = chapter });
            }
        });
    }

    public override void Draw(MenuPicture picture)
    {
        var saved = Saved;
        Text(picture, TitleFont, Menu.Text(Chapters.NameKey(saved.Chapter)), new Vector2(512, 80), ColourOf(0));
        Text(picture, TitleFont, Menu.Text(Chapters.TitleKey(saved.Chapter)), new Vector2(512, 150), ColourOf(0), scale: 0.8f);
        Text(picture, BoxFont, Menu.Text("#tooltip_lobby_level"), new Vector2(512, 240), Ink with { W = 0.5f });
        Text(picture, ItemFont, Menu.Text("#add_menu_start"), new Vector2(896, 880), ColourOf(1), TextAlignment.Right);
        BackArt(picture, 2);
    }
}
