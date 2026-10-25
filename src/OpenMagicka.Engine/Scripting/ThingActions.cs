using System.Numerics;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// <c>playAnimation name= start= end= speed= loop= children= resume=</c>: an animated part (a path of
/// names, "parent/child") plays its clip between the times (−1: its ends). A part the level lacks
/// crashes the original; here nothing plays.
/// </summary>
public sealed class PlayAnimationAction : ScriptAction
{
    public string Name { get; set; } = "";
    public float Speed { get; set; } = 1;
    public float Start { get; set; } = -1;
    public float End { get; set; } = -1;
    public bool Loop { get; set; }
    public bool Children { get; set; } = true;
    public bool Resume { get; set; }

    protected override void Execute() => Scene.Places.Part(Name)?.Play(Children, Start, End, Speed, Loop, Resume);
}

/// <summary>
/// <c>spawnEffect effect= name= area= range=</c>: a visual effect at the place under a name (later
/// started and stopped by it); one playing under the name stays as it is. A range makes it a beam.
/// </summary>
public sealed class SpawnEffectAction : ScriptAction
{
    /// <summary>A replay spawns it again (the original: only an effect that is not one-shot; ours does not know the kind yet).</summary>
    public override void Replay() => Execute();

    public string Effect { get; set; } = "";
    public string Name { get; set; } = "";
    public string Area { get; set; } = "";
    public float Range { get; set; }

    protected override void Execute() => Scene.Effects.Spawn(Hash(Name), Effect, Scene.Places.Placement(Hash(Area), Scene.Random), Range);
}

/// <summary><c>startEffect name=</c>: a stopped named effect plays again. With no name it asks a new key, so it starts none (as the original).</summary>
public sealed class StartEffectAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Name { get; set; } = "";

    protected override void Execute() => Scene.Effects.Start(Name.Length == 0 ? Scene.Effects.Generated() : Hash(Name));
}

/// <summary><c>stopEffect name=</c>: the named effect (the level's or a spawned one) stops.</summary>
public sealed class StopEffectAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Name { get; set; } = "";

    protected override void Execute() => Scene.Effects.Stop(Hash(Name));
}

/// <summary>
/// <c>spawnItem item= area= id= onPickup= physicsEnabled= ignoreTractorPull= nr= attachTo=</c>: the item
/// <c>Data/Items/Wizard/&lt;item&gt;</c> lies at the place, turned with it: one, whatever <c>nr</c> says, as
/// the original. Its pick-up sets off <c>onPickup</c>; without physics it stays put. The original keeps
/// one item per action and moves it on each run (even out of a holder's hands); ours moves it while it
/// still lies in the level, else puts down a new one. Attaching to a part waits for the parts' world (OM-74).
/// </summary>
public sealed class SpawnItemAction : ScriptAction
{
    private WorldItem? item;

    public string Item { get; set; } = "";
    public string Area { get; set; } = "";
    public string Id { get; set; } = "";
    public string OnPickup { get; set; } = "";
    public bool PhysicsEnabled { get; set; }
    public bool IgnoreTractorPull { get; set; }
    public int Nr { get; set; } = 1;
    public string AttachTo { get; set; } = "";

    protected override void Execute()
    {
        var world = Scene.World;
        if (!Scene.IsHost || world.Physics.IsDisposed || world.Physics.IsStepping
            || Scene.Assets?.Item("data/items/wizard/" + Item) is not { Bounds: not null } template)
        {
            return;
        }
        var placement = Scene.Places.Placement(Hash(Area), Scene.Random);
        if (item is { Manager: not null, IsDead: false } lying)
        {
            lying.MoveTo(placement);
        }
        else if (world.Entities.Entities.Count < EntityManager.Capacity)
        {
            item = new WorldItem(world.Physics, new Item(template), placement);
            try
            {
                world.Entities.Add(item);
            }
            catch
            {
                world.Physics.Remove(item.Body);
                item = null;
                throw;
            }
        }
        if (item is null)
        {
            return;
        }
        (item.OnPickupTrigger, item.IgnoresTractorPull) = (OnPickup.Length > 0 ? Hash(OnPickup) : 0, IgnoreTractorPull);
        if (Id.Length > 0)
        {
            item.UniqueId = Hash(Id);
        }
        item.IsPinned = !PhysicsEnabled;
    }
}

/// <summary>
/// <c>spawnPhys object= area= id= onDeath= onDamage=</c>: the physics prop <c>Data/PhysicsEntities/&lt;object&gt;</c>
/// stands at the place, turned with it; a damageable one sets off its triggers (with no sender, as the
/// original). The props come from the running level's assets; with none, none is made.
/// </summary>
public sealed class SpawnPhysAction : ScriptAction
{
    public string Object { get; set; } = "";
    public string Area { get; set; } = "";
    public string Id { get; set; } = "";
    public string OnDeath { get; set; } = "";
    public string OnDamage { get; set; } = "";

    protected override void Execute()
    {
        var world = Scene.World;
        if (world.Physics.IsDisposed || world.Physics.IsStepping || world.Entities.Entities.Count >= EntityManager.Capacity
            || Scene.Assets?.Prop("data/physicsentities/" + Object) is not { } template)
        {
            return;
        }
        var prop = ScriptProps.Place(world, template, Scene.Places.Placement(Hash(Area), Scene.Random));
        if (Id.Length > 0)
        {
            prop.UniqueId = Hash(Id);
        }
        (prop.OnDeathTrigger, prop.OnDamageTrigger) = (OnDeath.Length > 0 ? Hash(OnDeath) : 0, OnDamage.Length > 0 ? Hash(OnDamage) : 0);
    }
}

/// <summary>Putting a physics prop in a world.</summary>
public static class ScriptProps
{
    public static Gameplay.Props.PhysicsProp Place(Gameplay.GameWorld world, Gameplay.Props.PropTemplate template, Matrix4x4 at)
    {
        ArgumentNullException.ThrowIfNull(world);
        var prop = new Gameplay.Props.PhysicsProp(world.Physics, template, at);
        try
        {
            world.Entities.Add(prop);
        }
        catch
        {
            world.Physics.Remove(prop.Body);
            throw;
        }
        return prop;
    }
}
