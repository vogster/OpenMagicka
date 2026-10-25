using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>What magicks do to a character's mind: fear, charm, confusion, and its faction (docs/gameplay/MAGICKS.md §6).</summary>
public sealed partial class Character
{
    /// <summary>Fear lasts this long from each scare.</summary>
    public const float FearTime = 5;

    private Factions? factions;
    private float fearTimer;
    private Character? fearedBy;
    private Vector3 fearPosition;
    private float charmTimer;
    private float confuseTimer;

    /// <summary>Its factions now: a charmed NPC is Friendly; else the template's, unless confusion took them.</summary>
    public Factions Factions => IsCharmed && !IsPlayer ? Factions.Friendly : factions ?? Template.Factions;

    /// <summary>
    /// Its own factions, as a script sets them (setFaction): the template's until then. A charm shows over
    /// them, and confusion's end gives back a summon's master's (or the template's).
    /// </summary>
    public Factions OwnFactions
    {
        get => factions ?? Template.Factions;
        set => factions = value;
    }

    public bool IsFeared => fearTimer > 0 && !Dead;

    /// <summary>What it flees: the one who scared it, else the spot.</summary>
    public Vector3 FearPosition => fearedBy?.Position ?? fearPosition;

    public bool IsCharmed => charmTimer > float.Epsilon;

    /// <summary>Who charmed it (its AI takes their side, OM-40).</summary>
    public Entity? CharmOwner { get; private set; }

    public bool IsConfused => confuseTimer > 0;

    /// <summary>Called up by a magick: it fights for its master.</summary>
    public bool IsSummoned => SummonMaster is not null;

    public Character? SummonMaster { get; private set; }

    /// <summary>Called up by a master: it takes the master's factions (its AI follows, OM-40).</summary>
    public void Summoned(Character master)
    {
        ArgumentNullException.ThrowIfNull(master);
        (SummonMaster, factions) = (master, master.Factions);
    }

    /// <summary>Where it was going when fear took over, given back when the fear is done.</summary>
    internal Vector3? WantedMovement { get; set; }

    /// <summary>The heading a panicking character wanders on, drifting at random.</summary>
    public float WanderAngle { get; internal set; }

    /// <summary>Scared away from a spot for 5 s; demons, the undead and the fearless take no fright.</summary>
    public void Fear(Vector3 from)
    {
        if (CanFear())
        {
            (fearTimer, fearedBy, fearPosition) = (FearTime, null, from);
        }
    }

    /// <summary>Scared away from a character for 5 s.</summary>
    public void Fear(Character by)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (CanFear())
        {
            (fearTimer, fearedBy) = (FearTime, by);
        }
    }

    public void RemoveFear() => (fearTimer, fearedBy) = (0, null);

    /// <summary>Charmed for a time (the uncharmable are not).</summary>
    public void Charm(Entity by, float seconds)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (!Template.Uncharmable)
        {
            (CharmOwner, charmTimer) = (by, seconds);
            Mind?.DropToIdle();                                               // a charmed mind starts over on its new side
        }
    }

    public void EndCharm() => (charmTimer, CharmOwner) = (0, null);

    /// <summary>
    /// Confused for a time: an NPC belongs to no faction until it ends; an avatar's controls are
    /// turned around (input, OM-31). A new confusion only starts the time over.
    /// </summary>
    public void Confuse(float seconds)
    {
        confuseTimer = seconds;
        if (!IsPlayer)
        {
            factions = Factions.None;
        }
    }

    public void EndConfusion()
    {
        confuseTimer = 0;
        factions = SummonMaster?.Factions;                                    // a summon keeps its master's side
    }

    /// <summary>
    /// Nullify on the character: its statuses go; its vines, self-shield, fear, charm, confusion,
    /// invisibility, spell and queue (true when any of these were there); then its levitation, haste,
    /// enchantment and its auras (the original spares those of a cross's bubble, which comes with
    /// that ability).
    /// </summary>
    internal bool Nullify()
    {
        StopStatuses(StatusEffects.Burning | StatusEffects.Wet | StatusEffects.Frozen | StatusEffects.Cold | StatusEffects.Poisoned
            | StatusEffects.Healing | StatusEffects.Greased | StatusEffects.Steamed);
        var undone = IsEntangled || SelfShield is not null || IsFeared || IsCharmed || IsConfused || IsInvisible || CurrentSpell is not null || SpellQueue.Count > 0
            || Spell.Element != Spells.Elements.None;
        ReleaseEntanglement();
        RemoveSelfShield();
        RemoveFear();
        EndCharm();
        EndConfusion();
        RemoveInvisibility();
        StopSpell();
        ResetSpell();                                                         // a combined spell still to go (a charge, a clip before its action)
        SpellQueue = SpellQueue.Cleared();
        StopLevitate();
        (hasteTime, enchantTime) = (0, 0);
        auras.Clear();
        if (IsSummoned)
        {
            Kill();                                                           // a summon is undone with the magick
            return true;
        }
        return undone;
    }

    /// <summary>
    /// A script's removeAllStatusEffects: its statuses, vines, self-shield, fear and invisibility go, its
    /// spell stops, its queue empties, its levitation ends and its auras go (charm, confusion, haste and
    /// enchantment stay, as the original).
    /// </summary>
    public void RemoveStatusEffects()
    {
        StopStatuses(StatusEffects.Burning | StatusEffects.Wet | StatusEffects.Frozen | StatusEffects.Cold | StatusEffects.Poisoned
            | StatusEffects.Healing | StatusEffects.Greased | StatusEffects.Steamed);
        ReleaseEntanglement();
        RemoveSelfShield();
        RemoveFear();
        RemoveInvisibility();
        StopSpell();
        SpellQueue = SpellQueue.Cleared();
        StopLevitate();
        auras.Clear();
    }

    private bool CanFear() => (Factions & (Factions.Demon | Factions.Undead)) == 0 && !Template.Fearless;

    /// <summary>The mind's timers, each frame.</summary>
    private void UpdateMind(float dt)
    {
        if (fearTimer > 0)
        {
            fearTimer -= dt;
            if (fearTimer <= 0)
            {
                fearedBy = null;
            }
        }
        if (IsCharmed)
        {
            charmTimer -= dt;
            if (charmTimer <= 0)
            {
                EndCharm();
            }
        }
        if (confuseTimer > 0)
        {
            confuseTimer -= dt;
            if (confuseTimer <= 0 || HitPoints <= 0)
            {
                EndConfusion();
            }
        }
    }
}
