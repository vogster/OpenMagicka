using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>A character's buffs and auras (docs/engine/ENTITIES_AI.md §8.3).</summary>
public sealed partial class Character
{
    /// <summary>Which status a new resistance buff of an element scales at once.</summary>
    private static readonly (Elements Element, StatusEffects Status)[] Scaled =
    [
        (Elements.Water, StatusEffects.Wet), (Elements.Cold, StatusEffects.Cold), (Elements.Fire, StatusEffects.Burning),
        (Elements.Life, StatusEffects.Healing), (Elements.Steam, StatusEffects.Steamed), (Elements.Poison, StatusEffects.Poisoned),
    ];

    private readonly List<ActiveBuff> buffs = [];
    private readonly List<ActiveAura> auras = [];
    private bool undying;

    public IReadOnlyList<ActiveBuff> Buffs => buffs;

    public IReadOnlyList<ActiveAura> Auras => auras;

    /// <summary>The template's, or for a frame an undying buff's.</summary>
    public bool IsUndying => undying;

    /// <summary>Gives a buff; one the character has only gets its time back. The dead take none.</summary>
    /// <param name="giver">The aura's owner: a damage buff hits as it (else as the bearer).</param>
    public void AddBuff(Buff buff, Character? giver = null)
    {
        ArgumentNullException.ThrowIfNull(buff);
        for (var i = 0; i < buffs.Count; i++)
        {
            if (ReferenceEquals(buffs[i].Buff, buff))
            {
                buffs[i].Ttl = buff.Ttl;
                return;
            }
        }
        if (Dead || State == States.CharacterStates.Dead)
        {
            return;
        }
        buff.Validate();
        if (buff is ResistanceBuff resistance)
        {
            foreach (var (element, status) in Scaled)
            {
                if ((resistance.Resistance.Against & element) != 0 && Statuses.Has(status))
                {
                    Statuses = Statuses.Scaled(status, resistance.Resistance.Multiplier);
                }
            }
        }
        if (buff is ModifierBuff { Kind: Modified.HitPoints } hitPoints && !IsPlayer)
        {
            HitPoints *= hitPoints.Multiplier;
        }
        buffs.Add(new ActiveBuff(buff, giver));
        BuffsChanged();
    }

    /// <summary>Starts an aura around the character, until its time runs out.</summary>
    public void AddAura(Aura aura) => auras.Add(new ActiveAura(aura));

    /// <summary>Spells of these elements: (× multiplier, + bias) from the damage boosts that cover any of them.</summary>
    public (float Multiplier, float Bias) DamageBoost(Elements elements)
    {
        var (multiplier, bias) = (1f, 0f);
        foreach (var held in buffs)
        {
            if (held.Buff is BoostDamageBuff boost && (boost.Damage.Element & elements) != 0)
            {
                (multiplier, bias) = (multiplier * boost.Damage.Magnitude, bias + boost.Damage.Amount);
            }
        }
        return (multiplier, bias);
    }

    /// <summary>A hit as a character attacker deals it: its damage boosts for the hit's elements, amount × multiplier + bias, as the original.</summary>
    public static DamageEntry Boosted(DamageEntry damage, Entities.Entity? attacker)
    {
        if (attacker is not Character character)
        {
            return damage;
        }
        var (multiplier, bias) = character.DamageBoost(damage.Element);
        return multiplier == 1 && bias == 0 ? damage : damage with { Amount = damage.Amount * multiplier + bias };
    }

    /// <summary>A spell's time or range after the modifier buffs of that kind: × multipliers + modifiers.</summary>
    public float Modify(Modified kind, float value)
    {
        var (multiplier, modifier) = (1f, 0f);
        foreach (var held in buffs)
        {
            if (held.Buff is ModifierBuff m && m.Kind == kind)
            {
                (multiplier, modifier) = (multiplier * m.Multiplier, modifier + m.Modifier);
            }
        }
        return value * multiplier + modifier;
    }

    /// <summary>The aggro the AI feels for the character, as a multiplier.</summary>
    public float AggroMultiplier
    {
        get
        {
            var multiplier = 1f;
            for (var i = 0; i < buffs.Count; i++)
            {
                if (buffs[i].Buff is ReduceAggroBuff reduce)
                {
                    multiplier *= reduce.Amount;
                }
            }
            return multiplier;
        }
    }

    /// <summary>What a boost press counts for: its Boost auras', its slot-1 item's and its Boost buffs', each cut to a whole number.</summary>
    public int BoostFactor
    {
        get
        {
            var boosts = Boost(1, auras);
            if (HeldItem(ResistanceSlot) is { } item)
            {
                boosts = Boost(boosts, item.Auras);
            }
            for (var i = 0; i < buffs.Count; i++)
            {
                if (buffs[i].Buff is BoostBuff boost)
                {
                    boosts *= (int)boost.Amount;
                }
            }
            return boosts;

            static int Boost(int boosts, IReadOnlyList<ActiveAura> from)
            {
                for (var i = 0; i < from.Count; i++)
                {
                    if (from[i].Aura is BoostAura boost)
                    {
                        boosts *= (int)boost.Magnitude;
                    }
                }
                return boosts;
            }
        }
    }

