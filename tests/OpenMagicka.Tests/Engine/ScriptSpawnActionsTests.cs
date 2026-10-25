using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Engine.ScriptWorldActionsTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>What OM-46's actions put in the world (LEVELS_TRIGGERS.md §9.4): books, eggs, dispensers, clones, animated props; and the parts' clips.</summary>
public class ScriptSpawnActionsTests
{
    private static PropTemplate Spore(float hitPoints) => new("spore", true, false, true, 140, hitPoints, ResistanceTable.Neutral,
        new PropBox(new Numerics.Vector3(-0.5f, 0, -0.5f), Numerics.Vector3.One, Numerics.Quaternion.Identity), [], "", [], []);

    private sealed class Spores(PropTemplate spore) : IScriptAssets
    {
        public ItemTemplate? Item(string asset) => null;

        public PropTemplate? Prop(string asset) => asset == "data/animatedprops/" + spore.Id ? spore : null;
    }

    [Fact]
    public void SpawnMagick_LaysABook_WhosePickUpSetsOffItsTrigger()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        var avatar = Avatar(world, session.AddPlayer(), x: 5);
        var scene = Scene(world, """
            <Trigger id="lay" autorun="false"><Then><spawnMagick magick="Haste" area="spot" id="Tome" onPickup="read"/></Then></Trigger>
            <Trigger id="read" autorun="false"><Then><counter name="read" action="inc" value="1"/></Then></Trigger>
            """);

        Fire(scene, "lay");
        var book = Assert.IsType<BookOfMagick>(world.Entities.FindByUniqueId(ScriptScene.Hash("tome")));
        Assert.Equal(new Numerics.Vector3(5, 0, 1), book.Position);
        avatar.Body.Body.Position = new Numerics.Vector3(5, 0.9f, 1);

        Assert.True(avatar.PickUp(book));
        scene.Update(Tick);

