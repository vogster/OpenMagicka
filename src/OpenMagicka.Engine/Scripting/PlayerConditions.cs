using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Scripting;

/// <summary>The conditions on the players (LEVELS_TRIGGERS.md §10): those of the world's game, playing, with an avatar.</summary>
public abstract class PlayerCondition : ScriptCondition
{
    protected IReadOnlyList<Player> Players => Scene.World.Session?.Players ?? [];

    /// <summary>The playing players' avatars, the dead too.</summary>
    protected IEnumerable<Character> Avatars => Players.Where(p => p.Playing && p.Avatar is not null).Select(p => p.Avatar!);
}

/// <summary>
/// <c>robeInUse name= player=</c>: player N (1–4; 0: any) plays with that robe (its avatar's template).
/// No name never holds; a player beyond the slots crashes the original, here it does not hold.
/// </summary>
public sealed class RobeInUseCondition : PlayerCondition
{
    public string Name { get; set; } = "";

    public int Player { get; set; }

    protected override bool Holds(Character? sender) =>
        Name.Length > 0 && Players.Any(p => (Player == 0 || p.Id == Player - 1) && p.Playing
            && string.Equals(p.Avatar?.Template.Id, Name, StringComparison.OrdinalIgnoreCase));
}

/// <summary><c>playerCount compareMethod= nr=</c>: how many players play.</summary>
public sealed class PlayerCountCondition : PlayerCondition
{
    public CompareMethod CompareMethod { get; set; }

    public int Nr { get; set; }

    protected override bool Holds(Character? sender) => Compare(CompareMethod, Players.Count(p => p.Playing), Nr);
}

/// <summary>
/// <c>playersHaveStatusEffect statusEffect=</c>: a player has a status, any of them: the attribute is
/// read but not asked about (as the original).
/// </summary>
public sealed class PlayersHaveStatusEffectCondition : PlayerCondition
{
    public StatusEffects StatusEffect { get; set; }

    protected override bool Holds(Character? sender) => Avatars.Any(a => a.Statuses.Active != StatusEffects.None);
}

/// <summary><c>playerHasElement element=</c>: a player has one of the elements in its queue (any of a combination, as the original).</summary>
public sealed class PlayerHasElementCondition : PlayerCondition
{
    public Elements Element { get; set; }

    protected override bool Holds(Character? sender) => Avatars.Any(a => a.SpellQueue.Entries.Any(e => (e.Element & Element) != 0));
}
