using System.Numerics;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using Factions = OpenMagicka.Gameplay.Characters.Factions;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// The characters an action picks in an area (LEVELS_TRIGGERS.md §9): those present, the dead and the
/// players too, of the template ("any": all), <b>or</b> of one of the factions (their template's), or
/// with exactly those factions when <paramref name="exact"/>. An area the scene lacks picks none (the
/// original crashes).
/// </summary>
internal static class ScriptTargets
{
    public static IEnumerable<Character> InArea(ScriptScene scene, string area, string type, Factions factions, bool exact = false) =>
        scene.Places.Area(LegacyHash(area)) is { } found
            ? found.Characters.Where(c => type == "any" || string.Equals(c.Template.Id, type, StringComparison.OrdinalIgnoreCase)
                || (exact ? c.Template.Factions == factions : (c.Template.Factions & factions) != 0)).ToArray()
            : [];

    /// <summary>A player's avatar by "player1".."player4" (when playing), or null for any other name.</summary>
    public static Character? Player(ScriptScene scene, string name) =>
        name.Length == 7 && name.StartsWith("player", StringComparison.Ordinal) && name[6] is >= '1' and <= '4'
        && scene.World.Session?.Players.FirstOrDefault(p => p.Id == name[6] - '1') is { Playing: true, Avatar: { } avatar } ? avatar : null;

    /// <summary>A player's avatar by "player1".."player4", else the character of that script name.</summary>
    public static Character? Named(ScriptScene scene, string name) =>
        name is "player1" or "player2" or "player3" or "player4" ? Player(scene, name) : scene.World.Entities.FindByUniqueId(LegacyHash(name)) as Character;

    /// <summary>The players' avatars of the game, in the players' order (the playing ones with one).</summary>
    public static IEnumerable<Character> Avatars(ScriptScene scene) =>
        (scene.World.Session?.Players ?? []).Where(p => p.Playing && p.Avatar is not null).Select(p => p.Avatar!).ToArray();

    private static int LegacyHash(string name) => Content.LegacyStringHash.Lower(name);
}

/// <summary>
/// <c>spawn type= nr= area= …</c>: <c>nr</c> characters of <c>Data/Characters/&lt;type&gt;</c>, each at the
/// place (an area: a random point of it, turned a random way), on the floor below, with the order,
/// triggers, health, name, dialog and clips given; its children are the minds' events. A network client
/// spawns none (the host's message does, OM-59); a full level stops it. Drawing, camera and mesh
/// choices wait for the renderer.
/// </summary>
public class SpawnAction : ScriptAction
{
    private AiEvent[] events = [];

