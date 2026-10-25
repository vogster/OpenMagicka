namespace OpenMagicka.Gameplay.Entities;

/// <summary>A scene change's taking out and bringing back of the entities (docs/formats/LEVELS_TRIGGERS.md §6.1).</summary>
public sealed partial class EntityManager
{
    /// <summary>
    /// Takes everything out as a scene is left (docs/formats/LEVELS_TRIGGERS.md §6.1), as the original:
    /// the players' avatars wait outside, and when <paramref name="keep"/> so do the scene's living
    /// NPCs (not summoned ones), its standing props, its pickable items and books, and its eggs; their
    /// bodies are suspended and they are handed back. The rest go as if removed.
    /// </summary>
    public (IReadOnlyList<Entity> Avatars, IReadOnlyList<Entity> Kept) Store(bool keep)
    {
        using var _ = Busy();
        var (avatars, kept) = (new List<Entity>(), new List<Entity>());
        var all = entities.ToList();
        entities.Clear();
        foreach (var entity in all)
        {
            var avatar = entity is Characters.Character { IsPlayer: true };
            if (avatar || (keep && !entity.IsRemovable && IsKept(entity)))
            {
                Withdraw(entity);
                (avatar ? avatars : kept).Add(entity);
            }
            else
            {
                Release(entity);
            }
        }
        grid.Rebuild(entities);
        return (avatars, kept);
    }

    /// <summary>Brings back an entity <see cref="Store"/> handed out: its bodies resume and it is added anew.</summary>
    public void Restore(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var body in entity.Bodies)
        {
            if (body.IsRemoved)
            {
                throw new InvalidOperationException("an entity whose body is gone cannot come back");
            }
            body.Resume();
        }
        Add(entity);
    }

    /// <summary>Lets go for good of an entity <see cref="Store"/> handed out (it never comes back): its bodies leave the world.</summary>
    public void Discard(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Manager is not null)
        {
            throw new InvalidOperationException("the entity is in a manager: remove it there");
        }
        entity.OnRemoved();
    }

    private static bool IsKept(Entity entity) => entity switch
    {
        Props.PhysicsProp => true,
        Characters.Character { IsSummoned: false } npc => npc.HitPoints > 0 || npc.IsUndying,
        Items.WorldItem { IsPickable: true } => true,
        Items.BookOfMagick => true,
        Spells.Magicks.ElementalEgg { Summoner: null } => true,
        _ => false,
    };

    /// <summary>Unregisters an entity that waits outside: its bodies are suspended, it hears no removal.</summary>
    private void Withdraw(Entity entity)
    {
        Unregister(entity);
        (entity.Manager, entity.Id) = (null, NetId.None);
        entity.OnWithdrawn();
        foreach (var body in entity.Bodies)
        {
            if (!body.IsRemoved)
            {
                body.Suspend();
            }
        }
    }
}
