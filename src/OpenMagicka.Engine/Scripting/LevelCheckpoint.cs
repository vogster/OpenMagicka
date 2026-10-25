using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// An entity a checkpoint keeps (docs/formats/LEVELS_TRIGGERS.md §6.2), as the original: the standing
/// props and the pickable items. The NPCs come back by their spawns' replay, the rest by the scripts.
/// </summary>
public abstract record EntityState
{
    /// <summary>What of an entity a checkpoint keeps; null for one it does not.</summary>
    public static EntityState? Of(Entity entity) => entity switch
    {
        PhysicsProp { IsDead: false } prop => new PropState(prop.Template, prop.Transform, prop.HitPoints, prop.OnDeathTrigger, prop.OnDamageTrigger,
            prop.UniqueId),
        WorldItem { IsPickable: true, IsDead: false } item => new ItemState(item.Item.Template, item.Position, item.IsPinned, item.OnPickupTrigger,
            item.IgnoresTractorPull, item.UniqueId),
        _ => null,
    };

    /// <summary>The entity again, not yet in a level.</summary>
    public abstract Entity Make(GameWorld world);
}

public sealed record PropState(PropTemplate Template, Matrix4x4 Transform, float HitPoints, int OnDeath, int OnDamage, int UniqueId) : EntityState
{
    public override Entity Make(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var prop = new PhysicsProp(world.Physics, Template, Transform) { OnDeathTrigger = OnDeath, OnDamageTrigger = OnDamage, UniqueId = UniqueId };
        if (Template.IsDamageable)
        {
            prop.SetHitPoints(HitPoints);
        }
        return prop;
    }
}

/// <param name="Centre">Its box's centre.</param>
public sealed record ItemState(ItemTemplate Template, Vector3 Centre, bool Pinned, int OnPickup, bool IgnoresTractorPull, int UniqueId) : EntityState
{
    public override Entity Make(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var at = Matrix4x4.CreateTranslation(Centre - (Template.Bounds?.Centre ?? Vector3.Zero));
        var item = new WorldItem(world.Physics, new Item(Template), at) { OnPickupTrigger = OnPickup, IgnoresTractorPull = IgnoresTractorPull, UniqueId = UniqueId };
        item.IsPinned = Pinned;
        return item;
    }
}

/// <summary>A scene as a checkpoint keeps it: entered or not, its scripts (when entered), and its kept entities.</summary>
public sealed record SceneCheckpoint(bool FirstEntry, ScriptScene.Snapshot? Scripts, IReadOnlyList<EntityState> Entities);

/// <summary>
/// A level's checkpoint (§6.2): the scene it was taken in, the counters and timers, each scene, the
/// players' spawn points, the triggers it leaves out on loading, and the magicks the players had unlocked.
/// Writing it to disk goes with the saves (OM-55).
/// </summary>
public sealed record LevelCheckpoint(int Scene, ScriptLevel.Snapshot Level, IReadOnlyDictionary<int, SceneCheckpoint> Scenes,
    IReadOnlyList<Matrix4x4> SpawnPoints, IReadOnlyList<int> Ignored, ulong UnlockedMagicks);
