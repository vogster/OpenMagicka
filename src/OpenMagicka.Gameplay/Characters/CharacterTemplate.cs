using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>The factions of docs/engine/ENTITIES_AI.md §3: characters with no shared bit are enemies.</summary>
[Flags]
public enum Factions
{
    None = 0,
    Evil = 0x1,
    Wild = 0x2,
    Friendly = 0x4,
    Demon = 0x8,
    Undead = 0x10,
    Human = 0x20,
    Wizard = 0x40,
    Neutral = 0xFF,
    Player0 = 0x100,
    Player1 = 0x200,
    Player2 = 0x400,
    Player3 = 0x800,
    TeamRed = 0x1000,
    TeamBlue = 0x2000,
    Players = 0x3F00,
}

/// <summary>
/// What a character type brings to its core (docs/engine/ENTITIES_AI.md §2–3): the subset of the
/// template's fields the rules use. The content reader's full template maps onto it.
/// </summary>
/// <param name="KnockdownTolerance">The least magnitude that knocks it down; exactly 5000 means never.</param>
/// <param name="ZapModifier">The speed multiplier for a moment after a lightning hit.</param>
public sealed record CharacterTemplate(
    string Id,
    Factions Factions,
    float MaxHitPoints,
    float Length,
    float Radius,
    float Mass,
    float Speed,
    float TurnSpeed,
    ResistanceTable Resistances,
    int Regeneration = 0,
    int HitTolerance = 0,
    float KnockdownTolerance = 0,
    float ZapModifier = 1,
    float BleedRate = 0,
    float StunTime = 0,
    bool Undying = false,
    float UndieTime = 0,
    float UndieHitPoints = 0,
    bool IsEthereal = false,
    bool Flies = false,
    IReadOnlyList<EquipmentTemplate>? Equipment = null,
    float ModelScale = 1)
{
    /// <summary>The knockdown tolerance that makes a character immune to falling over.</summary>
    public const float NoKnockdown = 5000;

    public bool CanBeKnockedDown => KnockdownTolerance != NoKnockdown;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Resistances);
        foreach (var held in Equipment ?? [])
        {
            ArgumentNullException.ThrowIfNull(held, nameof(Equipment));
            held.Validate();                                                   // before the body is in the world
        }
        if (!(MaxHitPoints > 0) || !float.IsFinite(MaxHitPoints))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHitPoints), MaxHitPoints, "must be positive");
        }
        if (!(ModelScale > 0) || !float.IsFinite(ModelScale))
        {
            throw new ArgumentOutOfRangeException(nameof(ModelScale), ModelScale, "must be positive");
        }
        foreach (var (value, name) in new[] { (KnockdownTolerance, nameof(KnockdownTolerance)), (BleedRate, nameof(BleedRate)),
            (StunTime, nameof(StunTime)), (UndieTime, nameof(UndieTime)), (UndieHitPoints, nameof(UndieHitPoints)), (TurnSpeed, nameof(TurnSpeed)) })
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");
            }
        }
    }

    /// <summary>Enemy resistances grow with the players (docs/engine/ENTITIES_AI.md §2.4); friends' do not.</summary>
    public ResistanceTable ResistancesFor(int players) =>
        Factions.HasFlag(Factions.Friendly) ? Resistances : Resistances.ScaledForPlayers(players);
}

/// <summary>A self-shield (ward, armour) that takes damage before the character does.</summary>
public interface ISelfShield
{
    bool IsActive { get; }

    /// <summary>A solid shield (armour) stops pushes.</summary>
    bool IsSolid { get; }

    /// <summary>Takes its share of an entry; it may change or empty the entry.</summary>
    DamageResult Absorb(Character owner, ref DamageEntry damage, Entity? attacker, Vector3 attackPosition);

    /// <summary>Takes its share of damage over time; returns what gets through.</summary>
    float AbsorbOverTime(float amount, Spells.Elements element);

    void Update(Character owner, float dt);
}
