using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>A character's self-cast shields and wards (docs/gameplay/SPELLS.md §5.6).</summary>
public sealed partial class Character
{
    /// <summary>Ice armour strikes around its wearer as it forms, this far.</summary>
    public const float IceArmourReach = ShieldRules.IceArmourBlastRadius;

    /// <summary>
    /// A self-shield from a Self cast with Shield: a bubble (casting it while one is up only takes it
    /// down) or armour. Either ends the self-cast auras first.
    /// </summary>
    public void AddSelfShield(Spell spell)
    {
        ClearSelfCastAuras();
        if (spell.Earth == 0 && spell.Ice == 0)
        {
            var hadBubble = SelfShield is { IsActive: true, IsBubble: true };
            RemoveSelfShield();
            if (!hadBubble)
            {
                SelfShield = new SelfShield(spell);
            }
            return;
        }
        RemoveSelfShield();
        SelfShield = new SelfShield(spell);
        SyncShieldMass();
        if (spell.Ice > 0 && Manager is { } manager)
        {
            var blast = new DamageEntry(AttackProperties.Damage | AttackProperties.Piercing, Elements.Earth, ShieldRules.IceArmourBlast, MathF.Max(1, spell.Ice + spell.Earth));
            Blasts.Arc(manager, this, this, Position, -System.Numerics.Vector3.UnitZ, IceArmourReach, MathF.PI, [blast]);
        }
    }

    /// <summary>Takes the self-shield down; armour gives the template's mass back.</summary>
    public void RemoveSelfShield()
    {
        SelfShield = null;
        SyncShieldMass();
    }

    /// <summary>Armour's mass while it is up, the template's otherwise; a change the world's step put off lands on the next frame.</summary>
    private void SyncShieldMass()
    {
        var mass = SelfShield is SelfShield { IsArmour: true } ? Spells.Effects.SelfShield.ArmourMass : Template.Mass;
        if (Body.Body.Mass != mass && !world.IsDisposed && !world.IsStepping)
        {
            Body.Body.SetMass(mass);
        }
    }

    /// <summary>An aura the character cast on itself: a later self-shield or ward ends it.</summary>
    public void AddSelfCastAura(Aura aura) => auras.Add(new ActiveAura(aura) { SelfCasted = true });

    /// <summary>Ends the self-cast auras at once.</summary>
    public void ClearSelfCastAuras() => auras.RemoveAll(a => a.SelfCasted);
}
