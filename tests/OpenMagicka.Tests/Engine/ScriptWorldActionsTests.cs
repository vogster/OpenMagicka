using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using AttackProperties = OpenMagicka.Gameplay.Combat.AttackProperties;
using Elements = OpenMagicka.Gameplay.Spells.Elements;
using MagickType = OpenMagicka.Gameplay.Spells.MagickType;
using Factions = OpenMagicka.Gameplay.Characters.Factions;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The world's actions of OM-46 (LEVELS_TRIGGERS.md §9.4): combat, factions, statuses, teleports, magicks, items, parts and dialogs.</summary>
public class ScriptWorldActionsTests
{
    internal static readonly ItemTemplate Sword = new("sword"), Staff = new("staff", WeaponClass.Staff), Lamp = new("lamp");

    /// <summary>A wizard holding a sword and a staff.</summary>
    internal static readonly CharacterTemplate Wizard = Goblin with
    {
        Id = "wizard",
        Factions = Factions.Player0,
        Equipment = [new EquipmentTemplate(0, "hand", Numerics.Vector3.Zero, Sword), new EquipmentTemplate(1, "staffhand", Numerics.Vector3.Zero, Staff)],
    };

    internal sealed class Assets(params ItemTemplate[] items) : IScriptAssets
    {
        public ItemTemplate? Item(string asset) => items.FirstOrDefault(i => asset == "data/items/wizard/" + i.Id);
    }