    public string Type { get; set; } = "";
    public int Nr { get; set; } = 1;
    public string Area { get; set; } = "";
    public bool SnapToNavMesh { get; set; }
    public string TargetArea { get; set; } = "";
    public float Speed { get; set; } = 1;
    public float Health { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Dialog { get; set; } = "";
    public Order Order { get; set; } = Order.Attack;
    public ReactTo ReactTo { get; set; } = ReactTo.Attack | ReactTo.Proximity;
    public Order Reaction { get; set; } = Order.Attack;
    public string Trigger { get; set; } = "";
    public bool NeverRemove { get; set; }
    public string Animation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
    public string SpawnAnimation { get; set; } = "";
    public string PriorityTarget { get; set; } = "";
    public int PriorityAbility { get; set; } = -1;
    public string OnDeath { get; set; } = "";
    public string OnDamage { get; set; } = "";
    public bool ForceDraw { get; set; }
    public int MeshId { get; set; } = -1;
    public bool ForceCamera { get; set; }
    public bool ForceNavMesh { get; set; }
    public bool CannotDieWithoutExplicitKill { get; set; }

    private readonly List<Character> spawned = [];
    private List<float> toRestore = [];

    /// <summary>The characters it made that still live (in the scene, or waiting outside it), with their hit points (what a checkpoint replays).</summary>
    public IEnumerable<(Character Character, float HitPoints)> Survivors =>
        spawned.Where(c => c.HitPoints > 0 && !c.Body.Body.IsRemoved).Select(c => (c, c.HitPoints));

    public override void Initialize()
    {
        if (!float.IsFinite(Health) || !float.IsFinite(Speed))
        {
            throw new InvalidDataException($"<spawn type=\"{Type}\">: health and speed must be finite");
        }
        events = Node is { } node ? ScriptEvents.Read(node, Scene.Places, Scene.Random) : [];
    }

    /// <summary>The survivors' hit points (what a checkpoint keeps of a spawn).</summary>
    protected override object? SaveTag() => Survivors.Select(s => s.HitPoints).Concat(toRestore).ToList();

    protected override void LoadTag(object? tag) => toRestore = tag is List<float> hitPoints ? [.. hitPoints] : [];

    /// <summary>A replay makes the survivors again, each with the hit points it had, as the original.</summary>
    public override void Replay()
    {
        var manager = Scene.World.Entities;
        spawned.Clear();
        if (!Scene.IsHost || manager.Bestiary?.Find("data/characters/" + Type) is not { } found)
        {
            return;
        }
        var (template, animations) = found;
        foreach (var hitPoints in toRestore)
        {
            if (manager.Entities.Count >= EntityManager.Capacity)
            {
                break;
            }
            var character = Make(template, animations);
            character.SetHealth(character.MaxHitPoints > 0 ? hitPoints / character.MaxHitPoints : 0);
            spawned.Add(character);
        }
        toRestore = [];
    }

    protected override void Execute()
    {
        var manager = Scene.World.Entities;
        if (!Scene.IsHost || manager.Bestiary?.Find("data/characters/" + Type) is not { } found)
        {
            return;
        }
        var (template, animations) = found;
        spawned.RemoveAll(c => c.HitPoints <= 0 || c.Body.Body.IsRemoved);
        for (var i = 0; i < Nr && manager.Entities.Count < EntityManager.Capacity; i++)
        {
            spawned.Add(Make(template, animations));
        }
    }

    private protected Character Make(CharacterTemplate template, Gameplay.Animation.CharacterAnimations? animations)
    {
        var placement = Scene.Places.Placement(Hash(Area), Scene.Random);
        var point = SnapToNavMesh ? Scene.World.Entities.Walkable(placement.Translation, template.MoveAbilities) : placement.Translation;
        var players = Math.Max(1, Scene.World.Session?.Players.Count(p => p.Playing) ?? 1);
        var character = new Character(template, Scene.World.Physics, Standing(point, template), players, random: new Random(Scene.Random.Next()),
            animations: animations);
        try
        {
            Scene.World.Entities.Add(character);
        }
        catch
        {
            Scene.World.Physics.Remove(character.Body.Body);
            throw;
        }
        if (Id.Length > 0)
        {
            character.UniqueId = Hash(Id);
        }
        Dress(character, placement);
        return character;
    }

    /// <summary>The body's centre for a ground point: on the floor up to 9 below (and 0.1 over it), as the original places a new character.</summary>
    private Vector3 Standing(Vector3 point, CharacterTemplate template)
    {
        var from = point + Vector3.UnitY;
        var floor = Scene.World.Physics.CastSegment(from, from - Vector3.UnitY * 10, levelOnly: true) is { } hit ? hit.Position + Vector3.UnitY * 0.1f : point;
        return floor + Vector3.UnitY * (template.Length / 2 + template.Radius);
    }

    private void Dress(Character character, Matrix4x4 placement)
    {
        character.SetHealth(Health);
        (character.OnDeathTrigger, character.OnDamageTrigger) = (OnDeath.Length > 0 ? Hash(OnDeath) : 0, OnDamage.Length > 0 ? Hash(OnDamage) : 0);
        character.Mind?.Give(new GiveOrderSpec(Order, ReactTo, Reaction, PriorityTarget.Length > 0 ? Hash(PriorityTarget) : 0, PriorityAbility,
            Trigger.Length > 0 ? Hash(Trigger) : 0, events, TargetArea.Length > 0 ? Hash(TargetArea) : 0, Speed), byName: false);
        character.Dialog = Dialog.Length > 0 ? Hash(Dialog) : 0;
        character.Body.Face(ScriptPlaces.Forward(placement));
        (character.RemoveAfterDeath, character.CannotDieWithoutExplicitKill) = (!NeverRemove, CannotDieWithoutExplicitKill);
        if (Animation is not ("" or "none"))
        {
            character.ForceAnimation(Animation);
        }
        var rises = SpawnAnimation is not ("" or "none" or "idle" or "idle_agg");
        if (rises)
        {
            character.RiseWith(SpawnAnimation);
        }
        if (IdleAnimation is not ("" or "none"))
        {
            character.SpecialIdleAnimation = IdleAnimation;
            if (!rises)
            {
                character.ForceAnimation(IdleAnimation);
            }
        }
    }
}

/// <summary>
/// <c>giveOrder</c>: a new order for the NPC of that name, or for the NPCs in an area by template, or
/// faction (exact faction with <c>explicit</c>); its children are the order's events. Only the named one
/// panics at once (as the original). Grips are let go when grips come (OM-33).
/// </summary>
public sealed class GiveOrderAction : ScriptAction
{
    /// <summary>A replay gives the order again, the areas looked at anew (as the original).</summary>
    public override void Replay() => ReplayOrder();

