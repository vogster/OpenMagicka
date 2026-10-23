using System.Numerics;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The manager's guards: ids, re-entry, stale cells, runaway values.</summary>
public class EntityManagerEdgeTests
{
    [Fact]
    public void LocalEntities_TakeIdsTheServerNeverUses()
    {
        var client = new EntityManager();
        client.Add(new Thing(Vector3.Zero), new NetId(5));

        var gib = client.AddLocal(new Thing(Vector3.Zero));
        client.Add(new Thing(Vector3.Zero), new NetId(6));                     // the server's next spawn still fits

        Assert.True(gib.Value >= EntityManager.LocalIds);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Add(new Thing(Vector3.Zero), new NetId(EntityManager.LocalIds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Add(new Thing(Vector3.Zero), NetId.None));
        Assert.Equal(new NetId(7), client.Add(new Thing(Vector3.Zero)));         // a server id after the explicit ones
        Assert.Throws<InvalidOperationException>(() => new EntityManager().Add(client.Entities[0]));   // one manager at a time
    }

    [Fact]
    public void QueriesSkipEntitiesThatLeft_EvenBeforeTheNextRebuild()
    {
        var manager = new EntityManager();
        var leaving = new Thing(new Vector3(1, 0, 1)) { Removed = true };
        manager.Add(leaving);
        manager.RebuildGrid();

        manager.RemoveDead();

        Assert.Empty(manager.Query(new Vector3(1, 0, 1), 1));
        Assert.Null(manager.Closest(new Vector3(1, 0, 1), 1));
    }

    [Fact]
    public void AnEntity_CanComeBackFromItsRemoval_Once()
    {
        var manager = new EntityManager();
        var phoenix = new Thing(Vector3.Zero) { Removed = true };
        phoenix.OnRemove = t =>
        {
            t.Removed = false;
            t.OnRemove = null;
            manager.Add(t);
        };
        manager.Add(phoenix);

        manager.RemoveDead();

        Assert.Equal([phoenix], manager.Entities);
        Assert.Same(manager, phoenix.Manager);
        Assert.False(phoenix.Id.IsNone);
    }

    [Fact]
    public void NothingUpdatesOrRemoves_WhileTheManagerUpdates()
    {
        var manager = new EntityManager();
        var errors = new List<Exception>();
        var meddler = new Thing(Vector3.Zero)
        {
            OnUpdate = t =>
            {
                foreach (var act in new Action[] { () => manager.Update(1), () => manager.RemoveDead(), manager.Clear })
                {
                    try
                    {
                        act();
                    }
                    catch (InvalidOperationException e)
                    {
                        errors.Add(e);
                    }
                }
            },
        };
        manager.Add(meddler);

        manager.Update(1f / 60);

        Assert.Equal(3, errors.Count);
        Assert.Equal([meddler], manager.Entities);
        manager.Update(1f / 60);                                                  // the guard was released
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Update(float.NaN));
    }

    [Fact]
    public void ANewName_MovesTheEntry()
    {
        var manager = new EntityManager();
        var thing = new Thing(Vector3.Zero) { UniqueId = 7 };
        manager.Add(thing);

        thing.UniqueId = 8;

        Assert.Null(manager.FindByUniqueId(7));
        Assert.Same(thing, manager.FindByUniqueId(8));
        thing.Removed = true;
        manager.RemoveDead();
        Assert.Null(manager.FindByUniqueId(8));
    }

    [Fact]
    public void ClearingReleasesIdsNamesAndShields()
    {
        var manager = new EntityManager();
        var shield = new TestShield(Vector3.Zero, 2) { UniqueId = 3 };
        var id = manager.Add(shield);

        manager.Clear();

        Assert.Null(manager.Find(id));
        Assert.Null(manager.FindByUniqueId(3));
        Assert.Empty(manager.Shields);
    }

    [Fact]
    public void AnEntityJustAcrossARow_IsFoundFromThere()
    {
        var manager = new EntityManager();
        var thing = new Thing(new Vector3(4, 0, 4), radius: 4.8f);              // reaches z = 8.8, into the next row
        manager.Add(thing);
        manager.RebuildGrid();

        Assert.Equal([thing], manager.Query(new Vector3(4, 0, 8.5f), 0.5f));
        Assert.Same(thing, manager.Closest(new Vector3(4, 0, 8.5f), 0.5f));
    }

    [Fact]
    public void RunawayValues_DoNotStallTheGrid()
    {
        var manager = new EntityManager();
        manager.Add(new Thing(new Vector3(1e12f, 0, 0)));
        manager.Add(new Thing(new Vector3(float.NaN, 0, 0)));
        var near = new Thing(Vector3.Zero);
        manager.Add(near);

        manager.RebuildGrid();

        Assert.Equal([near], manager.Query(Vector3.Zero, 1));
        Assert.Empty(manager.Query(new Vector3(1e12f, 0, 0), 1));             // left out of the grid
        Assert.Equal([near], manager.Query(Vector3.Zero, float.MaxValue).Where(e => ReferenceEquals(e, near)));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Query(Vector3.Zero, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Query(Vector3.Zero, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Closest(new Vector3(float.NaN, 0, 0), 1));
    }

    [Fact]
    public void AProtectedQuery_WithNoShieldInIt_KeepsEverything()
    {
        var manager = new EntityManager();
        var crate = new Thing(new Vector3(1, 0, 0), damageable: false);
        manager.Add(crate);
        manager.RebuildGrid();

        Assert.Equal([crate], manager.Query(Vector3.Zero, 2, ignoreProtected: true));
    }

    [Fact]
    public void ShieldsInAProtectedQuery_FilterEachOther()
    {
        var manager = new EntityManager();
        var plain = new TestShield(new Vector3(5, 0, 0), 1);
        var sturdy = new TestShield(new Vector3(-5, 0, 0), 1, damageable: true);
        manager.Add(plain);
        manager.Add(sturdy);
        manager.RebuildGrid();

        var seen = manager.Query(Vector3.Zero, 10, ignoreProtected: true);

        Assert.Equal([sturdy], seen);                                            // the plain one is not damageable: the sturdy one drops it
    }

    [Fact]
    public void ASpreadOutEntity_IsFoundOnce()
    {
        var manager = new EntityManager();
        var wide = new Thing(Vector3.Zero, radius: 30);
        manager.Add(wide);
        manager.RebuildGrid();

        Assert.Equal([wide], manager.Query(Vector3.Zero, 40));
    }
}
