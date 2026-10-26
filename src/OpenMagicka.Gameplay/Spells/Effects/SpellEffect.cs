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

    /// <summary>The spell's sounds this effect plays (stopped when it ends, or when it plays others).</summary>
    private IReadOnlyList<string> sounds = [];

    /// <summary>
    /// The spell's sounds for a type and a cast (<see cref="SpellSounds"/>), going with the caster, as the original's
    /// effects play them; the ones played before stop first. Those of an effect that ends at once play out
    /// (<paramref name="playOut"/>: an area spray's blast), the others stop with the effect.
    /// </summary>
    protected void PlaySounds(Character caster, SpellType type, CastType cast, bool playOut = false)
    {
        StopSounds(caster);
        var played = SpellSounds.For(Spell, type, cast);
        foreach (var (cue, magnitude) in played)
        {
            caster.RaiseCue(new Animation.SoundCue(cue, SpellMissiles.SpellsBank, caster.Position, caster.Body.Facing, magnitude) { Follow = true });
        }
        sounds = playOut ? [] : [.. played.Select(p => p.Cue)];
    }

    /// <summary>The effect's spell sounds stop (as authored: they fade).</summary>
    private void StopSounds(Character caster)
    {
        foreach (var cue in sounds)
        {
            caster.RaiseCue(new Animation.SoundStopCue(cue));
        }
        sounds = [];
    }

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
            StopSounds(caster);
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
