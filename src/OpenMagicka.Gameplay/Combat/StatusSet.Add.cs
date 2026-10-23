using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Combat;

public sealed partial class StatusSet
{
    /// <summary>
    /// Adds a status as an attack sets it, with the interactions of docs/gameplay/SPELLS.md §9: fire
    /// and water or cold put each other out, cold freezes the wet, grease feeds fire, healing cures
    /// poison and bleeding. The status then meets the resistance of its element.
    /// </summary>
    public StatusChange Add(StatusEffect effect, Defences defences)
    {
        ArgumentNullException.ThrowIfNull(defences);
        StatusEffect.Index(effect.Type);
        StatusEffect.Finite(effect.Dps, nameof(effect));
        StatusEffect.Finite(effect.Magnitude, nameof(effect));
        StatusEffect.Finite(effect.DamageRest, nameof(effect));
        if (!effect.IsActive)
        {
            return new StatusChange(this, StatusOutcome.None);
        }
        var change = new Change(Copy(), DryTimer, this);
        var (blocked, rejected, freezes) = effect.Type switch
        {
            StatusEffects.Burning => change.Burning(ref effect, defences),
            StatusEffects.Cold => change.Cold(ref effect, defences),
            StatusEffects.Wet => change.Wet(ref effect, defences),
            StatusEffects.Greased => change.Greased(ref effect, defences),
            StatusEffects.Steamed => change.Resisted(ref effect, defences, Elements.Steam, removes: StatusEffects.Cold),
            StatusEffects.Poisoned => change.Resisted(ref effect, defences, Elements.Poison),
            StatusEffects.Healing => change.Resisted(ref effect, defences, Elements.Life, removes: StatusEffects.Poisoned | StatusEffects.Bleeding),
            _ => (false, false, false),                                            // Frozen and Bleeding take as they are
        };
        if (!blocked && !rejected)
        {
            ref var slot = ref change.Slots[StatusEffect.Index(effect.Type)];
            slot += effect;
            if (!slot.IsActive)
            {
                slot = default;                                                    // resisted below zero: reported as added, as the original
            }
        }
        var outcome = !blocked && !rejected ? StatusOutcome.Added : blocked ? StatusOutcome.Removed : StatusOutcome.None;
        return new StatusChange(new StatusSet(change.Slots, change.DryTimer), outcome, freezes);
    }

    /// <summary>A status whose DPS or magnitude ends up zero after resistances does not take.</summary>
    private static bool IsZero(float value) => MathF.Abs(value) < float.Epsilon;

    /// <summary>The slots being changed, read against the set as it was.</summary>
    private sealed class Change(StatusEffect[] slots, float dryTimer, StatusSet before)
    {
        public StatusEffect[] Slots { get; } = slots;

        public float DryTimer { get; private set; } = dryTimer;

        private bool Has(StatusEffects type) => Slots[StatusEffect.Index(type)].IsActive;

        private void Remove(StatusEffects types)
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                if ((types & (StatusEffects)(1 << i)) != 0)
                {
                    Slots[i] = default;
                }
            }
        }

        private void StartDrying(bool restart)
        {
            if (restart || DryTimer <= 0)
            {
                DryTimer = DryingTime;
            }
        }

        public (bool Blocked, bool Rejected, bool Freezes) Burning(ref StatusEffect effect, Defences defences)
        {
            var blocked = false;
            if (Has(StatusEffects.Cold))
            {
                Remove(StatusEffects.Cold);
                blocked = true;
            }
            if (Has(StatusEffects.Wet))
            {
                Remove(StatusEffects.Wet);
                StartDrying(restart: false);
                blocked = true;
            }
            else if (Has(StatusEffects.Frozen))
            {
                ref var frozen = ref Slots[StatusEffect.Index(StatusEffects.Frozen)];
                frozen = frozen with { Magnitude = frozen.Magnitude - effect.Magnitude };
                if (!frozen.IsActive)
                {
                    frozen = default;
                    StartDrying(restart: false);
                }
                blocked = true;
            }
            if (Has(StatusEffects.Greased))
            {
                effect = effect with { Dps = effect.Dps * 4 };
                Remove(StatusEffects.Greased);
            }
            var (multiplier, modifier) = defences.ForStatus(Elements.Fire);
            effect = effect with { Dps = (effect.Dps + modifier) * multiplier };
            return (blocked, IsZero(effect.Dps) || IsZero(effect.Magnitude) || defences.ForbidsStatus(Elements.Fire), false);
        }

        public (bool Blocked, bool Rejected, bool Freezes) Cold(ref StatusEffect effect, Defences defences)
        {
            var blocked = false;
            if (Has(StatusEffects.Burning) || before.IsDrying)
            {
                if (Has(StatusEffects.Burning))
                {
                    Remove(StatusEffects.Burning);
                    StartDrying(restart: true);
                }
                blocked = true;
            }
            if (Has(StatusEffects.Wet) && !defences.ForbidsStatus(Elements.Ice))
            {
                Remove(StatusEffects.Wet | StatusEffects.Cold);
                Slots[StatusEffect.Index(StatusEffects.Frozen)] = StatusEffect.Create(StatusEffects.Frozen, 0, 1);
                return (true, false, true);
            }
            if (Has(StatusEffects.Frozen))
            {
                ref var frozen = ref Slots[StatusEffect.Index(StatusEffects.Frozen)];
                frozen = frozen with { Magnitude = 1 };
                return (true, false, false);
            }
            Resist(ref effect, defences, Elements.Cold);
            return (blocked, IsZero(effect.Magnitude) || defences.ForbidsStatus(Elements.Cold), false);
        }

        public (bool Blocked, bool Rejected, bool Freezes) Wet(ref StatusEffect effect, Defences defences)
        {
            var blocked = false;
            if (Has(StatusEffects.Burning) || before.IsDrying)
            {
                if (Has(StatusEffects.Burning))
                {
                    StartDrying(restart: true);
                }
                Remove(StatusEffects.Burning);
                blocked = true;
            }
            Remove(StatusEffects.Greased);
            Resist(ref effect, defences, Elements.Water);
            return (blocked, IsZero(effect.Magnitude) || defences.ForbidsStatus(Elements.Water), false);
        }

        public (bool Blocked, bool Rejected, bool Freezes) Greased(ref StatusEffect effect, Defences defences)
        {
            Remove(StatusEffects.Wet);
            Resist(ref effect, defences, Elements.Water);
            return (false, IsZero(effect.Dps) || IsZero(effect.Magnitude), false);       // no status resistance stops grease
        }

        public (bool Blocked, bool Rejected, bool Freezes) Resisted(ref StatusEffect effect, Defences defences, Elements element, StatusEffects removes = StatusEffects.None)
        {
            Remove(removes);
            Resist(ref effect, defences, element);
            return (false, IsZero(effect.Dps) || IsZero(effect.Magnitude) || defences.ForbidsStatus(element), false);
        }

        /// <summary>Cold, Wet, Greased, Steamed, Poisoned and Healing: DPS plus the modifier, magnitude times the multiplier.</summary>
        private static void Resist(ref StatusEffect effect, Defences defences, Elements element)
        {
            var (multiplier, modifier) = defences.ForStatus(element);
            effect = effect with { Dps = effect.Dps + modifier, Magnitude = effect.Magnitude * multiplier };
        }
    }
}
