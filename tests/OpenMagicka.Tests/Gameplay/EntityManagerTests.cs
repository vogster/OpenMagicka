using System.Numerics;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Tests.Gameplay;

public class EntityManagerTests
{
    [Fact]
    public void TheServer_GivesEachEntityItsOwnId_AndClientsTakeTheGivenOne()
    {
        var server = new EntityManager();
        var a = new Thing(Vector3.Zero);
        var b = new Thing(Vector3.Zero);

        var idA = server.Add(a);
        var idB = server.Add(b);
        var client = new EntityManager();
        client.Add(new Thing(Vector3.Zero), idB);

        Assert.NotEqual(idA, idB);
        Assert.Equal((idA, idB), (a.Id, b.Id));
        Assert.Same(b, server.Find(idB));
        Assert.NotNull(client.Find(idB));
        Assert.Throws<InvalidOperationException>(() => client.Add(new Thing(Vector3.Zero), idB));
        Assert.Equal(idA, server.Add(a));                                      // already in: nothing changes
        Assert.Equal(2, server.Entities.Count);
    }

    [Fact]
    public void TheManagerHoldsFiveHundredAndTwelve()
    {
        var manager = new EntityManager();
        for (var i = 0; i < EntityManager.Capacity; i++)
        {
            manager.Add(new Thing(Vector3.Zero));
        }

        Assert.Throws<InvalidOperationException>(() => manager.Add(new Thing(Vector3.Zero)));
    }

    [Fact]
    public void EntitiesAddedDuringTheUpdate_AreUpdatedTheSameFrame()
    {
        var manager = new EntityManager();
        var child = new Thing(Vector3.Zero);
        var parent = new Thing(Vector3.Zero) { OnUpdate = t => t.Manager!.Add(child) };
        manager.Add(parent);

        manager.Update(1f / 60);

        Assert.Equal((1, 1), (parent.Updates, child.Updates));
        Assert.Same(manager, parent.Manager);
    }

    [Fact]
    public void RemovableEntities_LeaveAtTheEnd_AndAreDeinitialised()
    {
        var manager = new EntityManager();
        var stays = new Thing(Vector3.Zero);
        var leaves = new Thing(Vector3.Zero) { OnUpdate = t => t.Removed = true };
        manager.Add(stays);
        var id = manager.Add(leaves);

        manager.Update(1f / 60);
        Assert.Equal(2, manager.Entities.Count);                               // nothing leaves during the update
        var removed = manager.RemoveDead();

        Assert.Equal([new RemovedEntity(leaves, id)], removed);                 // with its id, for the broadcast
        Assert.Equal(id, leaves.IdWhenRemoved);
        Assert.Equal(NetId.None, leaves.Id);
        Assert.Equal([stays], manager.Entities);
        Assert.Null(manager.Find(id));
        Assert.Equal(1, leaves.Deinitialised);
        Assert.Null(leaves.Manager);
    }

    [Fact]
    public void UniqueNames_PointToTheLatestEntity_AndAreReleasedOnRemoval()
    {
        var manager = new EntityManager();
        var king = new Thing(Vector3.Zero) { UniqueId = 42 };
        var heir = new Thing(Vector3.Zero) { UniqueId = 42 };
        manager.Add(king);
        manager.Add(heir);

        Assert.Same(heir, manager.FindByUniqueId(42));
        king.Removed = true;
        manager.RemoveDead();
        Assert.Same(heir, manager.FindByUniqueId(42));                         // the king no longer held the name
        heir.Removed = true;
        manager.RemoveDead();
        Assert.Null(manager.FindByUniqueId(42));
        Assert.Null(manager.FindByUniqueId(0));
    }

    [Fact]
    public void ARadiusQuery_FindsTheLivingWithinReach_CountingTheirRadius()
    {
        var manager = new EntityManager();
        var near = new Thing(new Vector3(3, 0, 0), radius: 1);
        var edge = new Thing(new Vector3(5.9f, 0, 0), radius: 1);
        var far = new Thing(new Vector3(7, 0, 0), radius: 1);
        var dead = new Thing(new Vector3(1, 0, 0)) { Gone = true };
        var above = new Thing(new Vector3(3, 10, 0), radius: 1);
        foreach (var thing in new[] { near, edge, far, dead, above })
        {
            manager.Add(thing);
        }
        manager.RebuildGrid();

        Assert.Equal([near, edge], manager.Query(Vector3.Zero, radius: 5).OrderBy(e => e.Position.X));
        Assert.Contains(above, manager.Query(Vector3.Zero, 5, ignoreY: true));
    }

