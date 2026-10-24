using OpenMagicka.Content.Magicka;
using Buffs = OpenMagicka.Gameplay.Buffs;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>Buffs and auras from the data as the rules need them (docs/engine/ENTITIES_AI.md §8.3).</summary>
public static class BuffTemplates
{
    public static Buffs.Aura Aura(AuraContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (target, visual, color) = ((Buffs.AuraTarget)content.Target, (Buffs.BuffVisual)content.Visual, Numerics(content.Color));
        Buffs.Aura aura = content switch
        {
            BuffAura a => new Buffs.BuffAura(target, a.Ttl, a.Radius, Buff(a.Buff), Types(a.TargetTypes), (Gameplay.Characters.Factions)a.TargetFaction,
                visual, color, a.Effect),
            DeflectAura a => new Buffs.DeflectAura(target, a.Ttl, a.Radius, a.Strength, visual, color, a.Effect),
            BoostAura a => new Buffs.BoostAura(target, a.Ttl, a.Radius, a.Magnitude, visual, color, a.Effect),
            LifeStealAura a => new Buffs.LifeStealAura(target, a.Ttl, a.Radius, a.Amount, visual, color, a.Effect),
            LoveAura a => new Buffs.LoveAura(target, a.Ttl, a.Radius, a.LoveRadius, a.LoveTtl, visual, color, a.Effect),
            _ => throw new InvalidDataException($"unknown aura {content.GetType().Name}"),
        };
        try
        {
            aura.Validate();
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
        return aura;
    }

    public static Buffs.Buff Buff(BuffContent content)
    {
        var buff = Make(content);
        try
        {
            buff.Validate();
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
        return buff;
    }

    private static Buffs.Buff Make(BuffContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (visual, color) = ((Buffs.BuffVisual)content.Visual, Numerics(content.Color));
        return content switch
        {
            BoostDamageBuff b => new Buffs.BoostDamageBuff(b.Ttl, ItemTemplates.Damage(b.Damage), visual, color, b.Effect),
            DealDamageBuff b => new Buffs.DealDamageBuff(b.Ttl, ItemTemplates.Damage(b.Damage), visual, color, b.Effect),
            ResistanceBuff b => new Buffs.ResistanceBuff(b.Ttl, new Gameplay.Combat.Resistance((Gameplay.Spells.Elements)(int)b.Resistance.Elements,
                b.Resistance.Multiplier, b.Resistance.Modifier, b.Resistance.StatusResistance), visual, color, b.Effect),
            UndyingBuff b => new Buffs.UndyingBuff(b.Ttl, visual, color, b.Effect),
            BoostBuff b => new Buffs.BoostBuff(b.Ttl, b.Amount, visual, color, b.Effect),
            ReduceAggroBuff b => new Buffs.ReduceAggroBuff(b.Ttl, b.Amount, visual, color, b.Effect),
            ModifierBuff b => new Buffs.ModifierBuff(b.Ttl, Kind(b.Kind), b.Multiplier, b.Modifier, visual, color, b.Effect),
            _ => throw new InvalidDataException($"unknown buff {content.GetType().Name}"),
        };
    }

    private static Buffs.Modified Kind(ModifierBuffKind kind) => kind switch
    {
        ModifierBuffKind.HitPoints => Buffs.Modified.HitPoints,
        ModifierBuffKind.SpellTtl => Buffs.Modified.SpellTtl,
        ModifierBuffKind.SpellRange => Buffs.Modified.SpellRange,
        _ => throw new InvalidDataException($"unknown modifier buff {kind}"),
    };

    /// <summary>The comma-separated template types of a Type aura, untrimmed as the original.</summary>
    private static string[] Types(string types) => types.Split(',');

    private static System.Numerics.Vector3 Numerics(Microsoft.Xna.Framework.Vector3 v) => new(v.X, v.Y, v.Z);
}
