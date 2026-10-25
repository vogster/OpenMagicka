using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Physics;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>A moving level part's collision (OM-74): it follows the part, and carries what stands on it while the part plays.</summary>
public class PartCollisionTests
{
    /// <summary>A 4 × 4 floor at the part's origin.</summary>
    private static PartCollisionContent Floor(CollisionMaterial material = CollisionMaterial.Stone) => new(material, new CollisionMeshContent(
        [new(-2, 0, -2), new(2, 0, -2), new(2, 0, 2), new(-2, 0, 2)], [new CollisionTriangle(0, 2, 1), new CollisionTriangle(0, 3, 2)]));

    private static AnimationKeyframe Key(float time, Xna.Vector3 at, float turn = 0) =>
        new(time, new BonePose(at, Xna.Quaternion.CreateFromAxisAngle(Xna.Vector3.Up, turn), Xna.Vector3.One));

    /// <summary>A platform a metre up that slides 10 m along x in 2 s.</summary>
    private static AnimatedLevelPartContent Slider(PartCollisionContent? collision = null) => new("Slider", false, null, [], [], [], 2,
        [Key(0, new Xna.Vector3(0, 1, 0)), Key(2, new Xna.Vector3(10, 1, 0))], [], [], collision ?? Floor(), null, []);

    /// <summary>A platform a metre up that turns a quarter round in 2 s.</summary>
    private static AnimatedLevelPartContent Turner() => new("Turner", false, null, [], [], [], 2,
        [Key(0, new Xna.Vector3(0, 1, 0)), Key(2, new Xna.Vector3(0, 1, 0), Xna.MathHelper.PiOver2)], [], [], Floor(), null, []);

    private static ScriptPlaces Places(GameWorld world, params AnimatedLevelPartContent[] parts)
    {
        var places = new ScriptPlaces([], [], parts);
        places.UpdateParts(0);
        LevelCollision.LoadParts(world.Physics, places);
        return places;
    }

