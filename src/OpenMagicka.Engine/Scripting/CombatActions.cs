using System.Numerics;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Gameplay.Spells;
using AttackProperties = OpenMagicka.Gameplay.Combat.AttackProperties;
using Elements = OpenMagicka.Gameplay.Spells.Elements;
using Factions = OpenMagicka.Gameplay.Characters.Factions;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// <c>kill id= | area= type= factions=</c>: the named entity, or the characters in an area by template or
/// faction, die; a character's need of an explicit kill is lifted first. A prop breaks; a dispenser dies; an
/// egg hatches as it would.
/// </summary>
public sealed class KillAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Area { get; set; } = "";

    protected override void Execute()
    {
        if (Id.Length > 0)
        {
            Kill(Scene.World.Entities.FindByUniqueId(Hash(Id)));
            return;
        }
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions))
        {
            Kill(character);
        }
    }

    internal static void Kill(Entity? entity)
    {
        switch (entity)
        {
            case Character character:
                character.CannotDieWithoutExplicitKill = false;
                character.Kill();
                break;
            case PhysicsProp prop:
                prop.Kill();
                break;
            case Dispenser dispenser:
                dispenser.Kill();
                break;
            case Gameplay.Spells.Magicks.ElementalEgg egg:
                egg.Kill();
                break;
        }
    }
}

/// <summary><c>overkill id= | area= type= factions=</c>: the named one, or the characters in an area, shatter (no corpse); a prop breaks.</summary>
public sealed class OverkillAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Area { get; set; } = "";

    protected override void Execute()
    {
        if (Id.Length > 0)
        {
            switch (Scene.World.Entities.FindByUniqueId(Hash(Id)))
            {
                case Character character:
                    character.OverKill();
                    break;
                case PhysicsProp { IsDamageable: true } prop:
                    prop.Kill();
                    break;
            }
            return;
        }
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions))
        {
            character.OverKill();
        }
    }
}

/// <summary>
/// <c>damage id= | area= type= factions= source= ignoreShields=</c> with <c>&lt;damage attackProperty= element=
/// amount= magnitude=/&gt;</c> children: each entry hits the named entity, or the characters in an area, from
/// the source locator (the origin without one). Unless shields are ignored (the default is to ignore
/// them), a character a shield covers is spared: one inside a sphere with no source, else one a shield
/// stands between it and the source. The element "physical" is Earth, as the original's.
/// </summary>
public sealed class DamageAction : ScriptAction
{
    /// <summary>The original's shields are crossed by a line 0.25 thick.</summary>
    public const float ShieldTolerance = 0.25f;

    private DamageEntry[] damages = [];

    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Area { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IgnoreShields { get; set; } = true;

    public override void Initialize() => damages = Node is { } node ? Read(node) : [];

    /// <summary>The <c>&lt;damage&gt;</c> children (any case), their attributes by name (any case); an unknown attribute is skipped.</summary>
    internal static DamageEntry[] Read(ScriptNode node) =>
    [
        .. node.Children.Where(c => c.Type == System.Xml.XmlNodeType.Element && c.Name.Equals("damage", StringComparison.OrdinalIgnoreCase))
            .Select(Entry),
    ];

    private static DamageEntry Entry(ScriptNode child)
    {
        var (attack, element, amount, magnitude) = (AttackProperties.None, Elements.None, 0f, 0f);
        try
        {
            foreach (var attribute in child.Attributes)
            {
                switch (attribute.Name.ToLowerInvariant())
                {
                    case "attackproperty":
                        attack = Enum.Parse<AttackProperties>(attribute.Value, ignoreCase: true);
                        break;
                    case "element":
                        element = attribute.Value.Equals("physical", StringComparison.OrdinalIgnoreCase) ? Elements.Earth
                            : Enum.Parse<Elements>(attribute.Value, ignoreCase: true);
                        break;
                    case "amount":
                        amount = float.Parse(attribute.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "magnitude":
                        magnitude = float.Parse(attribute.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                }
            }
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"<damage>: {e.Message}", e);
        }
        if (!float.IsFinite(amount) || !float.IsFinite(magnitude))
        {
            throw new InvalidDataException("<damage>: amount and magnitude must be finite");
        }
        return new DamageEntry(attack, element, amount, magnitude);
    }

    public IReadOnlyList<DamageEntry> Damages => damages;

    protected override void Execute()
    {
        var source = Source.Length > 0 && Scene.Places.LocatorTransform(Hash(Source)) is { } locator ? locator.Translation : Vector3.Zero;
        if (Id.Length > 0)
        {
            if (Scene.World.Entities.FindByUniqueId(Hash(Id)) is { } entity)
            {
                Hit(entity, source);
            }
            return;
        }
        var manager = Scene.World.Entities;
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions))
        {
            var shielded = !IgnoreShields && (Source.Length == 0 ? manager.IsProtectedByShield(character, out _)
                : manager.Shields.Any(s => s.Blocks(source, character.Position, ShieldTolerance, out _)));
            if (!shielded)
            {
                Hit(character, source);
            }
        }
    }

