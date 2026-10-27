using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Liquids;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>A character in water or lava (OM-88, docs/engine/ARCHITECTURE.md §7.5), as the original's.</summary>
public sealed partial class Character
{
    /// <summary>From this depth a liquid wets or burns (the original's footstep-in-water depth).</summary>
    public const float WadingDepth = 0.2f;

    /// <summary>Lava's burning: its damage a second, and how fast its magnitude grows with the depth.</summary>
    public const float LavaDps = 100, LavaBurnRate = 20;

    /// <summary>How long a drowned body stays.</summary>
    public const float DrownedCorpseTime = 10;

    /// <summary>It drowned: no rising again, its own death clip.</summary>
    public bool IsDrowning { get; private set; }

    /// <summary>
    /// A step's liquid, after the physics: from 0.2 deep, water wets it (as much as the depth, a second) and lava burns it
    /// (100 a second, its magnitude 20 × depth a second), unless an elemental self-shield (not an armour) keeps it off;
    /// the status rules do the rest (wetting puts fire out and washes grease off). Alive with the surface up to its
    /// middle, it drowns.
    /// </summary>
    internal void Wade(float dt)
    {
        if (!Body.IsInWater || !(dt > 0))
        {
            return;
        }
        var depth = Body.WaterDepth;
        var lava = Body.LiquidMaterial == Liquid.LavaMaterial;
        if (depth >= WadingDepth && SelfShield is not { IsActive: true, IsSolid: false } && !IsImmortal && !IsEthereal)
        {
            AddStatus(lava
                ? new StatusEffect(StatusEffects.Burning, LavaDps, LavaBurnRate * depth * dt)
                : new StatusEffect(StatusEffects.Wet, 0, depth * dt));
        }
        if (HitPoints > 0 && !Dead && depth >= Body.Shape.Radius + Body.Shape.Length / 2)
        {
            Drown(depth, lava);
        }
    }

    /// <summary>
    /// Drowned, whatever guards it (its hit points are taken, not damaged): it comes up to the surface and stays there,
    /// its stun, fear and charm over, the drowning's effect where it went down; it dies without rising again.
    /// </summary>
    private void Drown(float depth, bool lava)
    {
        IsDrowning = true;
        RemoveInvisibility();
        ReleaseEntanglement();
        HitPoints = 0;
        corpseTimer = DrownedCorpseTime;
        stunTimer = 0;
        fearTimer = 0;
        EndCharm();
        Body.Body.Position += new Vector3(0, depth, 0);
        Body.Body.Velocity = Vector3.Zero;
        Body.Body.AffectedByGravity = false;                                  // it floats where it drowned
        RaiseCue(new WorldEffectCue(lava ? "lava_drown" : "water_drown", Body.Body.Position + new Vector3(0, Body.HeightOffset, 0), Body.Facing));
        DieUnlessDying();
    }
}
