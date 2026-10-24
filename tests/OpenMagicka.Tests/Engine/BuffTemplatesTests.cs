using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Characters;
using Buffs = OpenMagicka.Gameplay.Buffs;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace OpenMagicka.Tests.Engine;

/// <summary>Buffs and auras from the data (ENTITIES_AI.md §8.3).</summary>
public class BuffTemplatesTests
{
    [Fact]
    public void ABuffAura_CarriesItsBuffAndTargets()
    {
        var content = new BuffAura(AuraTarget.TypeButSelf, BuffVisualCategory.Defensive, new XnaVector3(1, 2, 3), "glow", 6900000, 5, "goblin_a, goblin_b", 32,
            new ResistanceBuff(BuffVisualCategory.Defensive, XnaVector3.One, 1, "", new ResistanceContent(Elements.Fire, 0.25f, -5, false)));

        var aura = Assert.IsType<Buffs.BuffAura>(BuffTemplates.Aura(content));

        Assert.Equal((Buffs.AuraTarget.TypeButSelf, 6900000f, 5f), (aura.Target, aura.Ttl, aura.Radius));
        Assert.Equal(["goblin_a", " goblin_b"], aura.TargetTypes!);                  // spaces kept, as the original
        Assert.Equal(OpenMagicka.Gameplay.Characters.Factions.Human, aura.TargetFaction);
        var buff = Assert.IsType<Buffs.ResistanceBuff>(aura.Buff);
        Assert.Equal(new OpenMagicka.Gameplay.Combat.Resistance(OpenMagicka.Gameplay.Spells.Elements.Fire, 0.25f, -5), buff.Resistance);
    }

    [Fact]
    public void EveryBuffKind_IsRead()
    {
        var damage = new DamageContent(AttackProperties.Damage, Elements.Life, -75, 1);
        BuffContent[] kinds =
        [
            new BoostDamageBuff(BuffVisualCategory.None, default, 0.5f, "", damage), new DealDamageBuff(BuffVisualCategory.None, default, 1, "", damage),
            new UndyingBuff(BuffVisualCategory.None, default, 1, ""), new BoostBuff(BuffVisualCategory.None, default, 1, "", 2),
            new ReduceAggroBuff(BuffVisualCategory.None, default, 1, "", 0.1f),
            new ModifierBuff(BuffVisualCategory.None, default, 1, "", ModifierBuffKind.SpellRange, 1.5f, 0),
        ];

        Assert.Equal([typeof(Buffs.BoostDamageBuff), typeof(Buffs.DealDamageBuff), typeof(Buffs.UndyingBuff), typeof(Buffs.BoostBuff),
            typeof(Buffs.ReduceAggroBuff), typeof(Buffs.ModifierBuff)], kinds.Select(k => BuffTemplates.Buff(k).GetType()));
        Assert.Equal(Buffs.Modified.SpellRange, ((Buffs.ModifierBuff)BuffTemplates.Buff(kinds[^1])).Kind);
    }

    [Fact]
    public void ABadAura_IsBadData() =>
        Assert.Throws<InvalidDataException>(() => BuffTemplates.Aura(new DeflectAura(AuraTarget.Self, BuffVisualCategory.None, default, "", 1, -3, "", 0, 3)));
}