    private void Hit(Entity entity, Vector3 source)
    {
        foreach (var damage in damages)
        {
            entity.Damage(damage, null, source);
        }
    }
}

/// <summary>How setFaction changes the factions.</summary>
public enum OperationAction { Add, Remove, Set }

/// <summary>
/// <c>setFaction id= | area= type= faction= action=</c>: the named character (or <c>player1</c>..<c>player4</c>),
/// or those in an area of the template ("any": all; factions are not looked at), add, lose or take the
/// factions.
/// </summary>
public sealed class SetFactionAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";
    public Factions Faction { get; set; }
    public string Area { get; set; } = "";
    public OperationAction Action { get; set; }
    public string Type { get; set; } = "";

    protected override void Execute()
    {
        if (Id.Length > 0)
        {
            if (ScriptTargets.Named(Scene, Id) is { } named)
            {
                Change(named);
            }
            return;
        }
        foreach (var character in ScriptTargets.InArea(Scene, Area, Type, Factions.None))
        {
            Change(character);
        }
    }

    private void Change(Character character) => character.OwnFactions = Action switch
    {
        OperationAction.Add => character.OwnFactions | Faction,
        OperationAction.Remove => character.OwnFactions & ~Faction,
        _ => Faction,
    };
}

/// <summary><c>removeAllStatusEffects</c>: every character's statuses, vines, shields, fear, invisibility, spells, levitation and auras go.</summary>
public sealed class RemoveAllStatusEffectsAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute()
    {
        foreach (var character in Scene.World.Entities.Entities.OfType<Character>().ToList())
        {
            character.RemoveStatusEffects();
        }
    }
}

/// <summary>
/// <c>teleport id= target= effect= sound=</c>: the named character goes to the locator, onto the floor up to
/// 4 below (0.1 over it), turned its way; the effect shows where it left and where it came (the
/// teleport's appearing by default), the sound (the teleport's arrival) plays where it came. Other
/// entities wait for their bodies; a locator the scene lacks takes it to the origin, as the original.
/// </summary>
public sealed class TeleportAction : ScriptAction
{
    public const string DefaultEffect = "magick_teleport_appear", DefaultSound = "magick_teleportb";

    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Effect { get; set; } = "";
    public string Sound { get; set; } = "";
    public string Id { get; set; } = "";
    public string Target { get; set; } = "";

    protected override void Execute()
    {
        if (Id.Length == 0 || Scene.World.Entities.FindByUniqueId(Hash(Id)) is not Character character || Scene.World.Physics.IsStepping)
        {
            return;
        }
        var effect = Effect.Length > 0 ? Effect : DefaultEffect;
        Scene.Effects.Spawn(Scene.Effects.Generated(), effect, Matrix4x4.CreateTranslation(character.Position), 0);
        var locator = Scene.Places.LocatorTransform(Hash(Target)) ?? Matrix4x4.Identity;
        Scene.Effects.Spawn(Scene.Effects.Generated(), effect, locator, 0);
        var point = locator.Translation;
        if (Scene.World.Physics.CastSegment(point, point - Vector3.UnitY * 4, character.Body.Body, levelOnly: true) is { } floor)
        {
            point = floor.Position;
        }
        var body = character.Body;
        (body.Body.Position, body.Body.Velocity) = (point + Vector3.UnitY * (0.1f - body.HeightOffset), Vector3.Zero);
        body.Face(ScriptPlaces.Forward(locator));
        Scene.Stage.Request(new PlaySoundRequest(0, SoundBanks.Spells, Sound.Length > 0 ? Sound : DefaultSound, 1, body.Body.Position, 0, true));
    }
}
