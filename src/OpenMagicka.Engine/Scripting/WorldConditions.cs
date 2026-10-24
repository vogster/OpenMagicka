using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;
using Factions = OpenMagicka.Gameplay.Characters.Factions;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A condition on a trigger area (LEVELS_TRIGGERS.md §10). An area the scene does not have crashes
/// the original; here the condition does not hold, inverted or not.
/// </summary>
public abstract class AreaCondition : ScriptCondition
{
    public string Area { get; set; } = "";

    public override bool IsMet(Character? sender) => Scene.Places.Area(Hash(Area)) is not null && base.IsMet(sender);

    protected sealed override bool Holds(Character? sender) => Holds(Scene.Places.Area(Hash(Area))!);

    protected abstract bool Holds(ScriptArea area);
}

/// <summary>
/// <c>present area= type= nr= compareMethod= includeInvisible=</c>: how many of a template ("any": all)
/// are counted in an area. Not including the invisible takes off each invisible one of the template
/// present, the dead too, and none for "any" (as the original).
/// </summary>
public sealed class PresentCondition : AreaCondition
{
    public string Type { get; set; } = "";

    public int Nr { get; set; }

    public CompareMethod CompareMethod { get; set; }

    public bool IncludeInvisible { get; set; } = true;

    protected override bool Holds(ScriptArea area)
    {
        var count = Type == "any" ? area.Count : area.CountOf(Type);
        if (!IncludeInvisible)
        {
            count -= area.Characters.Count(c => c.IsInvisible && string.Equals(c.Template.Id, Type, StringComparison.OrdinalIgnoreCase));
        }
        return Compare(CompareMethod, count, Nr);
    }
}

/// <summary>
/// <c>factionPresent area= factions= nr= compareMethod= includeInvisible=</c>: how many are counted in an
/// area by faction, once for each of its factions among them. Not including the invisible takes off
/// each invisible one present, the dead too, once, if its faction now is among them (as the original).
/// </summary>
public sealed class FactionPresentCondition : AreaCondition
{
    public Factions Factions { get; set; }

    public int Nr { get; set; }

    public CompareMethod CompareMethod { get; set; }

    public bool IncludeInvisible { get; set; } = true;

    protected override bool Holds(ScriptArea area)
    {
        var count = area.CountOf(Factions);
        if (!IncludeInvisible)
        {
            count -= area.Characters.Count(c => c.IsInvisible && (c.Factions & Factions) != 0);
        }
        return Compare(CompareMethod, count, Nr);
    }
}

/// <summary><c>idPresent id= area=</c>: the entity of that name is in an area ("any" by default) and alive.</summary>
public sealed class IdPresentCondition : AreaCondition
{
    public IdPresentCondition() => Area = "any";

    public string Id { get; set; } = "";

    protected override bool Holds(ScriptArea area)
    {
        var id = Hash(Id);
        return area.Entities.FirstOrDefault(e => e.UniqueId == id) is { } entity && !entity.IsDead;
    }
}

/// <summary><c>physicsPresent area= nr= compareMethod= onlyDamagable=</c>: how many physics objects (only the breakable ones) are in an area.</summary>
public sealed class PhysicsPresentCondition : AreaCondition
{
    public PhysicsPresentCondition() => Area = "any";

    public bool OnlyDamagable { get; set; }

    public int Nr { get; set; }

    public CompareMethod CompareMethod { get; set; }

    protected override bool Holds(ScriptArea area) =>
        Compare(CompareMethod, area.Entities.Count(e => e.IsPhysicsObject && (!OnlyDamagable || e.IsDamageable)), Nr);
}

/// <summary><c>triggered trigger=</c>: the trigger (or point) has fired; one the scene does not have has not (the original crashes).</summary>
public sealed class TriggeredCondition : ScriptCondition
{
    public string Trigger { get; set; } = "";

    protected override bool Holds(Character? sender) => Scene.Find(Hash(Trigger))?.HasTriggered ?? false;
}

/// <summary>
/// <c>characterHealth id= compareMethod= health=</c>: the named character's share of its hit points (0
/// when dead or missing); equal is within 0.0001. A health outside 0..1 fails the loading.
/// </summary>
public sealed class CharacterHealthCondition : ScriptCondition
{
    private float health;

    public string Id { get; set; } = "";

    public CompareMethod CompareMethod { get; set; }

    public float Health
    {
        get => health;
        set => health = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "health must be between 0 and 1");
    }

    protected override bool Holds(Character? sender)
    {
        var share = Scene.World.Entities.FindByUniqueId(Hash(Id)) is Character { IsDead: false } character ? character.HitPoints / character.MaxHitPoints : 0;
        return CompareMethod switch
        {
            CompareMethod.Less => share < Health,
            CompareMethod.Equal => MathF.Abs(Health - share) < 0.0001f,
            CompareMethod.Greater => share > Health,
            _ => false,
        };
    }
}

/// <summary><c>interactorIs id=</c>: the character that set the trigger off has that name.</summary>
public sealed class InteractorIsCondition : ScriptCondition
{
    public string Id { get; set; } = "";

    protected override bool Holds(Character? sender) => sender is not null && sender.UniqueId == Hash(Id);
}

/// <summary>
/// <c>characterCasting id= castType= element=</c>: the named character is casting that way a spell with
/// the element in it. A combination of elements is never in a spell (the original asks for each alone).
/// </summary>
public sealed class CharacterCastingCondition : ScriptCondition
{
    public string Id { get; set; } = "";

    public CastType CastType { get; set; }

    public Elements Element { get; set; }

    protected override bool Holds(Character? sender) =>
        Scene.World.Entities.FindByUniqueId(Hash(Id)) is Character { CurrentSpell: { } cast }
        && cast.CastType == CastType && Magnitude(cast.Spell, Element) > float.Epsilon;

    private static float Magnitude(Spell spell, Elements element) => element switch
    {
        Elements.Earth => spell.Earth,
        Elements.Water => spell.Water,
        Elements.Cold => spell.Cold,
        Elements.Fire => spell.Fire,
        Elements.Lightning => spell.Lightning,
        Elements.Arcane => spell.Arcane,
        Elements.Life => spell.Life,
        Elements.Shield => spell.Shield,
        Elements.Ice => spell.Ice,
        Elements.Steam => spell.Steam,
        Elements.Poison => spell.Poison,
        _ => 0,
    };
}
