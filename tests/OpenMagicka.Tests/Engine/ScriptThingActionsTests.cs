using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Items;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The things' actions (LEVELS_TRIGGERS.md §9.2): animated parts, named effects, items.</summary>
public class ScriptThingActionsTests
{
    private static AnimationKeyframe Key(float time, float x) => new(time, new BonePose(new Xna.Vector3(x, 0, 0), Xna.Quaternion.Identity, Xna.Vector3.One));

    /// <summary>A door that slides from x 0 to 10 in 2 s, with a lever on it that slides from 0 to 4 under it, and a beam on the lever.</summary>
    private static AnimatedLevelPartContent Door() => new("Door", false, null, [], [],
        [new LocatorContent("knob", Xna.Matrix.CreateTranslation(0, 1, 0), 1)], 2, [Key(0, 0), Key(2, 10)],
        [new VisualEffectPlacement("Glow", Xna.Vector3.Zero, Xna.Vector3.Forward, 0, "fx_glow")], [], null, null,
        [new AnimatedLevelPartContent("Lever", false, null, [], [], [], 2, [Key(0, 0), Key(2, 4)],
            [new VisualEffectPlacement("ray", new Xna.Vector3(0, 1, 0), new Xna.Vector3(0, -1, 1), 5, "fx_ray")], [], null, null, [])]);

    private sealed class Armoury(ItemTemplate item, OpenMagicka.Gameplay.Props.PropTemplate? prop = null) : IScriptAssets
    {
        public ItemTemplate? Item(string asset) => asset == "data/items/wizard/" + item.Id ? item : null;

        public OpenMagicka.Gameplay.Props.PropTemplate? Prop(string asset) => prop is not null && asset == "data/physicsentities/" + prop.Id ? prop : null;
    }

