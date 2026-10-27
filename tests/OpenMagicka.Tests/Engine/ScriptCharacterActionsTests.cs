using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using LocatorContent = OpenMagicka.Content.Magicka.LocatorContent;
using Numerics = System.Numerics;
using TriggerAreaContent = OpenMagicka.Content.Magicka.TriggerAreaContent;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The characters' actions (LEVELS_TRIGGERS.md §9): spawn, giveOrder, avatarMove, setPosition, remove; and AI events read from scripts.</summary>
public class ScriptCharacterActionsTests
{
    private static ScriptPlaces Places() => new(
        [new TriggerAreaContent("Gate", new Xna.Vector3(-2, 0, -2), new Xna.Vector3(4, 3, 4), Xna.Quaternion.Identity)],
        [new LocatorContent("spot", Xna.Matrix.CreateRotationY(Xna.MathHelper.PiOver2) * Xna.Matrix.CreateTranslation(5, 0, 1), 1),
         new LocatorContent("far", Xna.Matrix.CreateTranslation(20, 0, 20), 1)]);

    private static ScriptScene Scene(GameWorld world, string triggers)
    {
        world.Entities.Bestiary = new Bestiary().Add("data/characters/orc", Orc).Add("data/characters/goblin", Goblin with { Ai = Mind() });
        var xml = $"<Scene><Model>m</Model>{triggers}</Scene>";
        var scene = new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, places: Places());
        scene.Enter();
        return scene;
    }

    private static GameWorld Floored(GameSession session)
    {
        var world = new GameWorld(session: session);
        world.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
            [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
        return world;
    }

    private static void Fire(ScriptScene scene, string trigger, Character? sender = null)
    {
        scene.ExecuteTrigger(ScriptScene.Hash(trigger), sender);
        scene.Update(Tick);
    }

    private static void Play(GameWorld world, ScriptScene scene, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            world.Tick(Tick);
            scene.Update(Tick);
        }
    }

    [Fact]
    public void Spawn_MakesTheCharacters_AtTheLocator_OnTheFloor_WithTheirScript()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <spawn type="orc" nr="2" area="spot" id="Grunt" health="0.5" onDeath="mourn" order="idle" dialog="hello"/>
            </Then></Trigger>
            <Trigger id="mourn" autorun="false" repeat="true"><Then><counter name="dead" action="inc" value="1"/></Then></Trigger>
            """);

        Fire(scene, "go");

        var orcs = world.Characters.ToArray();
        Assert.Equal(2, orcs.Length);
        var grunt = Assert.IsType<Character>(world.Entities.FindByUniqueId(ScriptScene.Hash("grunt")));
        Assert.Equal(5, grunt.Position.X, 3);
        Assert.Equal(0.1f + Orc.Length / 2 + Orc.Radius, grunt.Position.Y, 2);                 // on the floor, 0.1 over it
        Assert.Equal(grunt.MaxHitPoints / 2, grunt.HitPoints);
        Assert.Equal(Order.Idle, grunt.Mind!.Order);
        Assert.Equal(ScriptScene.Hash("hello"), grunt.Dialog);
        Assert.True(Numerics.Vector3.Distance(-Numerics.Vector3.UnitX, grunt.Body.Facing) < 1e-3f);   // the locator's way (turned a quarter)

        grunt.Kill();
        scene.Update(Tick);
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("dead")));
    }

    [Fact]
    public void Spawn_InAnArea_TakesRandomPointsOfIt_AndReadsTheMindsEvents()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <spawn type="goblin" nr="3" area="gate" order="idle">
                <move position="far" speed="0.5" trigger="there"/>
                <animation animation="Wave" delay="1"/>
              </spawn>
            </Then></Trigger>
            """);

        Fire(scene, "go");

        var goblins = world.Characters.ToArray();
        Assert.Equal(3, goblins.Length);
        Assert.All(goblins, g => Assert.InRange(g.Position.X, -2, 2));
        var events = goblins[0].Mind!.Events!;
        var move = Assert.IsType<MoveAiEvent>(events[0]);
        Assert.Equal(new Numerics.Vector3(20, 0, 20), move.Waypoint);
        Assert.Equal(0.5f, move.Speed);
        Assert.Equal(ScriptScene.Hash("there"), move.Trigger);
        Assert.Equal("wave", Assert.IsType<AnimationAiEvent>(events[1]).Animation);
    }

    [Fact]
    public void Spawn_OfAnUnknownTemplate_OrOnAClient_MakesNone()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="dragon" autorun="false"><Then><spawn type="dragon" area="spot"/></Then></Trigger>
            <Trigger id="orc" autorun="false"><Then><spawn type="orc" area="spot"/></Then></Trigger>
            """);

        Fire(scene, "dragon");
        Assert.Empty(world.Characters);
        scene.IsHost = false;
        Fire(scene, "orc");                                                                       // a client fires nothing (the host spawns)
        Assert.Empty(world.Characters);
    }

    [Fact]
    public void TheCharactersTriggers_HurtAndDeath_RunWhileEntered_AndRemoveSetsOffNone()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <spawn type="orc" area="spot" id="a" onDamage="hurt" onDeath="mourn"/><spawn type="orc" area="far" id="b" onDeath="mourn"/>
            </Then></Trigger>
            <Trigger id="hurt" autorun="false" repeat="true"><Then><counter name="hurt" action="inc" value="1"/></Then></Trigger>
            <Trigger id="mourn" autorun="false" repeat="true"><Then><counter name="dead" action="inc" value="1"/></Then></Trigger>
            <Trigger id="clear" autorun="false"><Then><remove id="b"/></Then></Trigger>
            """);
        Fire(scene, "go");
        var a = (Character)world.Entities.FindByUniqueId(ScriptScene.Hash("a"))!;

        a.Damage(new OpenMagicka.Gameplay.Combat.DamageEntry(OpenMagicka.Gameplay.Combat.AttackProperties.Damage,
            OpenMagicka.Gameplay.Spells.Elements.Earth, 10, 1), null, a.Position);
        Fire(scene, "clear");
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("hurt")));
        Assert.Equal(0, scene.Level.Counter(ScriptScene.Hash("dead")));                          // removal: no death trigger, as the original

        scene.Leave();
        a.Kill();
        scene.Update(Tick);
        Assert.Equal(0, scene.Level.Counter(ScriptScene.Hash("dead")));                          // left: the scene hears no more
    }

    [Fact]
    public void GiveOrder_ByName_OrInAnArea_ByTypeOrFaction()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="named" autorun="false"><Then><giveOrder id="boss" order="wander" speed="0.3"/></Then></Trigger>
            <Trigger id="wild" autorun="false"><Then><giveOrder area="gate" factions="wild" order="defend" targetArea="gate"/></Then></Trigger>
            <Trigger id="nobody" autorun="false"><Then><giveOrder area="gate" order="flee"/></Then></Trigger>
            """);
        var boss = Spawn(world, Orc, at: new Numerics.Vector3(9, 0.9f, 0));
        boss.UniqueId = ScriptScene.Hash("Boss");
        var inside = Spawn(world, Orc, at: new Numerics.Vector3(0, 0.9f, 0));
        var goblin = Spawn(world, Goblin with { Ai = Mind() }, at: new Numerics.Vector3(1, 0.9f, 0));

        Fire(scene, "named");
        Fire(scene, "wild");
        Fire(scene, "nobody");                                                                     // neither type nor faction: none, as the original

        Assert.Equal(Order.Wander, boss.Mind!.Order);
        Assert.Equal(0.3f, boss.Mind.WanderSpeed);
        Assert.Equal(Order.Defend, inside.Mind!.Order);
        Assert.Equal(ScriptScene.Hash("gate"), inside.Mind.TargetArea);
        Assert.NotEqual(Order.Defend, goblin.Mind!.Order);
        Assert.NotEqual(Order.Flee, inside.Mind.Order);
    }

    [Fact]
    public void SetPosition_PutsTheNamedOrAPlayer_OnTheFloorAtTheLocator()
    {
        var session = new GameSession();
        using var world = Floored(session);
        var player = session.AddPlayer();
        player.Avatar = Spawn(world, at: new Numerics.Vector3(-5, 0.9f, -5));
        var npc = Spawn(world, Orc, at: new Numerics.Vector3(0, 0.9f, 0));
        npc.UniqueId = ScriptScene.Hash("guard");
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then>
              <setPosition id="player1" area="spot" facingDirection="true"/><setPosition id="guard" area="far"/>
            </Then></Trigger>
            """);

        Fire(scene, "go");

        Assert.Equal(new Numerics.Vector3(5, -player.Avatar.Body.HeightOffset, 1), player.Avatar.Position);
        Assert.True(Numerics.Vector3.Distance(-Numerics.Vector3.UnitX, player.Avatar.Body.Facing) < 1e-3f);
        Assert.Equal(20, npc.Position.X, 3);
    }

    [Fact]
    public void Remove_TakesTheNamed_OrThoseInTheArea_AndKillWantersOnlyWithForce()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="area" autorun="false"><Then><remove area="gate" type="goblin"/></Then></Trigger>
            <Trigger id="named" autorun="false"><Then><remove id="chief" force="true"/></Then></Trigger>
            """);
        var goblin = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        var orc = Spawn(world, Orc, at: new Numerics.Vector3(1, 0.9f, 0));
        var chief = Spawn(world, Orc, at: new Numerics.Vector3(9, 0.9f, 0));
        (chief.UniqueId, chief.CannotDieWithoutExplicitKill) = (ScriptScene.Hash("chief"), true);

        Fire(scene, "area");
        Fire(scene, "named");
        world.Tick(Tick);

        Assert.True(goblin.IsDead);
        Assert.False(orc.IsDead);
        Assert.True(chief.IsDead);
        Assert.False(chief.CannotDieWithoutExplicitKill);
        Assert.DoesNotContain(goblin, world.Characters);
    }

    [Fact]
    public void AvatarMove_GivesEachPlayerItsEvents_TheTakenSlotGoingToTheNext()
    {
        var session = new GameSession();
        using var world = Floored(session);
        var first = session.AddPlayer();
        var second = session.AddPlayer();
        first.Avatar = Spawn(world, at: new Numerics.Vector3(0, 0.9f, 0));
        second.Avatar = Spawn(world, at: new Numerics.Vector3(3, 0.9f, 0));
        var scene = Scene(world, """
            <Trigger id="go" autorun="false"><Then><avatarMove>
              <Player1><move position="1,0,0"/></Player1>
              <Player1><face facingDirection="0,0,1"/></Player1>
            </avatarMove></Then></Trigger>
            """);

        Fire(scene, "go");

        Assert.True(first.Avatar.IsInEvent);
        Assert.True(second.Avatar.IsInEvent);
        Play(world, scene, 2);
        Assert.False(first.Avatar.IsInEvent);
        Assert.Equal(1, first.Avatar.Position.X, 0);
    }

    [Fact]
    public void AnUnknownAiEvent_OrAttribute_FailsTheLoading()
    {
        using var world = World();

        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="a"><Then><spawn type="orc"><dance/></spawn></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="a"><Then><spawn type="orc"><move speed="fast"/></spawn></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="a"><Then><spawn type="orc"><kill when="now"/></spawn></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="a"><Then><avatarMove><Wizard/></avatarMove></Then></Trigger>"""));
    }

    [Fact]
    public void ReleaseGripAndGiveOrder_FreeTheNpcsGrips_BothWays()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="free" autorun="false"><Then><releaseGrip id="boss"/></Then></Trigger>
            <Trigger id="order" autorun="false"><Then><giveOrder area="gate" type="orc" order="wander"/></Then></Trigger>
            <Trigger id="area" autorun="false"><Then><releaseGrip area="gate" type="orc"/></Then></Trigger>
            """);
        var boss = Spawn(world, Orc, at: new Numerics.Vector3(9, 0.9f, 0));
        boss.UniqueId = ScriptScene.Hash("Boss");
        var held = Spawn(world, Orc, at: new Numerics.Vector3(10, 0.9f, 0));
        Assert.True(boss.GripCharacter(held, GripType.Hold, -1, 10));
        Fire(scene, "free");
        Assert.False(boss.IsGripping);
        Assert.False(held.IsGripped);

        var inside = Spawn(world, Orc, at: new Numerics.Vector3(0, 0.9f, 0));
        var gripper = Spawn(world, Orc, at: new Numerics.Vector3(9, 0.9f, 3));
        Assert.True(gripper.GripCharacter(inside, GripType.Hold, -1, 10));
        Fire(scene, "order");
        Assert.False(inside.IsGripped);                                                            // let go by its gripper, outside the area
        Assert.False(gripper.IsGripping);

        Assert.True(inside.GripCharacter(gripper, GripType.Hold, -1, 10));
        Fire(scene, "area");
        Assert.False(inside.IsGripping);
    }
}