    internal static ScriptScene Scene(GameWorld world, string triggers, IScriptAssets? assets = null)
    {
        var places = new ScriptPlaces(
            [new TriggerAreaContent("Gate", new Xna.Vector3(-2, 0, -2), new Xna.Vector3(4, 3, 4), Xna.Quaternion.Identity)],
            [new LocatorContent("spot", Xna.Matrix.CreateRotationY(Xna.MathHelper.PiOver2) * Xna.Matrix.CreateTranslation(5, 0, 1), 1),
             new LocatorContent("far0", Xna.Matrix.CreateTranslation(20, 0, 20), 1), new LocatorContent("far1", Xna.Matrix.CreateTranslation(-20, 0, 20), 1)]);
        world.Entities.Bestiary = new Bestiary().Add("data/characters/orc", Orc).Add("data/characters/goblin", Goblin with { Ai = Mind() });
        var xml = $"<Scene><Model>m</Model>{triggers}</Scene>";
        var scene = new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, places: places, assets: assets ?? new Assets(Sword, Staff, Lamp));
        scene.Enter();
        return scene;
    }

    internal static void Fire(ScriptScene scene, string trigger, Character? sender = null)
    {
        scene.ExecuteTrigger(ScriptScene.Hash(trigger), sender);
        scene.Update(Tick);
    }

    internal static Character Avatar(GameWorld world, Player player, float x = 0)
    {
        var avatar = new Character(Wizard, world.Physics, new Numerics.Vector3(x, 0.9f, 0)) { IsPlayer = true, Player = player };
        world.Entities.Add(avatar);
        player.Avatar = avatar;
        foreach (var held in Wizard.Equipment!)
        {
            avatar.Equip(held.Slot, new Item(held.Item), held.Bone);
        }
        return avatar;
    }

    [Fact]
    public void Kill_AndOverkill_TakeTheNamed_OrThoseInTheAreaByTypeOrFaction()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="area" autorun="false"><Then><kill area="gate" type="orc" factions="evil"/></Then></Trigger>
            <Trigger id="named" autorun="false"><Then><overkill id="chief"/></Then></Trigger>
            """);
        var goblin = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));                          // evil, by faction
        var orc = Spawn(world, Orc, at: new Numerics.Vector3(1, 0.9f, 0));                       // by type
        orc.CannotDieWithoutExplicitKill = true;
        var outside = Spawn(world, at: new Numerics.Vector3(10, 0.9f, 0));
        var chief = Spawn(world, at: new Numerics.Vector3(-10, 0.9f, 0));
        chief.UniqueId = ScriptScene.Hash("chief");

        Fire(scene, "area");
        Fire(scene, "named");

        Assert.True(goblin.IsDead && orc.IsDead);
        Assert.False(orc.CannotDieWithoutExplicitKill);
        Assert.False(outside.IsDead);
        Assert.True(chief.IsDead && chief.IsOverkilled);
    }

    [Fact]
    public void Damage_ReadsItsEntries_AndHitsTheNamed_OrThoseInTheArea()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="area" autorun="false"><Then><damage area="gate" type="any" source="spot">
              <damage attackProperty="Damage" element="physical" amount="100" magnitude="1"/>
              <damage attackproperty="damage" elements="life" amount="50" magnitude="1"/>
            </damage></Then></Trigger>
            <Trigger id="named" autorun="false"><Then><damage id="chief"><damage attackProperty="damage" element="fire" amount="30" magnitude="2"/></damage></Then></Trigger>
            """);
        var goblin = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        var chief = Spawn(world, at: new Numerics.Vector3(-10, 0.9f, 0));
        chief.UniqueId = ScriptScene.Hash("chief");
        var action = scene.Find("area")!.Variants[0].OfType<DamageAction>().Single();

        Assert.Equal([new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), new DamageEntry(AttackProperties.Damage, Elements.None, 50, 1)],
            action.Damages);                                                                     // "elements" is not "element": none, as the original
        Fire(scene, "area");
        Fire(scene, "named");

        Assert.True(goblin.HitPoints < Goblin.MaxHitPoints - 100);
        Assert.True(chief.HitPoints < Goblin.MaxHitPoints);
    }

    [Fact]
    public void Damage_WithABadEntry_FailsTheLoading()
    {
        using var world = World();
        Assert.Throws<InvalidDataException>(() => Scene(world, """
            <Trigger id="t"><Then><damage id="x"><damage element="plasma" amount="1"/></damage></Then></Trigger>
            """));
    }

    [Fact]
    public void SetFaction_AddsRemovesOrSets_OnTheNamed_TheAvatars_OrThoseInTheAreaByType()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        world.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
            [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
        var avatar = Avatar(world, session.AddPlayer(), x: 30);
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <setFaction id="player1" faction="evil" action="add"/>
              <setFaction area="gate" type="goblin" faction="friendly" action="set"/>
              <setFaction id="boss" faction="evil" action="remove"/>
            </Then></Trigger>
            """);
        var goblin = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        var orc = Spawn(world, Orc, at: new Numerics.Vector3(1, 0.9f, 0));
        var boss = Spawn(world, Goblin with { Factions = Factions.Evil | Factions.Undead }, at: new Numerics.Vector3(-10, 0.9f, 0));
        boss.UniqueId = ScriptScene.Hash("boss");

        Fire(scene, "go");

        Assert.Equal(Factions.Player0 | Factions.Evil, avatar.Factions);
        Assert.Equal(Factions.Friendly, goblin.Factions);
        Assert.Equal(Factions.Wild, orc.Factions);                                               // another type: as it was
        Assert.Equal(Factions.Undead, boss.Factions);
    }

    [Fact]
    public void RemoveAllStatusEffects_FreesEveryCharacter()
    {
        using var world = World();
        var scene = Scene(world, """<Trigger id="go" autorun="false"><Then><removeAllStatusEffects/></Then></Trigger>""");
        var goblin = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        goblin.Entangle(5);
        goblin.SetLevitate(10);
        Assert.True(goblin.IsEntangled && goblin.IsLevitating);

        Fire(scene, "go");

        Assert.False(goblin.IsEntangled || goblin.IsLevitating);
    }

    [Fact]
    public void Teleport_MovesTheNamedToTheLocatorsFloor_WithItsEffectsAndSound()
    {
        using var world = World();
        var scene = Scene(world, """<Trigger id="go" autorun="false"><Then><teleport id="leader" target="spot"/></Then></Trigger>""");
        var leader = Spawn(world, at: new Numerics.Vector3(-5, 0.9f, -5));
        leader.UniqueId = ScriptScene.Hash("leader");

        Fire(scene, "go");

        Assert.Equal(new Numerics.Vector3(5, 0.1f - leader.Body.HeightOffset, 1), leader.Position);
        Assert.True(Numerics.Vector3.Distance(-Numerics.Vector3.UnitX, leader.Body.Facing) < 1e-3f);
        Assert.Equal(2, scene.Effects.All.Count(e => e.Effect == TeleportAction.DefaultEffect));       // where it left, and where it came
        Assert.Contains(scene.Stage.TakeRequests(), r => r is PlaySoundRequest { Bank: SoundBanks.Spells, Cue: TeleportAction.DefaultSound });
    }

    [Fact]
    public void Magick_OfTheNamed_TakesEffectAtOnce_AndUnlockMagick_IsEveryonesWithItsText()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var (first, second) = (session.AddPlayer(), session.AddPlayer());
        var scene = Scene(world, """
            <Trigger id="cast" autorun="false"><Then><magick magickType="levitate" id="flyer"/></Then></Trigger>
            <Trigger id="learn" autorun="false"><Then><unlockMagick magickType="Haste"/></Then></Trigger>
            """);
        var flyer = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        flyer.UniqueId = ScriptScene.Hash("flyer");

        Fire(scene, "cast");
        Fire(scene, "learn");

        Assert.True(flyer.IsLevitating);
        Assert.True(first.HasUnlocked(MagickType.Haste) && second.HasUnlocked(MagickType.Haste));
        Assert.Contains(new MagickUnlockedRequest(MagickType.Haste), scene.Stage.TakeRequests());
    }

    [Fact]
    public void AssignItem_PutsAStaffOrAWeaponInItsSlot_AndTheDefaultsGiveTheTemplatesBack()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var (first, second) = (session.AddPlayer(), session.AddPlayer());
        var (one, two) = (Avatar(world, first), Avatar(world, second, x: 30));
        var scene = Scene(world, """
            <Trigger id="lamps" autorun="false"><Then><assignItem id="lamp" animation="pickup"/></Then></Trigger>
            <Trigger id="staff" autorun="false"><Then><assignItem id="staff" player="2"/></Then></Trigger>
            <Trigger id="back" autorun="false"><Then><assignItem id="weapon_default" area="gate"/></Then></Trigger>
            """);

        Fire(scene, "lamps");
        Assert.Equal("lamp", one.ItemIn(0)!.Template.Id);
        Assert.Equal("lamp", two.ItemIn(0)!.Template.Id);
        Assert.Equal("hand", one.Equipment[0].BoneName);
        Assert.Equal("pickup", one.NextAttackAnimation);
        Fire(scene, "staff");
        Assert.Equal("staff", two.ItemIn(1)!.Template.Id);
        Fire(scene, "back");                                                                     // only the first, in the gate
        Assert.Equal("sword", one.ItemIn(0)!.Template.Id);
        Assert.Equal("lamp", two.ItemIn(0)!.Template.Id);
    }

    [Fact]
    public void AssignItem_OfAnItemTheGameLacks_FailsTheLoading()
    {
        using var world = World();
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="t"><Then><assignItem id="excalibur"/></Then></Trigger>"""));
    }

    [Fact]
    public void AddItemToAvatars_FillsAnEmptySlot_AndRemoveItemFromAvatars_GivesTheTemplatesBack()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var avatar = Avatar(world, session.AddPlayer());
        var scene = Scene(world, """
            <Trigger id="add" autorun="false"><Then><addItemToAvatars item="lamp" bone="head"/></Then></Trigger>
            <Trigger id="swap" autorun="false"><Then><assignItem id="lamp"/></Then></Trigger>
            <Trigger id="remove" autorun="false"><Then><removeItemFromAvatars item="LAMP"/></Then></Trigger>
            """);

        Fire(scene, "add");
        Assert.Equal("lamp", avatar.ItemIn(2)!.Template.Id);
        Assert.Equal("head", avatar.Equipment[2].BoneName);
        Fire(scene, "swap");
        Fire(scene, "remove");

        Assert.Equal("sword", avatar.ItemIn(0)!.Template.Id);                                    // the template's own back
        Assert.Null(avatar.ItemIn(2));                                                           // the template has none there
    }

    [Fact]
    public void SetDialog_GivesTheNamedCharacterItsDialog()
    {
        using var world = World();
        var scene = Scene(world, """<Trigger id="go" autorun="false"><Then><setDialog id="Sage" dialog="Riddle"/></Then></Trigger>""");
        var sage = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        sage.UniqueId = ScriptScene.Hash("sage");

        Fire(scene, "go");

        Assert.Equal(ScriptScene.Hash("riddle"), sage.Dialog);
    }
}
