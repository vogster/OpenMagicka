using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>Held items' passive abilities at work on their holder (docs/engine/ENTITIES_AI.md §8.1.6).</summary>
public sealed partial class Character
{
    /// <summary>How long a melee boost lasts after it was last given.</summary>
    public const float MeleeBoostTime = 5;

    private float meleeBoostTimer;

    /// <summary>What a boosted character's item damage is multiplied by; 0 when not boosted.</summary>
    public float MeleeBoostAmount { get; private set; }

    public bool IsMeleeBoosted => meleeBoostTimer > 0;

    /// <summary>Its item damage is multiplied by <paramref name="amount"/> for the next 5 s.</summary>
    public void BoostMelee(float amount)
    {
        if (!float.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "must be finite");
        }
        (MeleeBoostAmount, meleeBoostTimer) = (amount, MeleeBoostTime);
    }

    /// <summary>An item's damage as this holder deals it: boosted while a melee boost lasts.</summary>
    public DamageEntry MeleeBoosted(DamageEntry damage) =>
        IsMeleeBoosted ? damage with { Amount = damage.Amount * MeleeBoostAmount } : damage;

    /// <summary>Plain healing (no resistances), as a passive's: the immortal take none, as the original.</summary>
    internal void Heal(float amount)
    {
        if (IsImmortal || Dead)
        {
            return;
        }
        RemoveInvisibility();
        HitPoints += amount;
    }

    /// <summary>Plain damage (no resistances), as a status's over time.</summary>
    internal void Hurt(float amount, Elements element) => DamageOverTime(amount, element);

    private void CountDownMeleeBoost(float dt)
    {
        meleeBoostTimer -= dt;
        if (meleeBoostTimer <= 0)
        {
            MeleeBoostAmount = 0;
        }
    }

    /// <summary>Each held item's ability, at the end of the frame once the items moved.</summary>
    private void RunPassives(float dt)
    {
        foreach (var slot in equipment)
        {
            if (slot.Item is { IsAttached: true } item)
            {
                Passives.Run(this, item, dt);
            }
        }
    }
}
