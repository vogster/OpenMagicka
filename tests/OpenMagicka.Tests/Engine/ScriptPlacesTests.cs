using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.AI;
using Microsoft.Xna.Framework;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>A scene's areas and locators, and the minds reaching its triggers (LEVELS_TRIGGERS.md §2.3, §5.7).</summary>
public class ScriptPlacesTests
{
    private static ScriptPlaces Places() => new(
        [new TriggerAreaContent("Gate", new Vector3(-2, 0, -2), new Vector3(4, 3, 4), Quaternion.Identity)],
        [new LocatorContent("spot", Matrix.CreateTranslation(5, 0, 1), 1), new LocatorContent("Shout", Matrix.CreateTranslation(9, 0, 9), 1)]);

    [Fact]
    public void AnArea_CountsWhoIsInIt_ByTypeAndFaction_TheDeadApart()
    {
        var world = World();
        using var _ = world;
        var places = Places();
        var inside = Animated(world, Orc, at: new Numerics.Vector3(0, 0.9f, 0));
        var dead = Animated(world, Orc, at: new Numerics.Vector3(1, 0.9f, 1));
        Animated(world, Orc, at: new Numerics.Vector3(8, 0.9f, 0));
        dead.Kill();
        Run(world, 2);

        places.Refill(world.Entities);
        var gate = places.Area("GATE")!;

        Assert.Equal(2, gate.Characters.Count);
        Assert.Equal(1, gate.Count);
        Assert.Equal(1, gate.CountOf("orc"));
        Assert.Equal(1, gate.CountOf(OpenMagicka.Gameplay.Characters.Factions.Wild | OpenMagicka.Gameplay.Characters.Factions.Evil));
        Assert.Equal(3, places.AnyArea.Count + 1);                                           // the whole level, the dead one apart
        Assert.Contains(inside, gate.Characters);
    }

    [Fact]
    public void Locators_AreFoundByTheirNamesAsWritten_AndAreasByAnyCase()
    {
        var places = Places();
        IAiPlaces asPlaces = places;

        Assert.Equal(new Numerics.Vector3(5, 0, 1), places.LocatorPosition(OpenMagicka.Content.LegacyStringHash.Compute("spot")));
        Assert.Null(places.LocatorPosition(OpenMagicka.Content.LegacyStringHash.Lower("Shout")));                // "shout" does not find "Shout", as the original
        Assert.Equal(new Numerics.Vector3(2, 0, 0), asPlaces.ClosestInArea(OpenMagicka.Content.LegacyStringHash.Lower("gate"), new Numerics.Vector3(7, 0, 0)));
        var random = new Random(3);
        for (var i = 0; i < 20; i++)
        {
            var point = places.Area("gate")!.RandomPoint(random);
            Assert.InRange(point.X, -2, 2);
            Assert.InRange(point.Y, 0, 3);
        }
    }

    [Fact]
    public void AMindsEvent_RunsTheScenesTrigger_AsItsCharacter()
    {
        const string xml = """
            <Scene><Model>m</Model>
              <Trigger id="arrived" autorun="false" repeat="true"><Then><counter name="arrived" action="inc" value="1"/></Then></Trigger>
            </Scene>
            """;
        var world = World();
        using var _ = world;
        var scene = new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, places: Places());
        var orc = Animated(world, Orc, at: new Numerics.Vector3(0, 0.9f, 0));
        OpenMagicka.Gameplay.Characters.Character? sender = null;
        scene.TriggerFired += (_, _, by) => sender = by;
        Assert.Null(world.Entities.Places);                                                        // not the world's scene until entered
        scene.Enter();
        scene.Enter();
        Run(world, Tick);

        orc.Mind!.SetOrder(OpenMagicka.Gameplay.AI.Order.Idle, events: [new MoveAiEvent(ScriptScene.Hash("arrived"), 0, orc.Position with { Y = 0 })]);
        for (var t = 0f; t < 0.5f; t += Tick)
        {
            world.Tick(Tick);
            scene.Update(Tick);
        }

        Assert.Equal(2, scene.Level.Counter(ScriptScene.Hash("arrived")));                         // as the original: a walk with no delay fires on arrival, and again as its delay runs out
        Assert.Same(orc, sender);
        Assert.Same(scene.Places, world.Entities.Places);
        scene.Leave();
        Assert.Null(world.Entities.Places);

        orc.Mind!.SetOrder(OpenMagicka.Gameplay.AI.Order.Idle, events: [new MoveAiEvent(ScriptScene.Hash("arrived"), 0, orc.Position with { Y = 0 })]);
        for (var t = 0f; t < 0.5f; t += Tick)
        {
            world.Tick(Tick);
            scene.Update(Tick);
        }
        Assert.Equal(2, scene.Level.Counter(ScriptScene.Hash("arrived")));                         // left: the minds no longer reach it
    }

    [Fact]
    public void ARotatedArea_ClampsInItsOwnFrame()
    {
        var turned = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2);              // its x runs along the world's -z
        IAiPlaces places = new ScriptPlaces([new TriggerAreaContent("hall", Vector3.Zero, new Vector3(4, 3, 2), turned)], []);
        var hall = OpenMagicka.Content.LegacyStringHash.Lower("hall");

        var inside = places.ClosestInArea(hall, new Numerics.Vector3(1, 1, -3));
        var outside = places.ClosestInArea(hall, new Numerics.Vector3(5, 1, -3));

        Assert.True(Numerics.Vector3.Distance(new Numerics.Vector3(1, 1, -3), inside!.Value) < 1e-4f);
        Assert.True(Numerics.Vector3.Distance(new Numerics.Vector3(2, 1, -3), outside!.Value) < 1e-4f);
    }
}
