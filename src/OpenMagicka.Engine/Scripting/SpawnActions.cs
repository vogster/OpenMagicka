using System.Numerics;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// <c>spawnDispenser model= area= types= amount= time= active= id=</c>: a dispenser at the locator, on the
/// floor up to 4 below it (1.75 over it), turned its way, letting out the kinds (a comma list of
/// <c>Data/Characters</c>) by their counts (a comma list) each <c>time</c> while active. A kind the game
/// lacks is let out as none (its count still goes down). A network client spawns none. A checkpoint does not keep a dispenser: a
/// replay makes it again, as the original.
/// </summary>
public sealed class SpawnDispenserAction : ScriptAction
{
    private string[] kinds = [];
    private int[] counts = [];

    public override void Replay() => Execute();

    public string Area { get; set; } = "";
    public Dispensers Model { get; set; }
    public string Types { get; set; } = "";
    public string Amount { get; set; } = "";
    public float Time { get; set; }
    public string Id { get; set; } = "";
    public bool Active { get; set; } = true;

    public override void Initialize()
    {
        kinds = [.. Types.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(t => "data/characters/" + t)];
        try
        {
            counts = [.. Amount.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(a => int.Parse(a, System.Globalization.CultureInfo.InvariantCulture))];
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new InvalidDataException($"<spawnDispenser amount=\"{Amount}\">: {e.Message}", e);
        }
        if (!float.IsFinite(Time))
        {
            throw new InvalidDataException("<spawnDispenser>: the time must be finite");
        }
    }

    protected override void Execute()
    {
        var manager = Scene.World.Entities;
        if (!Scene.IsHost || manager.Entities.Count >= EntityManager.Capacity || Scene.World.Physics.IsStepping)
        {
            return;
        }
        var locator = Scene.Places.Placement(Hash(Area), Scene.Random);
        var from = locator.Translation + Vector3.UnitY;
        var ground = Scene.World.Physics.CastSegment(from, from - Vector3.UnitY * 4, levelOnly: true) is { } floor ? floor.Position : locator.Translation;
        var dispenser = new Dispenser(Scene.World.Physics, Model, ground + Vector3.UnitY * Dispenser.Lift, ScriptPlaces.Forward(locator), kinds, counts, Time,
            Active, Scene.Random.Next());
        try
        {
            manager.Add(dispenser);
        }
        catch
        {
            Scene.World.Physics.Remove(dispenser.Body);
            throw;
        }
        if (Id.Length > 0)
        {
            dispenser.UniqueId = Hash(Id);
        }
    }
}

/// <summary>
/// <c>activateDispenser id= | type= area=</c>: the named dispenser starts letting its characters out. The
/// original's form by area looks in the area for the dispenser named by <c>id</c>, so without one it
/// starts none; as the original, ours does the same (the Mines' eggs it is used for stay shut).
/// </summary>
public sealed class ActivateDispenserAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";
    public Dispensers Type { get; set; }
    public string Area { get; set; } = "";
    public Order Order { get; set; }
    public ReactTo ReactTo { get; set; }

    protected override void Execute()
    {
        if (Id.Length > 0 && Scene.World.Entities.FindByUniqueId(Hash(Id)) is Dispenser dispenser)
        {
            dispenser.Activate();
        }
    }
}

/// <summary>
/// <c>spawnAnimProp object= area= id= animation= damageable= gravity=</c>: the prop of
/// <c>Data/AnimatedProps/&lt;object&gt;</c> at the locator. It takes hits only with <c>damageable="true"</c>,
/// and stays put unless <c>gravity="true"</c>. Its clip waits for the animated props' drawing (OM-75).
/// The original keeps one prop per action and puts it back afresh on each run; ours puts down a new one
/// once the last is gone.
/// </summary>
public sealed class SpawnAnimPropAction : ScriptAction
{
    private Gameplay.Props.PhysicsProp? last;

    public string Area { get; set; } = "";
    public string Object { get; set; } = "";
    public string OnDeath { get; set; } = "";
    public string OnDamage { get; set; } = "";
    public string Id { get; set; } = "";
    public string Animation { get; set; } = "";
    public string Damageable { get; set; } = "";
    public string Gravity { get; set; } = "";

