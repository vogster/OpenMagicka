using System.Text;
using OpenMagicka.Content.Levels;
using TriggerAreaContent = OpenMagicka.Content.Magicka.TriggerAreaContent;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The trigger conditions (LEVELS_TRIGGERS.md §10).</summary>
public class ScriptConditionsTests
{
    private static ScriptPlaces Gate() =>
        new([new TriggerAreaContent("Gate", new Xna.Vector3(-2, 0, -2), new Xna.Vector3(4, 3, 4), Xna.Quaternion.Identity)], []);

    private static ScriptScene Scene(GameWorld world, string triggers = "", ScriptPlaces? places = null, ScriptGame? game = null, bool looped = false)
    {
        var xml = $"<Scene><Model>m</Model><Trigger id=\"probe\" autorun=\"false\" repeat=\"true\"><Then/></Trigger>{triggers}</Scene>";
        return new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, looped, places: places ?? Gate(), game: game);
    }

    /// <summary>Whether a condition, written as in a scene, holds there now.</summary>
    private static bool Holds(ScriptScene scene, string condition, Character? sender = null)
    {
        var node = ScriptNodeOf(condition);
        var part = ScriptRegistry.Engine().MakeCondition(node);
        part.Scene = scene;
        scene.Places.Refill(scene.World.Entities);
        return part.IsMet(sender);
    }

    private static ScriptNode ScriptNodeOf(string condition)
    {
        var xml = $"<Scene><Model>m</Model><Trigger id=\"x\"><If>{condition}</If><Then/></Trigger></Scene>";
        return SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml").Triggers[0].Conditions[0]
            .First(n => n.Type == System.Xml.XmlNodeType.Element);
    }

    private static Character Named(Character character, string name)
    {
        character.UniqueId = ScriptScene.Hash(name);
        return character;
    }

    [Fact]
    public void Present_CountsATemplateInAnArea_TheInvisibleOffOnlyWhenAsked_AndNotForAny()
    {
        using var world = World();
        Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        Spawn(world, at: new Numerics.Vector3(1, 0.9f, 1)).SetInvisible(10);
        Spawn(world, at: new Numerics.Vector3(9, 0.9f, 0));
        var scene = Scene(world);

        Assert.True(Holds(scene, """<present area="gate" type="goblin" nr="2" compareMethod="equal"/>"""));
        Assert.True(Holds(scene, """<present area="GATE" type="Goblin" nr="1" compareMethod="equal" includeInvisible="false"/>"""));
        Assert.True(Holds(scene, """<present area="gate" type="any" nr="2" compareMethod="equal" includeInvisible="false"/>"""));
        Assert.True(Holds(scene, """<present area="any" type="goblin" nr="2" compareMethod="greater"/>"""));
        Assert.False(Holds(scene, """<present area="nowhere" type="goblin" nr="9" compareMethod="equal" invert="true"/>"""));
        Assert.False(Holds(scene, """<idPresent id="x" area="nowhere" invert="true"/>"""));

        var ghost = Spawn(world, at: new Numerics.Vector3(-1, 0.9f, -1));
        ghost.Kill();
        ghost.SetInvisible(10);                                                                    // death ends it: made so after
        Assert.True(Holds(scene, """<present area="gate" type="goblin" nr="0" compareMethod="equal" includeInvisible="false"/>"""));   // the dead one taken off too, as the original
    }

    [Fact]
    public void FactionPresent_CountsEachFactionAmongThem()
    {
        using var world = World();
        Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        Spawn(world, Orc, at: new Numerics.Vector3(1, 0.9f, 1)).SetInvisible(10);
        var scene = Scene(world);

        Assert.True(Holds(scene, """<factionPresent area="gate" factions="evil,wild" nr="2" compareMethod="equal"/>"""));
        Assert.True(Holds(scene, """<factionPresent area="gate" factions="wild" nr="0" compareMethod="equal" includeInvisible="false"/>"""));
        Assert.True(Holds(scene, """<factionPresent area="gate" factions="friendly" nr="1" compareMethod="less"/>"""));
    }

    [Fact]
    public void IdPresent_WantsTheNamedOneAlive_AnywhereByDefault()
    {
        using var world = World();
        var boss = Named(Spawn(world, at: new Numerics.Vector3(9, 0.9f, 0)), "Boss");
        var scene = Scene(world);

        Assert.True(Holds(scene, """<idPresent id="boss"/>"""));
        Assert.False(Holds(scene, """<idPresent id="boss" area="gate"/>"""));
        boss.Kill();
        Assert.False(Holds(scene, """<idPresent id="boss"/>"""));
    }

    [Fact]
    public void Triggered_AndPhysicsPresent()
    {
        using var world = World();
        var scene = Scene(world);

        Assert.False(Holds(scene, """<triggered trigger="probe"/>"""));
        scene.ExecuteTrigger(ScriptScene.Hash("probe"), null);
        Assert.True(Holds(scene, """<triggered trigger="PROBE"/>"""));
        Assert.False(Holds(scene, """<triggered trigger="nothing"/>"""));
        Assert.True(Holds(scene, """<physicsPresent nr="0" compareMethod="equal"/>"""));          // no physics objects yet
    }

    [Fact]
    public void CharacterHealth_IsTheShareOfHitPoints_NoneWhenDeadOrMissing()
    {
        using var world = World();
        var pony = Named(Spawn(world), "pony1");
        var scene = Scene(world);

        Assert.True(Holds(scene, """<characterhealth id="pony1" compareMethod="equal" health="1"/>"""));
        pony.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 150, 1), null, pony.Position);
        Assert.True(Holds(scene, """<characterHealth id="pony1" compareMethod="less" health="0.6"/>"""));
        Assert.True(Holds(scene, """<characterHealth id="pony1" compareMethod="greater" health="0.4"/>"""));
        Assert.True(Holds(scene, """<characterHealth id="nobody" compareMethod="equal" health="0"/>"""));
        pony.Kill();
        Assert.True(Holds(scene, """<characterHealth id="pony1" compareMethod="equal" health="0"/>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="bad"><If><characterHealth id="p" health="2"/></If><Then/></Trigger>"""));
    }

    [Fact]
    public void InteractorIs_AndCharacterCasting()
    {
        using var world = World();
        var hero = Named(Animated(world), "hero");
        var scene = Scene(world);

        Assert.True(Holds(scene, """<interactorIs id="Hero"/>""", hero));
        Assert.False(Holds(scene, """<interactorIs id="hero"/>"""));
        Assert.False(Holds(scene, """<characterCasting id="hero" castType="force" element="arcane"/>"""));

        hero.Conjure(Elements.Arcane);
        hero.CastType = CastType.Force;
        hero.CastSpell(fromStaff: true);

        Assert.True(Holds(scene, """<characterCasting id="hero" castType="force" element="arcane"/>"""));
        Assert.False(Holds(scene, """<characterCasting id="hero" castType="area" element="arcane"/>"""));
        Assert.False(Holds(scene, """<characterCasting id="hero" castType="force" element="arcane,fire"/>"""));   // a combination, as the original
    }

    [Fact]
    public void ThePlayers_TheirRobesCountStatusesAndElements()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var first = session.AddPlayer();
        var second = session.AddPlayer();
        first.Avatar = Spawn(world, Goblin with { Id = "wizard_detective" });
        second.Avatar = Spawn(world, at: new Numerics.Vector3(3, 0.9f, 0));
        var scene = Scene(world);

        Assert.True(Holds(scene, """<robeInUse name="wizard_detective" player="1"/>"""));
        Assert.False(Holds(scene, """<robeInUse name="wizard_detective" player="2"/>"""));
        Assert.True(Holds(scene, """<robeInUse name="Wizard_Detective" player="0"/>"""));
        Assert.False(Holds(scene, """<robeInUse name="wizard_detective" player="4"/>"""));
        Assert.False(Holds(scene, """<robeInUse name="wizard_detective" player="5"/>"""));
        Assert.False(Holds(scene, """<robeInUse player="0"/>"""));
        Assert.True(Holds(scene, """<playerCount compareMethod="equal" nr="2"/>"""));
        second.Playing = false;
        Assert.True(Holds(scene, """<playerCount compareMethod="equal" nr="1"/>"""));

        Assert.False(Holds(scene, """<playersHaveStatusEffect statusEffect="burning"/>"""));
        first.Avatar.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, first.Avatar.Position);
        Assert.True(first.Avatar.Statuses.Has(StatusEffects.Wet));
        Assert.True(Holds(scene, """<playersHaveStatusEffect statusEffect="burning"/>"""));      // any status, as the original

        Assert.False(Holds(scene, """<playerHasElement element="life"/>"""));
        first.Avatar.Conjure(Elements.Life);
        Assert.True(Holds(scene, """<playerHasElement element="life"/>"""));
    }

    private sealed class Boss(bool dead, float health) : IScriptBoss
    {
        public bool Dead => dead;

        public float NormalizedHealth => health;
    }

    private sealed class Dialogs : IScriptDialogs
    {
        public bool MessageBoxActive => true;

        public bool IsActive(int dialog) => dialog == ScriptScene.Hash("intro");

        public bool IsDone(int dialog, int interactIndex) => dialog == ScriptScene.Hash("help") && interactIndex < 1;
    }

    [Fact]
    public void TheBoss_WithNoneUnderWay_NeverHolds_EvenInverted()
    {
        using var world = World();
        var game = new ScriptGame();
        var scene = Scene(world, game: game);

        Assert.False(Holds(scene, """<bossDead invert="true"/>"""));
        Assert.False(Holds(scene, """<bosshealth compareMethod="greater" value="0" invert="true"/>"""));
        game.Boss = new Boss(dead: false, health: 0.25f);
        Assert.True(Holds(scene, """<bossDead invert="true"/>"""));
        Assert.True(Holds(scene, """<bossHealth compareMethod="less" value="0.5"/>"""));
        Assert.True(Holds(scene, """<bossHealth comparemethod="equal" value="0.25"/>"""));
        Assert.False(Holds(scene, """<bossDead/>"""));
        game.Boss = new Boss(dead: true, health: 0);
        Assert.True(Holds(scene, """<bossDead/>"""));
    }

    [Fact]
    public void TheGameState_DialogsCutscenesProfileAndPacks()
    {
        var session = new GameSession(GameType.Versus);
        using var world = new GameWorld(session: session);
        var game = new ScriptGame();
        var scene = Scene(world, game: game);

        Assert.False(Holds(scene, """<dialogDone dialog="help"/>"""));
        Assert.False(Holds(scene, """<dialogActive dialog="any"/>"""));
        game.Dialogs = new Dialogs();
        Assert.True(Holds(scene, """<dialogDone dialog="Help"/>"""));
        Assert.False(Holds(scene, """<dialogDone dialog="help" interactIndex="1"/>"""));
        Assert.True(Holds(scene, """<dialogActive/>"""));
        Assert.True(Holds(scene, """<dialogActive dialog="intro"/>"""));
        Assert.False(Holds(scene, """<dialogActive dialog="outro"/>"""));

        Assert.True(Holds(scene, """<noCutsceneActive/>"""));
        game.InCutscene = true;
        Assert.True(Holds(scene, """<NoCutsceneActive invert="true"/>"""));

        Assert.False(Holds(scene, """<hasAchievement name="dng1win"/>"""));
        game.Achievements.Add("DNG1WIN");
        Assert.True(Holds(scene, """<hasAchievement name="dng1win"/>"""));
        Assert.True(Holds(scene, """<anyPlayerHasDied invert="true"/>"""));
        Assert.False(Holds(scene, """<HasDLC type="WizardHat"/>"""));
        game.InstalledDlc.Add(Dlc.WizardHat);
        Assert.True(Holds(scene, """<HasDLC type="wizardhat"/>"""));
        Assert.True(Holds(scene, """<dialogHintDone/>"""));                                  // none up
        game.Tutorial.Show(new TutorialDialogHint(Elements.Life, MagickType.None, "#hint", 0, null, null));
        Assert.False(Holds(scene, """<dialogHintDone/>"""));

        Assert.False(Holds(Scene(world, looped: true), """<isCampaignLooped/>"""));            // not a campaign: the original crashes
    }

    [Fact]
    public void IsCampaignLooped_IsTheScenesLoop_InACampaign()
    {
        using var campaign = new GameWorld(session: new GameSession(GameType.Campaign));
        using var bare = World();

        Assert.True(Holds(Scene(campaign, looped: true), """<isCampaignLooped/>"""));
        Assert.False(Holds(Scene(campaign), """<isCampaignLooped/>"""));
        Assert.False(Holds(Scene(bare, looped: true), """<isCampaignLooped/>"""));
    }

    [Fact]
    public void ABadEnumValue_FailsTheLoading()
    {
        using var world = World();

        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="bad"><If><characterCasting id="p" castType="nonsense"/></If><Then/></Trigger>"""));
    }
}