    [Fact]
    public void TheGridWraps_SoFarEntitiesShareCells_ButAreFilteredByDistance()
    {
        var manager = new EntityManager();
        var here = new Thing(new Vector3(1, 0, 1));
        var wrapped = new Thing(new Vector3(1 + 128, 0, 1));                   // 16 cells away: the same cell
        manager.Add(here);
        manager.Add(wrapped);
        manager.RebuildGrid();

        Assert.Equal([here], manager.Query(new Vector3(1, 0, 1), 2));
        Assert.Equal([wrapped], manager.Query(new Vector3(129, 0, 1), 2));
        Assert.Equal(2, manager.Query(new Vector3(64, 0, 1), 200).Count);      // wider than the grid: every cell, each once
    }

    [Fact]
    public void ABigEntity_IsFoundFromCellsItReaches()
    {
        var manager = new EntityManager();
        var giant = new Thing(new Vector3(4, 0, 4), radius: 20);
        manager.Add(giant);
        manager.RebuildGrid();

        Assert.Equal([giant], manager.Query(new Vector3(22, 0, 4), 0.5f));
        Assert.Equal([giant], manager.Query(new Vector3(4, 0, -14), 0.5f));
        Assert.Empty(manager.Query(new Vector3(22, 0, 22), 0.5f));            // the corner is outside the circle
    }

    [Fact]
    public void AnEntityMovedSinceTheLastRebuild_IsWhereTheGridLastSawIt()
    {
        var manager = new EntityManager();
        var runner = new Thing(new Vector3(1, 0, 1));
        manager.Add(runner);
        manager.RebuildGrid();

        runner.At = new Vector3(40, 0, 40);

        Assert.Empty(manager.Query(new Vector3(40, 0, 40), 1));               // its old cell is far away
        manager.RebuildGrid();
        Assert.Equal([runner], manager.Query(new Vector3(40, 0, 40), 1));
    }

    [Fact]
    public void Shields_HideWhatIsBehindThem_FromProtectedQueries()
    {
        var manager = new EntityManager();
        var shield = new TestShield(new Vector3(6, 0, 0), radius: 2);
        var behind = new Thing(new Vector3(6, 0, 0));                          // inside the bubble
        var open = new Thing(new Vector3(0, 0, 3));
        var crate = new Thing(new Vector3(0, 0, -3), damageable: false);
        foreach (var e in new Entity[] { shield, behind, open, crate })
        {
            manager.Add(e);
        }
        manager.RebuildGrid();

        var seen = manager.Query(Vector3.Zero, 10, ignoreProtected: true);

        Assert.Contains(open, seen);
        Assert.Contains(shield, seen);
        Assert.DoesNotContain(behind, seen);
        Assert.DoesNotContain(crate, seen);                                    // the original drops every non-damageable
        Assert.Equal(4, manager.Query(Vector3.Zero, 10).Count);
        Assert.True(manager.IsProtectedByShield(behind, out var by));
        Assert.Same(shield, by);
        Assert.False(manager.IsProtectedByShield(open, out _));
        Assert.Equal([shield], manager.Shields);
    }

    [Fact]
    public void TheClosestDamageable_IsNotTheCaster_NorBehindAShield()
    {
        var manager = new EntityManager();
        var caster = new Thing(Vector3.Zero);
        var crate = new Thing(new Vector3(1, 0, 0), damageable: false);
        var shield = new TestShield(new Vector3(2, 0, 0), radius: 1.5f);
        var hidden = new Thing(new Vector3(2, 0, 0));
        var open = new Thing(new Vector3(0, 0, 4));
        foreach (var e in new Entity[] { caster, crate, shield, hidden, open })
        {
            manager.Add(e);
        }
        manager.RebuildGrid();

        Assert.Same(hidden, manager.Closest(Vector3.Zero, 10, caster));         // the crate and the shield are not damageable
        Assert.Same(open, manager.Closest(Vector3.Zero, 10, caster, ignoreProtected: true));
        Assert.Null(manager.Closest(new Vector3(100, 0, 100), 1));
    }

    [Fact]
    public void ClearingTheManager_DeinitialisesEverything()
    {
        var manager = new EntityManager();
        var thing = new Thing(Vector3.Zero);
        manager.Add(thing);

        manager.Clear();

        Assert.Empty(manager.Entities);
        Assert.Equal(1, thing.Deinitialised);
    }
}