    protected override void Execute()
    {
        var world = Scene.World;
        if (last is { IsDead: false, Manager: not null } || world.Physics.IsDisposed || world.Physics.IsStepping
            || world.Entities.Entities.Count >= EntityManager.Capacity || Scene.Assets?.Prop("data/animatedprops/" + Object) is not { } template)
        {
            return;
        }
        template = template with
        {
            MaxHitPoints = bool.TryParse(Damageable, out var damageable) && damageable ? template.MaxHitPoints : 0,
            Movable = bool.TryParse(Gravity, out var gravity) && gravity && template.Movable,
        };
        last = ScriptProps.Place(world, template, Scene.Places.Placement(Hash(Area), Scene.Random));
        if (Id.Length > 0)
        {
            last.UniqueId = Hash(Id);
        }
        (last.OnDeathTrigger, last.OnDamageTrigger) = (OnDeath.Length > 0 ? Hash(OnDeath) : 0, OnDamage.Length > 0 ? Hash(OnDamage) : 0);
    }
}

/// <summary>
/// <c>spawnCloneWizards area= id= order= reactTo= …</c>: for each living player's avatar, an NPC of its
/// template at the locator <c>area</c> and the player's number (0..3), named <c>id</c> (by default
/// "clonewizard") and the number, whole, with the order, triggers, dialog and clips given (as a spawn's);
/// its children are the minds' events. Its colour goes with the drawing. A network client spawns none.
/// As the original, a replay makes them all again.
/// </summary>
public sealed class SpawnCloneWizardsAction : ScriptAction
{
    private AiEvent[] events = [];

    public override void Replay() => Execute();

    public string Area { get; set; } = "";
    public string TargetArea { get; set; } = "";
    public float Speed { get; set; } = 1;
    public string Id { get; set; } = "clonewizard";
    public string Dialog { get; set; } = "";
    public Order Order { get; set; } = Order.Attack;
    public ReactTo ReactTo { get; set; } = ReactTo.Attack | ReactTo.Proximity;
    public Order Reaction { get; set; } = Order.Attack;
    public string Trigger { get; set; } = "";
    public bool NeverRemove { get; set; }
    public string IdleAnimation { get; set; } = "";
    public string SpawnAnimation { get; set; } = "";
    public string PriorityTarget { get; set; } = "";
    public int PriorityAbility { get; set; } = -1;
    public string OnDeath { get; set; } = "";
    public string OnDamage { get; set; } = "";

    public override void Initialize()
    {
        if (!float.IsFinite(Speed))
        {
            throw new InvalidDataException("<spawnCloneWizards>: speed must be finite");
        }
        events = Node is { } node ? ScriptEvents.Read(node, Scene.Places, Scene.Random) : [];
    }

    protected override void Execute()
    {
        var manager = Scene.World.Entities;
        foreach (var player in Scene.IsHost ? Scene.World.Session?.Players ?? [] : [])
        {
            if (player is not { Playing: true, Avatar: { IsDead: false } avatar, Id: < 4 } || manager.Entities.Count >= EntityManager.Capacity
                || Scene.World.Physics.IsStepping)
            {
                continue;
            }
            var locator = Scene.Places.LocatorTransform(Hash(Area + player.Id)) ?? Matrix4x4.Identity;
            var clone = new Character(avatar.Template, Scene.World.Physics, locator.Translation + Vector3.UnitY * (avatar.Template.Length / 2 + avatar.Template.Radius),
                random: new Random(Scene.Random.Next()), animations: avatar.Animator?.Animations);
            manager.Add(clone);
            clone.UniqueId = Hash(Id + player.Id);
            Dress(clone, locator);
        }
    }

    private void Dress(Character clone, Matrix4x4 locator)
    {
        clone.SetHealth(1);
        (clone.OnDeathTrigger, clone.OnDamageTrigger) = (OnDeath.Length > 0 ? Hash(OnDeath) : 0, OnDamage.Length > 0 ? Hash(OnDamage) : 0);
        clone.Mind?.Give(new GiveOrderSpec(Order, ReactTo, Reaction, PriorityTarget.Length > 0 ? Hash(PriorityTarget) : 0, PriorityAbility,
            Trigger.Length > 0 ? Hash(Trigger) : 0, events, TargetArea.Length > 0 ? Hash(TargetArea) : 0, Speed), byName: false);
        clone.Dialog = Dialog.Length > 0 ? Hash(Dialog) : 0;
        clone.Body.Face(ScriptPlaces.Forward(locator));
        clone.RemoveAfterDeath = !NeverRemove;
        var rises = SpawnAnimation is not ("" or "none" or "idle" or "idle_agg");
        if (rises)
        {
            clone.RiseWith(SpawnAnimation);
        }
        if (IdleAnimation is not ("" or "none"))
        {
            clone.SpecialIdleAnimation = IdleAnimation;
            if (!rises)
            {
                clone.ForceAnimation(IdleAnimation);
            }
        }
    }
}