    /// <summary>The character's own auras, at the start of its frame; those out of time go.</summary>
    private void RunAuras(float dt)
    {
        for (var i = 0; i < auras.Count; i++)
        {
            auras[i].Ttl -= dt;
            Run(auras[i], dt);
            if (auras[i].Ttl <= 0)
            {
                auras.RemoveAt(i--);
            }
        }
    }

    /// <summary>Held items' auras, at the end of the frame, for as long as they are held.</summary>
    private void RunItemAuras(float dt)
    {
        foreach (var slot in equipment)
        {
            var held = slot.Item is { IsAttached: true } item ? item.Auras : null;
            for (var i = 0; i < (held?.Count ?? 0); i++)
            {
                Run(held![i], dt);                                           // their time means nothing while held
            }
        }
    }

    /// <summary>
    /// The maximum and undying go back to the template's; then each buff with time left acts, in the order
    /// they came, and one out of time goes. As the original, a damage buff before a hit point buff hits
    /// while the maximum is still the template's.
    /// </summary>
    private void RunBuffs(float dt)
    {
        (undying, MaxHitPoints) = (Template.Undying, Template.MaxHitPoints);
        var changed = false;
        for (var i = 0; i < buffs.Count; i++)
        {
            var held = buffs[i];
            if (held.Ttl <= 0)
            {
                buffs.RemoveAt(i--);
                changed = true;
                continue;
            }
            held.Ttl -= dt;
            Act(held, dt);
        }
        if (changed)
        {
            BuffsChanged();
        }
    }

    private void Act(ActiveBuff held, float dt)
    {
        switch (held.Buff)
        {
            case UndyingBuff:
                undying = true;
                break;
            case ModifierBuff { Kind: Modified.HitPoints } m:
                MaxHitPoints = MaxHitPoints * m.Multiplier + m.Modifier;
                break;
            case DealDamageBuff damage:
                held.Interval -= dt;
                if (held.Interval <= 0)
                {
                    held.Interval = DealDamageBuff.Interval;
                    Damage(damage.Damage, held.Giver ?? this, Position);
                }
                break;
        }
    }

    /// <summary>An aura's pulses this frame; after the first, a Buff aura's pulses only renew, so one stands for them all.</summary>
    private void Run(ActiveAura active, float dt)
    {
        if (active.Aura is DeflectAura)
        {
            return;                                                           // with projectiles
        }
        active.Pulse -= dt;
        if (active.Pulse > 0)
        {
            return;
        }
        active.Pulse += (MathF.Floor(-active.Pulse / Aura.Period) + 1) * Aura.Period;
        if (active.Aura is BuffAura buffAura)
        {
            Give(buffAura);
        }
    }

    /// <summary>A Buff aura's pulse: its buff to whom it targets.</summary>
    private void Give(BuffAura aura)
    {
        if (aura.Target == AuraTarget.Self)
        {
            AddBuff(aura.Buff, this);
            return;
        }
        if (Manager is not { } manager)
        {
            return;
        }
        foreach (var entity in manager.Query(Position, aura.Radius, ignoreY: true))
        {
            if (entity is Character { IsEthereal: false } character && Targets(aura, character))
            {
                character.AddBuff(aura.Buff, this);
            }
        }
    }

    private bool Targets(BuffAura aura, Character other)
    {
        var self = ReferenceEquals(other, this);
        var friendly = (other.Factions & Factions) != 0;
        var faction = (other.Factions & aura.TargetFaction) != 0;
        return aura.Target switch
        {
            AuraTarget.All => true,
            AuraTarget.AllButSelf => !self,
            AuraTarget.Enemy => !friendly,
            AuraTarget.Friendly => friendly,
            AuraTarget.FriendlyButSelf => friendly && !self,
            AuraTarget.Type => Typed(aura, other),
            AuraTarget.TypeButSelf => !self && Typed(aura, other),
            AuraTarget.Faction => faction,
            AuraTarget.FactionButSelf => faction && !self,
            _ => false,
        };
    }

    private static bool Typed(BuffAura aura, Character other)
    {
        var types = aura.TargetTypes ?? [];
        for (var i = 0; i < types.Count; i++)
        {
            if (string.Equals(types[i], other.Template.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The resistance buffs join the defences.</summary>
    private void BuffsChanged()
    {
        var resistances = buffs.Select(b => b.Buff).OfType<ResistanceBuff>().Select(r => r.Resistance).ToList();
        Defences = Defences with { Buffs = resistances.Count > 0 ? resistances : null };
    }
}
