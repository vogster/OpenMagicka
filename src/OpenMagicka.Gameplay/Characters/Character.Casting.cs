using System.Numerics;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>A character's spells: its queue, its cast and the effect it runs (docs/gameplay/SPELLS.md §4.1).</summary>
public sealed partial class Character
{
    private CastType castType;
    private SpellEffect? currentSpell;
    private float spellPower;

    /// <summary>The share of its turn speed the cast leaves it (§4); 1 when not casting.</summary>
    private float castTurn = 1;

    /// <summary>This cast already cast (or imbued): a later cast clip action does nothing until the next cast.</summary>
    private bool spent;

    /// <summary>The conjured elements, waiting to be cast.</summary>
    public ElementQueue SpellQueue { get; private set; } = ElementQueue.Empty;

    /// <summary>The spell the queue was combined into for this cast.</summary>
    public Spell Spell { get; private set; }

    /// <summary>The cast under way; setting Force, Area or Self (a change) combines the queue into the spell.</summary>
    public CastType CastType
    {
        get => castType;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "unknown cast type");
            }
            if (value is not (CastType.Weapon or CastType.None) && value != castType)
            {
                CombineSpell();
            }
            if (value != CastType.None && value != castType)
            {
                spent = false;                                                // a new cast
            }
            castType = value;
        }
    }

    /// <summary>How charged the cast is, 0 to 1.</summary>
    public float SpellPower
    {
        get => spellPower;
        set => spellPower = float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(SpellPower), value, "must be finite");
    }

    /// <summary>The spell effect it runs; dropping it ends the cast type too.</summary>
    public SpellEffect? CurrentSpell
    {
        get => currentSpell;
        internal set
        {
            currentSpell = value;
            if (value is null)
            {
                castType = CastType.None;
            }
        }
    }

    /// <summary>The cast button an avatar holds.</summary>
    public CastType HeldCast { get; private set; }

    /// <summary>An avatar may charge; a charge locks it until the Force button is let go.</summary>
    public bool ChargeUnlocked { get; internal set; } = true;

    /// <summary>Added to an area push's radius (a special ability's).</summary>
    public float AdditionalPushRadius { get; set; }

    /// <summary>Where weapon spells strike from: the slot-0 weapon's tip, else the character.</summary>
    public Vector3 WeaponSource => HeldItem(0) is { } weapon ? weapon.Tip : Position;

    /// <summary>Where spells leave from: the spell source's tip, else the character.</summary>
    public Vector3 CastSource => SpellSource >= 0 && HeldItem(SpellSource) is { } source ? source.Tip : Position;

    /// <summary>
    /// Adds an element to the queue. As the original's avatar, lightning while wet (without the
    /// WetLightning passive) or fire while greased is not added: it hurts the caster instead, at half.
    /// </summary>
    public bool Conjure(Elements element)
    {
        if (Dead || State == CharacterStates.Charge)
        {
            return false;                                                     // a charge ignores the elements
        }
        if (IsPlayer && ((element == Elements.Lightning && Statuses.Has(StatusEffects.Wet) && !HasPassiveAbility(PassiveAbility.WetLightning))
            || (element == Elements.Fire && Statuses.Has(StatusEffects.Greased))))
        {
            foreach (var entry in SpellDamage.For(Spell.Of(element), SpellType.Lightning, CastType.Self))
            {
                Damage(entry with { Magnitude = entry.Magnitude * 0.5f }, this, Position);
            }
            return false;
        }
        (SpellQueue, var added) = SpellQueue.Conjure(element);
        return added;
    }

    /// <summary>
    /// An avatar presses a cast button (docs/gameplay/SPELLS.md §4.1); true when it began a cast.
    /// Self with an empty queue uses the spell source's special ability instead.
    /// </summary>
    public bool PressCast(CastType cast)
    {
        if (Dead)
        {
            return false;
        }
        switch (cast)
        {
            case CastType.Force or CastType.Area:
                if (ItemIn(ResistanceSlot)?.WeaponClass != WeaponClass.Staff || HeldCast != CastType.None || CastType != CastType.None)
                {
                    return false;
                }
                (CastType, HeldCast) = (cast, cast);
                return true;
            case CastType.Self when SpellQueue.Count == 0:
                if (State != CharacterStates.Cast && State != CharacterStates.Charge && SpellSource >= 0)
                {
                    UseSpecialAbility(SpellSource);
                }
                return false;
            case CastType.Self:
                if (CastType != CastType.None)
                {
                    return false;
                }
                (HeldCast, CastType) = (CastType.Self, CastType.Self);
                if (Statuses.Has(StatusEffects.Frozen) || State == CharacterStates.Flying || State == CharacterStates.Pushed || State == CharacterStates.KnockDown)
                {
                    CastSpell(fromStaff: true);                               // its state will not get to it
                }
                return true;
            case CastType.Weapon:
                var charged = ItemIn(0)?.IsSpellCharged == true;
                if (!(SpellQueue.Count > 0 && SpellQueue.Entries[0].Magick == MagickType.None) && !charged || CastType != CastType.None)
                {
                    return false;
                }
                (HeldCast, CastType) = (CastType.Weapon, CastType.Weapon);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(cast), cast, "not a button");
        }
    }

    /// <summary>An avatar lets go of a cast button.</summary>
    public void ReleaseCast(CastType cast)
    {
        if (cast == CastType.Force)
        {
            ChargeUnlocked = true;
        }
        if (HeldCast == cast && (cast != CastType.Weapon || CastType == CastType.None))
        {
            HeldCast = CastType.None;
        }
    }

    /// <summary>
    /// Casts the combined spell now, unless an effect runs (the <c>CastSpell</c> action, the end of a
    /// charge): an avatar's Weapon cast imbues the weapon or casts what it holds; a magick runs its effect.
    /// </summary>
    public void CastSpell(bool fromStaff, string? source = null)
    {
        if (CurrentSpell is not null || spent)
        {
            return;
        }
        spent = true;
        if (Spell.Element == Elements.All)
        {
            CastMagick();
            return;
        }
        if (!IsPlayer && CastType == CastType.Weapon)
        {
            CombineSpell();                                                   // a non-player's weapon cast casts its queue at once
            Cast(Spell, CastType.Weapon, fromStaff);
            Spell = default;
            return;
        }
        if (IsPlayer && CastType == CastType.Weapon)
        {
            if (ItemIn(0) is not { } weapon)
            {
                return;
            }
            if (weapon.IsSpellCharged && SpellQueue.Count == 0)
            {
                Cast(weapon.RetrieveSpell(), CastType.Weapon, fromStaff);
            }
            else
            {
                (SpellQueue, weapon.Imbued) = CastRules.Imbue(SpellQueue, weapon.Imbued);
            }
            return;
        }
        Cast(Spell, CastType, fromStaff);
        if (IsEntangled && CastType == CastType.Self)
        {
            DecreaseEntanglement(0, Spell.Element);
        }
        Spell = default;
    }

    /// <summary>Lets go of a non-Force/Area spell entirely (a reset).</summary>
    public void ResetSpell() => Spell = default;

    /// <summary>Lets go of the elements chanted (an NPC that waited too long with them).</summary>
    public void ClearQueue() => SpellQueue = SpellQueue.Cleared();

    private void CombineSpell() => (Spell, SpellQueue) = (SpellQueue.Combine(), SpellQueue.Cleared());

    private void Cast(Spell spell, CastType cast, bool fromStaff)
    {
        if (cast == CastType.None || SpellEffects.For(SpellTypes.Of(spell.Element)) is not { } effect)
        {
            return;
        }
        CurrentSpell = effect;
        effect.Start(this, spell, cast, fromStaff);
    }

    /// <summary>The running effect's frame, after the state's; an avatar lets go of buttons it no longer casts with.</summary>
    private void UpdateCasting(float dt)
    {
        if (CurrentSpell is { } effect)
        {
            if (!effect.Update(this, dt, out var turn) && effect.CastType == CastType.Weapon && !effect.IsActive && State != CharacterStates.Cast)
            {
                CurrentSpell = null;
            }
            castTurn = turn;
        }
        else if (State != CharacterStates.Charge)
        {
            castTurn = 1;
        }
        if (IsPlayer && CurrentSpell is null && CastType == CastType.None && HeldCast is CastType.Self or CastType.Weapon)
        {
            HeldCast = CastType.None;
        }
    }

    /// <summary>The charge slows turning (§4).</summary>
    internal void SlowForCharge() => castTurn = CastRules.ChargeTurnSpeed;

    /// <summary>The running effect stops and is dropped (an attack chaining or ending, a cast ending).</summary>
    internal void StopSpell()
    {
        if (CurrentSpell is { } effect)
        {
            effect.Stop(this);
            CurrentSpell = null;
        }
    }
}