        Assert.True(session.Players[0].HasUnlocked(MagickType.Haste));
        Assert.Equal(1, scene.Level.Counter(ScriptScene.Hash("read")));
    }

    [Fact]
    public void SpawnMagick_OfNoPlayerMagick_FailsTheLoading()
    {
        using var world = World();
        Assert.Throws<InvalidDataException>(() => Scene(world, """<Trigger id="t"><Then><spawnMagick area="spot"/></Then></Trigger>"""));
    }

    [Fact]
    public void SpawnElemental_LaysItsEggs_AndAReplayLaysAsManyAgain()
    {
        using var world = World();
        var scene = Scene(world, """<Trigger id="lay" autorun="false"><Then><spawnElemental nr="3" area="gate" proximity="10"/></Then></Trigger>""");

        Fire(scene, "lay");
        var eggs = world.Entities.Entities.OfType<ElementalEgg>().ToArray();
        Assert.Equal(3, eggs.Length);
        Assert.All(eggs, e => Assert.Equal(10, e.Proximity));
        Assert.All(eggs, e => Assert.InRange(e.Position.X, -2, 2));

        var action = scene.Find("lay")!.Variants[0].Single();
        var state = action.Save();
        world.Entities.Clear();
        action.Load(state);
        action.Replay();
        Assert.Equal(3, world.Entities.Entities.OfType<ElementalEgg>().Count());
    }

    [Fact]
    public void SpawnDispenser_StandsOverTheFloor_AndOnlyTheNamedOneIsActivated()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="put" autorun="false"><Then>
              <spawnDispenser model="spidereggs" area="spot" types="goblin" amount="2" time="0.3" active="false" id="clutch"/>
              <spawnDispenser model="spidereggs" area="far0" types="goblin" amount="2" time="0.3" active="false"/>
            </Then></Trigger>
            <Trigger id="wake" autorun="false"><Then><activateDispenser id="clutch"/><activateDispenser type="spidereggs" area="gate"/></Then></Trigger>
            """);

        Fire(scene, "put");
        var dispensers = world.Entities.Entities.OfType<Dispenser>().ToArray();
        Assert.Equal(2, dispensers.Length);
        var clutch = Assert.IsType<Dispenser>(world.Entities.FindByUniqueId(ScriptScene.Hash("clutch")));
        Assert.Equal(new Numerics.Vector3(5, Dispenser.Lift, 1), clutch.Position);
        Fire(scene, "wake");

        Assert.True(clutch.IsActive);
        Assert.False(dispensers.Single(d => d != clutch).IsActive);                              // as the original: the area's form wakes none
    }

    [Fact]
    public void SpawnCloneWizards_MakesOneForEachLivingPlayer_AtItsLocator()
    {
        var session = new GameSession();
        using var world = new GameWorld(session: session);
        world.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
            [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
        Avatar(world, session.AddPlayer());
        Avatar(world, session.AddPlayer(), x: 5);
        var scene = Scene(world, """<Trigger id="go" autorun="false"><Then><spawnCloneWizards area="far" order="idle" reactto="none"/></Then></Trigger>""");

        Fire(scene, "go");

        var clones = new[] { "clonewizard0", "clonewizard1" }.Select(n => Assert.IsType<Character>(world.Entities.FindByUniqueId(ScriptScene.Hash(n)))).ToArray();
        Assert.All(clones, c => Assert.False(c.IsPlayer));
        Assert.All(clones, c => Assert.Equal("wizard", c.Template.Id));
        Assert.Equal(20, clones[0].Position.X, 2);
        Assert.Equal(-20, clones[1].Position.X, 2);
        Assert.Equal(OpenMagicka.Gameplay.AI.Order.Idle, clones[0].Mind?.Order ?? OpenMagicka.Gameplay.AI.Order.Idle);
    }

    [Fact]
    public void SpawnAnimProp_TakesHitsOnlyWhenDamageable_AndPutsANewOneOnceTheLastIsGone()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="hard" autorun="false" repeat="true"><Then><spawnAnimProp object="spore" area="spot" id="spore0" damageable="true" gravity="false"/></Then></Trigger>
            <Trigger id="soft" autorun="false"><Then><spawnAnimProp object="spore" area="far0" damageable="false"/></Then></Trigger>
            """);
        scene.Assets = new Spores(Spore(50));

        Fire(scene, "hard");
        Fire(scene, "soft");
        var spore = Assert.IsType<PhysicsProp>(world.Entities.FindByUniqueId(ScriptScene.Hash("spore0")));
        Assert.True(spore.IsDamageable);
        Assert.False(spore.Template.Movable);
        Assert.Single(world.Entities.Entities.OfType<PhysicsProp>(), p => !p.IsDamageable);
        Fire(scene, "hard");
        Assert.Equal(2, world.Entities.Entities.OfType<PhysicsProp>().Count());                  // the first still stands: no new one

        spore.Kill();
        world.Tick(Tick);
        Fire(scene, "hard");
        Assert.Equal(2, world.Entities.Entities.OfType<PhysicsProp>().Count());
        Assert.NotSame(spore, world.Entities.FindByUniqueId(ScriptScene.Hash("spore0")));
    }

    [Fact]
    public void StopResumeAndHighlight_WorkOnTheNamedPartsAndProps()
    {
        using var world = World();
        var scene = Scene(world, """
            <Trigger id="hold" autorun="false"><Then><highlightPhys id="keg" time="2"/></Then></Trigger>
            """);
        var keg = ScriptProps.Place(world, Spore(50), Numerics.Matrix4x4.CreateTranslation(0, 0, 10));
        keg.UniqueId = ScriptScene.Hash("keg");

        Fire(scene, "hold");
        Assert.Equal(2, keg.Highlighted, 3);
        world.Tick(1);
        Assert.InRange(keg.Highlighted, 0.5f, 1.99f);
    }

    [Fact]
    public void APart_Resumes_ForALength_OrToTheClipsEnd_KeepingItsLoopUnlessGiven()
    {
        var door = new ScenePart(new OpenMagicka.Content.Magicka.AnimatedLevelPartContent("Door", false, null, [], [], [], 2,
            [new(0, new OpenMagicka.Content.XnAnimation.BonePose(Microsoft.Xna.Framework.Vector3.Zero, Microsoft.Xna.Framework.Quaternion.Identity,
                Microsoft.Xna.Framework.Vector3.One))], [], [], null, null, []));
        door.Play(withChildren: false, -1, -1, speed: 1, loop: true, resume: false);
        door.Update(0.5f, Numerics.Matrix4x4.Identity);
        door.Stop(withChildren: false);
        Assert.False(door.IsPlaying);

        door.Resume(withChildren: false, length: 0.25f, speed: 1, loop: null);
        Assert.True(door.IsPlaying && door.IsLooping);
        door.Update(0.5f, Numerics.Matrix4x4.Identity);
        Assert.Equal(0.25f, door.Time, 3);                                                       // past the new end (0.75): looped back

        door.Resume(withChildren: false, length: 0, speed: 1, loop: false);
        door.Update(5, Numerics.Matrix4x4.Identity);
        Assert.Equal(2, door.Time, 3);                                                           // to the clip's end, then it stops
        Assert.False(door.IsPlaying);

        door.Highlight(1);
        door.Update(0.25f, Numerics.Matrix4x4.Identity);
        Assert.Equal(0.75f, door.Highlighted, 3);
    }
}