    private AiEvent[] events = [];

    public Order Order { get; set; }
    public ReactTo ReactTo { get; set; }
    public string Trigger { get; set; } = "";
    public Order Reaction { get; set; } = Order.Attack;
    public string Animation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Area { get; set; } = "";
    public string TargetArea { get; set; } = "";
    public float Speed { get; set; } = 1;
    public string PriorityTarget { get; set; } = "";
    public int PriorityAbility { get; set; } = -1;
    public bool Explicit { get; set; }

    public override void Initialize()
    {
        if (!float.IsFinite(Speed))
        {
            throw new InvalidDataException("<giveOrder>: speed must be finite");
        }
        events = Node is { } node ? ScriptEvents.Read(node, Scene.Places, Scene.Random) : [];
    }

    private void ReplayOrder()
    {
        Scene.Places.Refill(Scene.World.Entities);
        Execute();
    }

    protected override void Execute()
    {
        var spec = new GiveOrderSpec(Order, ReactTo, Reaction, PriorityTarget.Length > 0 ? Hash(PriorityTarget) : 0, PriorityAbility,
            Trigger.Length > 0 ? Hash(Trigger) : 0, events, TargetArea.Length > 0 ? Hash(TargetArea) : 0, Speed,
            Animation is "" or "none" ? null : Animation, IdleAnimation is "" or "none" ? null : IdleAnimation);
        if (Id.Length > 0)
        {
            if (Scene.World.Entities.FindByUniqueId(Hash(Id)) is Character { IsPlayer: false, Mind: { } mind })
            {
                mind.Give(spec, byName: true);
            }
            return;
        }
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions, Explicit))
        {
            if (character is { IsPlayer: false, Mind: { } mind })
            {
                mind.Give(spec, byName: false);
            }
        }
    }
}

/// <summary>
/// <c>avatarMove</c>: scripted events for the players' avatars. <c>&lt;Player1&gt;</c>..<c>&lt;Player4&gt;</c>
/// take that slot, or the next free one; <c>&lt;PlayerID name=&gt;</c> the slot of the avatar of that name
/// when the scene is built. Any other child fails the loading.
/// </summary>
public sealed class AvatarMoveAction : ScriptAction
{
    private readonly AiEvent[]?[] events = new AiEvent[]?[4];

