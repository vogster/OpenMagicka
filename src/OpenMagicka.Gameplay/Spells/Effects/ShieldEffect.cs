using System.Numerics;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A spell with Shield: a disc, sphere or wall, or with other elements mines or barriers; on oneself
/// a bubble, armour or a ward (docs/gameplay/SPELLS.md §5.6).
/// </summary>
public sealed class ShieldEffect : SpellEffect
{
    public const float MinimumTime = 0.5f;

    public const float DiscHitPoints = 800, SphereHitPoints = 1000, WallHitPoints = 800;

    /// <summary>A disc stands this far behind its caster; a wall this far ahead, beyond the caster's radius.</summary>
    public const float DiscBack = ShieldRules.ShieldRadius - 2, WallAhead = ShieldRules.ShieldRadius + 2;

    /// <summary>How long a ward's immunity lasts after its aura last gave it (each pulse renews it).</summary>
    public const float WardBuffTime = 1;

    /// <summary>The ward's elements, in the original's order.</summary>
    private static readonly Elements[] WardOrder = [Elements.Lightning, Elements.Fire, Elements.Cold, Elements.Steam, Elements.Water, Elements.Life, Elements.Arcane];

    private bool cast;

    /// <summary>What the cast raised, if anything.</summary>
    public SpellShield? Shield { get; private set; }

    /// <summary>As the original, no shield cast stops for a released button: it lives its minimum or its clip.</summary>
    protected override bool IsContinuous => false;

    private Elements Others => Spell.Element & ~Elements.Shield;

    protected override void Force(Character caster)
    {
        (MinTime, cast) = (MinimumTime, true);
        if (Others == Elements.None)
        {
            Raise(caster, ShieldShape.Disc, caster.Position - caster.Body.Facing * DiscBack, DiscHitPoints);
            return;
        }
        Lay(caster, CastType.Force);
    }

    protected override void Area(Character caster)
    {
        (MinTime, cast) = (MinimumTime, true);
        if (Others == Elements.None)
        {
            Raise(caster, ShieldShape.Sphere, caster.Position, SphereHitPoints);
            return;
        }
        Lay(caster, CastType.Area);
    }

    protected override void Weapon(Character caster) => cast = false;   // the wall rises when the clip's action ends

    protected override void Self(Character caster)
    {
        (MinTime, cast) = (MinimumTime, true);
        var others = Others;
        if (others == Elements.None)
        {
            caster.AddSelfShield(Spell);
            return;
        }
        var armour = (others & ElementSets.Physical) != 0;
        if (armour)
        {
            caster.AddSelfShield(Spell);
        }
        if ((others & (Elements.Lightning | Elements.Arcane | Elements.Life | Elements.Steam | Elements.Water | Elements.Cold | Elements.Fire)) == 0)
        {
            return;
        }
        Ward(caster, others, armour);
    }

    /// <summary>One immunity aura per ward element, for everyone within 1.5·m, for 15 + 5·m s.</summary>
    private void Ward(Character caster, Elements others, bool armour)
    {
        var m = 1f;
        foreach (var element in Enumerable.Range(0, 11).Select(i => (Elements)(1 << i)).Where(e => (others & e) == e))
        {
            m = MathF.Max(m, Spell[element]);
        }
        if (!armour)
        {
            caster.RemoveSelfShield();
        }
        caster.ClearSelfCastAuras();
        foreach (var element in WardOrder.Where(e => (others & e) != 0))
        {
            var immunity = new ResistanceBuff(WardBuffTime, new Resistance(element, Multiplier: 0));
            caster.AddSelfCastAura(new BuffAura(AuraTarget.All, 15 + 5 * m, 1.5f * m, immunity));
        }
    }

    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Shield);
        MinTime -= dt;
        if (IsActive && ((caster.Animator?.HasFinished ?? false) || MinTime < 0))
        {
            Stop(caster);
        }
        return false;                                                         // as the original: the cast state watches IsActive
    }

    internal override void AnimationEnded(Character caster)
    {
        if (CastType != CastType.Weapon || cast)
        {
            return;
        }
        cast = true;
        if (Others == Elements.None)
        {
            Raise(caster, ShieldShape.Wall, caster.Position + caster.Body.Facing * (caster.Radius + WallAhead), WallHitPoints);
            return;
        }
        Lay(caster, CastType.Weapon);
    }

    /// <summary>Mines with Arcane or Life and no Earth or Ice; barriers otherwise.</summary>
    private void Lay(Character caster, CastType cast)
    {
        switch (ShieldRules.For(Spell, cast))
        {
            case Mines:
                ShieldLayouts.LayMines(caster, Spell, cast);
                break;
            case Barriers rules:
                ShieldLayouts.RaiseBarriers(caster, Spell, cast, rules);
                break;
        }
    }

    /// <summary>A weapon cast that never reached its clip's end raises its wall as it stops.</summary>
    protected override void End(Character caster)
    {
        if (!cast)
        {
            AnimationEnded(caster);
        }
    }

    private void Raise(Character caster, ShieldShape shape, Vector3 at, float hitPoints)
    {
        if (caster.Manager is not { } manager || caster.World.IsDisposed || manager.Entities.Count >= Entities.EntityManager.Capacity)
        {
            return;
        }
        Shield = new SpellShield(caster.World, caster, shape, at, caster.Body.Facing, ShieldRules.ShieldRadius, hitPoints);
        manager.Add(Shield);
    }
}
