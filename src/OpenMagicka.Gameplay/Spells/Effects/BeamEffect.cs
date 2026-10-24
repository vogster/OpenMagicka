using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>An arcane or life beam: a forward beam, a blast, a self-cast or a blade arc (docs/gameplay/SPELLS.md §5.4).</summary>
public sealed class BeamEffect : SpellEffect
{
    /// <summary>How high the blade's arc reaches.</summary>
    public const float BladeHeight = 2;

    private float timeLeft;
    private bool selfDone;
    private IReadOnlyList<DamageEntry> damage = [];

    /// <summary>The forward cast's first beam.</summary>
    public Beam? Beam { get; private set; }

    protected override void Force(Character caster)
    {
        timeLeft = caster.Modify(Modified.SpellTtl, BeamRules.Lifetime(Spell));
        Beam = Beam.Fire(caster, Spell, SpellDamage.For(Spell, SpellType.Beam, CastType.Force));
    }

    protected override void Area(Character caster)
    {
        var sound = Spell.Arcane > 0 ? ("spell_arcane_area", Spell.Arcane) : ("spell_life_area", Spell.Life);
        caster.RaiseCue(new SoundCue(sound.Item1, SpellMissiles.SpellsBank, caster.Position, caster.Body.Facing, sound.Item2));
        if (caster.Manager is { } manager)
        {
            Blasts.Full(manager, caster.World, caster, caster, BeamRules.AreaRadius(Spell), caster.Position, SpellDamage.For(Spell, SpellType.Beam, CastType.Area));
        }
    }

    protected override void Self(Character caster)
    {
        timeLeft = 1;
        damage = SpellDamage.For(Spell, SpellType.Beam, CastType.Self);
    }

    protected override void Weapon(Character caster)
    {
        damage = SpellDamage.For(Spell, SpellType.Beam, CastType.Weapon);
        var radius = BeamRules.WeaponRadius(Spell);
        if (caster.Manager is not { } manager)
        {
            return;
        }
        foreach (var entity in manager.Query(caster.Position, radius, ignoreProtected: true))
        {
            if (entity.IsDamageable && entity.ArcIntersect(caster.Position, caster.Body.Facing, radius, BeamRules.WeaponHalfAngle, BladeHeight, out var at))
            {
                entity.Damage(damage, caster, at);
            }
        }
    }

    internal override bool Update(Character caster, float dt, out float turn)
    {
        turn = CastRules.TurnSpeed(SpellType.Beam);
        if (!IsActive)
        {
            return false;
        }
        if (CastType == CastType.Self)
        {
            if (!selfDone)
            {
                selfDone = true;
                caster.Damage(damage, caster, caster.Position);                 // on its first frame, as the original
            }
            if (caster.IsDead || (caster.IsPlayer && caster.CastType == CastType.None))
            {
                timeLeft = 0;
            }
        }
        timeLeft -= dt;
        Beam?.Aim(caster.CastSource, caster.Body.Facing);
        if (Beam is { IsDead: true } || (caster.CastType != CastType.Weapon && (timeLeft <= 0 || caster.IsDead || caster.CastType == CastType.None)))
        {
            Stop(caster);
            return false;
        }
        return base.Update(caster, dt, out _);
    }

    protected override void End(Character caster)
    {
        Beam?.Kill();
        Beam = null;
    }
}