    public override void Initialize()
    {
        Array.Clear(events);
        if (Node is not { } node)
        {
            return;
        }
        var children = node.Children.Where(c => c.Type == System.Xml.XmlNodeType.Element).ToArray();
        foreach (var child in children.Where(c => c.Name.StartsWith("playerid", StringComparison.OrdinalIgnoreCase)))
        {
            var named = Scene.World.Entities.FindByUniqueId(Hash(child.Attribute("name") ?? ""));
            if (named is not null && Scene.World.Session?.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, named)) is { Id: < 4 } player)
            {
                events[player.Id] = ScriptEvents.Read(child, Scene.Places, Scene.Random);
            }
        }
        foreach (var child in children.Where(c => !c.Name.StartsWith("playerid", StringComparison.OrdinalIgnoreCase)))
        {
            if (!child.Name.StartsWith("player", StringComparison.OrdinalIgnoreCase) || !char.IsDigit(child.Name[^1]))
            {
                throw new InvalidDataException($"<avatarMove>: \"{child.Name}\" is not Player1..Player4");
            }
            events[FreeSlot(child.Name[^1] - '1')] = ScriptEvents.Read(child, Scene.Places, Scene.Random);
        }
    }

    /// <summary>The slot, or the next free one after it (round again past the fourth), as the original.</summary>
    private int FreeSlot(int slot)
    {
        for (var tries = 0; tries <= 10 * 4; tries++, slot++)
        {
            slot = slot is > 3 or < 0 ? 0 : slot;
            if (events[slot] is null)
            {
                return slot;
            }
        }
        throw new InvalidDataException("<avatarMove>: more players than slots");
    }

    protected override void Execute()
    {
        foreach (var player in Scene.World.Session?.Players ?? [])
        {
            if (player.Id < 4 && player.Avatar is { } avatar && events[player.Id] is { } own)
            {
                avatar.PlayEvents(own);
            }
        }
    }
}

/// <summary>
/// <c>setPosition id= area= facingDirection=</c>: the named character (or <c>player1</c>..<c>player4</c>)
/// moves to the place, onto the floor up to 4 below it; it faces the place's way with
/// <c>facingDirection</c>, or when no floor was found (as the original). Other entities wait for their bodies.
/// </summary>
public sealed class SetPositionAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";
    public string Area { get; set; } = "";
    public bool FacingDirection { get; set; }

    protected override void Execute()
    {
        if (Id.Length == 0)
        {
            return;
        }
        if (ScriptTargets.Named(Scene, Id) is not { } target)
        {
            return;
        }
        var placement = Scene.Places.Placement(Hash(Area), Scene.Random);
        var point = placement.Translation;
        var from = point + Vector3.UnitY;
        var floor = Scene.World.Physics.CastSegment(from, from - Vector3.UnitY * 5, target.Body.Body, levelOnly: true);
        target.Body.Body.Velocity = Vector3.Zero;
        target.Body.Body.Position = floor is { } hit ? hit.Position - Vector3.UnitY * target.Body.HeightOffset : point;
        if (floor is null || FacingDirection)
        {
            target.Body.Face(ScriptPlaces.Forward(placement));
        }
    }
}

/// <summary>
/// <c>remove id= | area= type= factions= force=</c>: the named character, or those in an area by template
/// or faction (the players too), vanish at once, dead, with no corpse and no death trigger (as the
/// original). <c>force</c> first lifts a named one's need of an explicit kill (in an area it is not
/// looked at).
/// </summary>
public sealed class RemoveAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Area { get; set; } = "";
    public bool Force { get; set; }

    protected override void Execute()
    {
        if (Id.Length > 0)
        {
            if (Scene.World.Entities.FindByUniqueId(Hash(Id)) is Character named)
            {
                if (Force)
                {
                    named.CannotDieWithoutExplicitKill = false;
                }
                named.Terminate();
            }
            return;
        }
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions))
        {
            character.Terminate();
        }
    }
}