    /// <summary>The world steps, then the parts take their places, as a frame of the level.</summary>
    private static void Play(GameWorld world, ScriptPlaces places, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            world.Tick(Tick);
            places.UpdateParts(Tick);
        }
    }

    private static PhysicsBody Box(GameWorld world, Numerics.Vector3 at) => world.Physics.AddBody(new BoxShape(new Numerics.Vector3(0.5f)), at, mass: 1);

    [Fact]
    public void ABoxOnAPlayingPart_RidesWithIt_AndOneOnAPartAtRest_StaysPut()
    {
        using var world = World();
        var places = Places(world, Slider());
        var part = Assert.Single(places.AllParts);
        var box = Box(world, new Numerics.Vector3(0, 1.5f, 0));

        Play(world, places, 1);
        Assert.Equal(0, box.Position.X, 2);                                                       // at rest: nothing carried
        Assert.Empty(part.Collision!.Carried);

        part.Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
        Play(world, places, 2.5f);

        Assert.False(part.IsPlaying);
        Assert.Equal(10, box.Position.X, 0);                                                      // rode the whole way
        Assert.Equal(1.25f, box.Position.Y, 1);                                                   // still on top of it
        Assert.Empty(part.Collision.Carried);                                                     // the part stopped: none renewed
    }

    [Fact]
    public void ABoxAsleepOnAPart_WakesAndRides_WhenThePartPlays()
    {
        using var world = World();
        var places = Places(world, Slider());
        var part = Assert.Single(places.AllParts);
        var box = Box(world, new Numerics.Vector3(0, 1.5f, 0));
        Play(world, places, 5);
        Assert.False(box.IsAwake);                                                                // it lay still long enough to sleep

        part.Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
        Play(world, places, 2.5f);

        Assert.Equal(10, box.Position.X, 0);
        Assert.Equal(1.25f, box.Position.Y, 1);
    }

    [Fact]
    public void ABoxAsleepOnALiftGoingDown_GoesDownWithIt()
    {
        using var world = World();
        var lift = new AnimatedLevelPartContent("Lift", false, null, [], [], [], 2,
            [Key(0, new Xna.Vector3(0, 5, 0)), Key(2, new Xna.Vector3(0, 1, 0))], [], [], Floor(), null, []);
        var places = Places(world, lift);
        var box = Box(world, new Numerics.Vector3(0, 5.5f, 0));
        Play(world, places, 5);
        Assert.False(box.IsAwake);

        places.AllParts.Single().Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
        Play(world, places, 2.5f);

        Assert.Equal(1.25f, box.Position.Y, 1);
    }

    [Fact]
    public void AStandingCharacterOnATurningPart_IsCarriedRound_AndItsWantedWayTurns()
    {
        using var world = World();
        var places = Places(world, Turner());
        var part = Assert.Single(places.AllParts);
        var goblin = Spawn(world, at: new Numerics.Vector3(1, 2, 0));
        goblin.Body.WantedDirection = Numerics.Vector3.UnitX;
        Run(world, 0.5f);

        part.Play(withChildren: false, from: -1, to: -1, speed: 1, loop: false, resume: false);
        Play(world, places, 2.2f);

        Assert.True(Numerics.Vector3.Distance(new Numerics.Vector3(0, 0, -1), goblin.Position with { Y = 0 }) < 0.2f, goblin.Position.ToString());
        Assert.True(Numerics.Vector3.Dot(-Numerics.Vector3.UnitZ, goblin.Body.WantedDirection) > 0.95f, goblin.Body.WantedDirection.ToString());
    }

    [Fact]
    public void ATouchFromTheSide_CarriesNothing_AndATouchCarriesForAQuarterSecond()
    {
        using var world = new PhysicsWorld();
        var mesh = world.AddStaticMesh(5, [new(-2, 0, -2), new(2, 0, -2), new(2, 0, 2), new(-2, 0, 2)], [new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);
        var collision = new PartCollision(world, mesh, Numerics.Matrix4x4.Identity);
        var box = world.AddBody(new BoxShape(Numerics.Vector3.One), new Numerics.Vector3(0, 5, 0), mass: 1);

        collision.Update(Tick, Numerics.Matrix4x4.Identity, isPlaying: true);
        mesh.Touched!(box, new Contact(new Numerics.Vector3(1, 0.5f, 0), default, 0, null, mesh));
        Assert.False(collision.Touches(box));                                                      // pushed sideways more than up

        mesh.Touched(box, new Contact(Numerics.Vector3.UnitY, default, 0, null, mesh));
        Assert.True(collision.Touches(box));
        collision.Update(0.2f, Numerics.Matrix4x4.CreateTranslation(1, 0, 0), isPlaying: true);
        Assert.Equal(1, box.Position.X, 3);
        Assert.True(collision.Touches(box));
        collision.Update(0.1f, Numerics.Matrix4x4.CreateTranslation(2, 0, 0), isPlaying: true);
        Assert.Equal(2, box.Position.X, 3);                                                       // the frame its time ran out still moves it
        Assert.False(collision.Touches(box));
        collision.Update(0.1f, Numerics.Matrix4x4.CreateTranslation(3, 0, 0), isPlaying: true);
        Assert.Equal(2, box.Position.X, 3);
    }

    [Fact]
    public void LoadParts_GivesSolidPartsTheirCollision_WhereTheyAre_AndUnloadTakesItAway()
    {
        using var world = World();
        var places = new ScriptPlaces([], [], [Slider(), Slider(Floor(CollisionMaterial.Water)) with { Name = "Pool" }]);
        places.UpdateParts(0);

        var meshes = LevelCollision.LoadParts(world.Physics, places);

        Assert.Single(meshes);
        var slider = places.AllParts.Single(p => p.Name == "slider");
        Assert.Same(meshes[0], slider.Collision!.Mesh);
        Assert.Null(places.AllParts.Single(p => p.Name == "pool").Collision!.Mesh);               // water is a liquid, not a floor
        Assert.Equal(1, world.Physics.CastSegment(new Numerics.Vector3(0, 5, 0), new Numerics.Vector3(0, 0.5f, 0), levelOnly: true)!.Value.Position.Y, 3);

        LevelCollision.UnloadParts(places);
        Assert.All(places.AllParts, p => Assert.Null(p.Collision));
    }

    [Fact]
    public void WhatIsAttached_RidesAgain_WhenTheSceneIsEnteredAgain()
    {
        using var world = World();
        var places = Places(world, Slider());
        var part = Assert.Single(places.AllParts);
        var kept = Box(world, new Numerics.Vector3(0, 1.5f, 0));
        var gone = Box(world, new Numerics.Vector3(1, 1.5f, 0));
        part.Collision!.Attach(kept);
        part.Collision.Attach(gone);

        LevelCollision.UnloadParts(places);
        world.Physics.Remove(gone);                                                               // not kept with the scene
        LevelCollision.LoadParts(world.Physics, places);

        Assert.True(part.Collision!.Touches(kept));
        Assert.False(part.Collision.Touches(gone));
        Assert.Empty(part.Attached);
    }

    [Fact]
    public void APartWithNoFinitePose_PutsNoMeshInTheWorld()
    {
        using var world = World();
        var ring = new AnimatedLevelPartContent("Ring", false, null, [], [], [], 1,
            [new AnimationKeyframe(0, new BonePose(new Xna.Vector3(float.NaN), Xna.Quaternion.Identity, Xna.Vector3.One))], [], [], Floor(), null, []);

        var places = Places(world, ring);

        Assert.Null(Assert.Single(places.AllParts).Collision!.Mesh);
        Assert.Null(world.Physics.CastSegment(new Numerics.Vector3(0, 5, 0), new Numerics.Vector3(0, 0.5f, 0), levelOnly: true));   // no wall at the origin
    }
}
