using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Items;

/// <summary>What held items do by being held (docs/engine/ENTITIES_AI.md §8.1.6).</summary>
public static class Passives
{
    /// <summary>How far the area abilities reach from the tip.</summary>
    public const float AreaRange = 7;

    /// <summary>How often the drain and the regeneration pulse.</summary>
    public const float Pulse = 0.25f;

    public const float Drain = 4;

    public const float Regeneration = 2;

    public const float MeleeBoost = 1.5f;

    /// <summary>Half the gap to the glow's target closes each second.</summary>
    public const float GlowRate = 0.5f;

    /// <summary>A Gungnir's missile.</summary>
    public const float GungnirRadius = 0.5f;

    public const string SteamEffect = "birch_steam";

    /// <summary>The holder's speed multiplier, times every held MoveSpeed item's value.</summary>
    internal static float Speed(Character owner, float multiplier)
    {
        foreach (var slot in owner.Equipment)
        {
            if (slot.Item is { IsAttached: true, Template.PassiveAbility: PassiveAbility.MoveSpeed } item)
            {
                multiplier *= item.Template.PassiveValue;
            }
        }
        return multiplier;
    }

    /// <summary>One held item's ability for this frame (its holder alive, the item placed).</summary>
    internal static void Run(Character owner, Item item, float dt)
    {
        switch (item.Template.PassiveAbility)
        {
            case PassiveAbility.AreaLifeDrain:
                item.PassiveTimer -= dt;
                if (item.PassiveTimer <= 0)
                {
                    item.PassiveTimer += Pulse;                               // on its beat
                    DrainAround(owner, item.Tip);
                }
                break;
            case PassiveAbility.AreaRegeneration:
                item.PassiveTimer -= dt;
                if (item.PassiveTimer <= 0)
                {
                    item.PassiveTimer = Pulse;                                // afresh, as the original
                    foreach (var ally in Around(owner, item.Tip, AreaRange).Where(c => Friends(owner, c)))
                    {
                        ally.Heal(Regeneration);
                    }
                }
                break;
            case PassiveAbility.EnhanceAllyMelee:
                foreach (var ally in Around(owner, item.Tip, AreaRange).Where(c => Friends(owner, c)))
                {
                    ally.BoostMelee(MeleeBoost);
                }
                break;
            case PassiveAbility.Glow:
                var foe = Around(owner, item.Tip, MathF.Max(0, item.Template.PassiveValue)).Any(c => !ReferenceEquals(c, owner) && !Friends(owner, c));
                item.Glow += ((foe ? 1 : 0) - item.Glow) * dt * GlowRate;
                break;
            case PassiveAbility.Gungner:
                item.IsVisible = !Flying(item);                                   // hidden while its spear flies, every frame as the original
                break;
            case PassiveAbility.BirchSteam when !item.PassiveEffect:
                item.PassiveEffect = true;
                owner.RaiseCue(new ItemEffectCue(SteamEffect, item.Tip, Vector3.UnitX, Follow: true, item.PassiveSource));
                break;
        }
    }

    /// <summary>Whether the item may throw now: a Gungnir one spear at a time, a Master Sword at full health.</summary>
    internal static bool MayThrow(Character owner, Item item) => item.Template.PassiveAbility switch
    {
        PassiveAbility.Gungner => !Flying(item),
        PassiveAbility.MasterSword => owner.HitPoints >= owner.MaxHitPoints,
        _ => true,
    };

    /// <summary>A Gungnir's spear is still out: alive and in the level.</summary>
    private static bool Flying(Item item) => item.Thrown is { IsDead: false, Manager: not null };

    private static void DrainAround(Character owner, Vector3 at)
    {
        foreach (var victim in Around(owner, at, AreaRange).Where(c => !ReferenceEquals(c, owner)))
        {
            victim.Hurt(Drain, Elements.Arcane);
            owner.Heal(Drain);
        }
    }

    /// <summary>The living characters within range of a point (a query drops the dead).</summary>
    private static List<Character> Around(Character owner, Vector3 at, float range) =>
        owner.Manager is { } manager ? [.. manager.Query(at, range, ignoreProtected: true).OfType<Character>()] : [];

    private static bool Friends(Character a, Character b) => (a.Template.Factions & b.Template.Factions) != 0;
}
