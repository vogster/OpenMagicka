using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Spells.Effects;

/// <summary>
/// A spell carried out in the world, from its cast to its end (docs/gameplay/SPELLS.md §4.1). One
/// per cast: a simulation object that changes in place.
/// </summary>
public abstract class SpellEffect
{
    public Spell Spell { get; private set; }

    public CastType CastType { get; private set; }

    /// <summary>Cast from the staff (else from a bone).</summary>
    public bool FromStaff { get; protected set; }

    /// <summary>Still running; a stopped effect ends the caster's cast state.</summary>
    public bool IsActive { get; protected set; }

    /// <summary>Time left of its minimum and its maximum life.</summary>
    protected float MinTime { get; set; }

    protected float MaxTime { get; set; }

    /// <summary>A continuous effect goes on while the button is held (all but projectiles).</summary>
    protected virtual bool IsContinuous => true;

    /// <summary>Starts the effect for a cast (Force, Area, Self or Weapon).</summary>
    internal void Start(Character caster, Spell spell, CastType cast, bool fromStaff)
    {
        (Spell, CastType, FromStaff, IsActive) = (spell, cast, fromStaff, true);
        (MinTime, MaxTime) = CastRules.EffectLifetime(cast);
        switch (cast)
        {
            case CastType.Force:
                Force(caster);
                break;
            case CastType.Area:
                Area(caster);
                break;
            case CastType.Self:
                Self(caster);
                break;
            case CastType.Weapon:
                Weapon(caster);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cast), cast, "no effect for this cast");
        }
    }

    protected virtual void Force(Character caster)
    {
    }

    protected virtual void Area(Character caster)
    {
    }

    protected virtual void Self(Character caster)
    {
    }

    protected virtual void Weapon(Character caster)
    {
    }

    /// <summary>
    /// The effect's frame: false once it stopped. The base rules: a continuous effect stops when an
    /// avatar lets go after its minimum time or no longer casts, or when the caster is dead.
    /// </summary>
    internal virtual bool Update(Character caster, float dt, out float turn)
    {
        turn = 0.75f;
        MinTime -= dt;
        MaxTime -= dt;
        var player = caster.IsPlayer;
        if (IsActive && IsContinuous && MinTime <= 0 && player && caster.HeldCast == CastType.None)
        {
            Stop(caster);
            return false;
        }
        if (!IsActive || caster.IsDead || (player && caster.CastType == CastType.None && IsContinuous))
        {
            Stop(caster);
            return false;
        }
        return true;
    }

    /// <summary>Ends the effect now.</summary>
    internal void Stop(Character caster)
    {
        if (IsActive)
        {
            IsActive = false;
            End(caster);
        }
    }

    /// <summary>What ending leaves behind (its cues, its entities).</summary>
    protected virtual void End(Character caster)
    {
    }

    /// <summary>The cast clip's <c>CastSpell</c> action ended.</summary>
    internal virtual void AnimationEnded(Character caster)
    {
    }
}

/// <summary>The effect for each spell type, as they arrive (docs/gameplay/SPELLS.md §4.1, §5).</summary>
public static class SpellEffects
{
    /// <summary>A new effect for the type; null for a type with none yet.</summary>
    public static SpellEffect? For(SpellType type) => type switch
    {
        SpellType.Push => new PushEffect(),
        SpellType.Spray => new SprayEffect(),
        SpellType.Projectile => new ProjectileEffect(),
        SpellType.Lightning => new LightningEffect(),
        SpellType.Beam => new BeamEffect(),
        SpellType.Shield => new ShieldEffect(),
        _ => null,
    };
}