    [Fact]
    public void SpawnPhys_StandsAPropAtThePlace_WithItsNameAndTriggers()
    {
        using var world = World();
        var barrel = new OpenMagicka.Gameplay.Props.PropTemplate("barrel", false, false, true, 140, 100, OpenMagicka.Gameplay.Combat.ResistanceTable.Neutral,
            new OpenMagicka.Gameplay.Props.PropBox(new Numerics.Vector3(-0.5f, 0, -0.5f), Numerics.Vector3.One, Numerics.Quaternion.Identity), [], "", [], []);
        var scene = Scene(world, """
            <Trigger id="put" autorun="false"><Then><spawnPhys object="barrel" area="spot" id="Keg" onDeath="gone"/></Then></Trigger>
            <Trigger id="gone" autorun="false"><Then><counter name="gone" action="inc" value="1"/></Then></Trigger>
            <Trigger id="count" autorun="false"><If><physicsPresent nr="1" compareMethod="equal"/></If><Then><counter name="seen" action="inc" value="1"/></Then></Trigger>
            """);
        scene.Assets = new Armoury(new ItemTemplate("none"), barrel);

        Fire(scene, "put");
        var keg = Assert.IsType<OpenMagicka.Gameplay.Props.PhysicsProp>(world.Entities.FindByUniqueId(ScriptScene.Hash("keg")));
        Assert.Equal(new Numerics.Vector3(3, 1, 0), keg.Position);                                // its box's centre over the spot
        Fire(scene, "count");
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("seen")));

        keg.Kill();
        scene.Update(Tick);
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("gone")));
    }

    private static ScriptScene Scene(GameWorld world, string triggers)
    {
        var places = new ScriptPlaces([], [new LocatorContent("spot", Xna.Matrix.CreateTranslation(3, 0.5f, 0), 1),
            new LocatorContent("away", Xna.Matrix.CreateTranslation(-4, 0.5f, 0), 1)], [Door()]);
        var xml = $"<Scene><Model>m</Model>{triggers}</Scene>";
        var scene = new ScriptScene(SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/t/t_s1.xml"), new ScriptLevel(),
            ScriptRegistry.Engine(), world, places: places);
        scene.Enter();
        return scene;
    }

    private static void Run(ScriptScene scene, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            scene.Update(Tick);
        }
    }

    private static void Fire(ScriptScene scene, string trigger)
    {
        scene.ExecuteTrigger(ScriptScene.Hash(trigger), null);
        scene.Update(Tick);
    }

    [Fact]
    public void APart_Plays_Loops_GoesBackwards_AndResumes()
    {
        var door = new ScenePart(Door());
        door.Update(0, Numerics.Matrix4x4.Identity);
        Assert.Equal(0, door.World.Translation.X, 3);

        door.Play(withChildren: false, -1, -1, speed: 1, loop: false, resume: false);
        door.Update(1, Numerics.Matrix4x4.Identity);
        Assert.Equal(5, door.World.Translation.X, 3);                                            // halfway, blended
        door.Update(5, Numerics.Matrix4x4.Identity);
        Assert.False(door.IsPlaying);
        Assert.Equal(2, door.Time);

        door.Play(withChildren: false, -1, -1, speed: -1, loop: false, resume: true);              // back from where it is
        door.Update(0.5f, Numerics.Matrix4x4.Identity);
        Assert.Equal(1.5f, door.Time, 3);

        door.Play(withChildren: false, 0, 1, speed: 1, loop: true, resume: false);
        door.Update(1.25f, Numerics.Matrix4x4.Identity);
        Assert.Equal(0.25f, door.Time, 3);
        Assert.True(door.IsPlaying);
        door.Update(3.5f, Numerics.Matrix4x4.Identity);                                           // longer than the loop: still in it
        Assert.Equal(0.75f, door.Time, 3);

        door.Play(withChildren: false, 1, 2, speed: -1, loop: true, resume: false);               // backwards from 2 down to 1
        door.Update(1.25f, Numerics.Matrix4x4.Identity);
        Assert.Equal(1.75f, door.Time, 3);
    }

    [Fact]
    public void PlayAnimation_MovesThePart_ItsChildren_ItsLocators_AndItsEffects()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="open" autorun="false"><Then><playAnimation name="DOOR" speed="2"/></Then></Trigger>
            <Trigger id="lever" autorun="false"><Then><playAnimation name="door/lever" children="false"/></Then></Trigger>
            """);
        var door = scene.Places.Part("door")!;
        var lever = scene.Places.Part("Door/Lever")!;

        Fire(scene, "open");
        Run(scene, 1.1f);

        Assert.Equal(10, door.World.Translation.X, 3);                                           // 2 s of clip at double speed
        Assert.Equal(14, lever.World.Translation.X, 3);                                          // its own clip ran too, on the door
        Assert.Equal(new Numerics.Vector3(10, 1, 0), scene.Places.LocatorPosition(OpenMagicka.Content.LegacyStringHash.Compute("knob")));
        var glow = scene.Effects.Find(ScriptScene.Hash("glow"))!;
        Assert.Equal(10, glow.World.Translation.X, 3);
        Assert.Null(scene.Places.Part("door/handle"));
    }

    [Fact]
    public void TheLevelsEffects_StartWithTheScene_StopAndStartByName_AndASpawnKeepsOnePlaying()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="stop" autorun="false" repeat="true"><Then><stopEffect name="glow"/></Then></Trigger>
            <Trigger id="start" autorun="false" repeat="true"><Then><startEffect name="Glow"/></Then></Trigger>
            <Trigger id="spawn" autorun="false" repeat="true"><Then><spawnEffect name="smoke" effect="FX_Smoke" area="spot"/></Then></Trigger>
            <Trigger id="nameless" autorun="false"><Then><startEffect/></Then></Trigger>
            """);
        var glow = scene.Effects.Find(ScriptScene.Hash("glow"))!;
        Assert.True(glow.IsActive);

        Fire(scene, "stop");
        Assert.False(glow.IsActive);
        Fire(scene, "start");
        Assert.True(glow.IsActive);
        Assert.Equal(2, glow.Starts);

        Fire(scene, "spawn");
        var smoke = scene.Effects.Find(ScriptScene.Hash("smoke"))!;
        Assert.Equal("fx_smoke", smoke.Effect);
        Assert.Equal(new Numerics.Vector3(3, 0.5f, 0), smoke.World.Translation);
        Fire(scene, "spawn");
        Assert.Same(smoke, scene.Effects.Find(ScriptScene.Hash("smoke")));                      // playing: kept as it is
        Fire(scene, "nameless");
        Assert.Equal(3, scene.Effects.All.Count());
    }

    [Fact]
    public void ABeam_IsShortenedToTheLevelInItsWay()
    {
        using var world = World();
        var scene = Scene(world, "");

        Run(scene, Tick);

        var ray = scene.Effects.Find(ScriptScene.Hash("ray"))!;                                   // 1 over the floor, 5 long, 45° down
        Assert.Equal(MathF.Sqrt(2) / 5, ray.Reach, 2);
    }

    [Fact]
    public void SpawnItem_PutsOneItemDown_PinnedWithoutPhysics_AndItsPickupFires()
    {
        using var world = World();
        var leaf = new ItemTemplate("leaf", Pickable: true, Bounds: new ItemBounds(new Numerics.Vector3(-0.3f, 0.05f, -0.3f), new Numerics.Vector3(0.3f, 0.15f, 0.3f)));
        var scene = Scene(world, """
            <Trigger id="drop" autorun="false" repeat="true"><Then><spawnItem item="leaf" area="spot" nr="3" id="Clover" onPickup="got"/></Then></Trigger>
            <Trigger id="got" autorun="false"><Then><counter name="got" action="inc" value="1"/></Then></Trigger>
            """);
        scene.Assets = new Armoury(leaf);

        Fire(scene, "drop");
        OpenMagicka.Tests.Gameplay.CharacterTests.Run(world, 0.5f);
        var item = Assert.Single(world.Entities.Entities.OfType<WorldItem>());                    // one, whatever nr says, as the original
        Assert.Equal(new Numerics.Vector3(3, 0.6f, 0), item.Position);                           // no physics: it stays where it was put
        Assert.Same(item, world.Entities.FindByUniqueId(ScriptScene.Hash("clover")));
        item.MoveTo(Numerics.Matrix4x4.CreateTranslation(-4, 0.5f, 0));
        OpenMagicka.Tests.Gameplay.CharacterTests.Run(world, 0.5f);
        Assert.Equal(new Numerics.Vector3(-4, 0.6f, 0), item.Position);                          // moved as if put down there: its box where its bounds are, pinned
        Fire(scene, "drop");
        Assert.Single(world.Entities.Entities.OfType<WorldItem>());                               // a run again moves the same one back
        Assert.Equal(new Numerics.Vector3(3, 0.6f, 0), item.Position);

        var picker = OpenMagicka.Tests.Gameplay.DropTests.Holder(world, leaf, new Numerics.Vector3(3, 0.9f, 0.5f));
        Assert.True(picker.PickUp(item));
        scene.Update(Tick);
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("got")));
    }
}
