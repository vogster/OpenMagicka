using System.Numerics;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>What a player's controls tell its avatar (docs/engine/ENTITIES_AI.md §6.1): where to face and go, and its melee.</summary>
public sealed partial class Character
{
    /// <summary>A heavy weapon swings its two clips in turn.</summary>
    private bool heavyReload;

    /// <summary>
    /// The avatar turns toward a way on the ground (the cursor's, a stick's), and walks it when told to,
    /// as the original's mouse direction: a way too short to tell stops it, facing as it was; a short one
    /// it is not told to walk (under 0.32 or so) leaves it as it is; fear keeps its own way. Its scripted
    /// events take it over meanwhile.
    /// </summary>
    public void Steer(Vector3 toward, bool walk)
    {
        if (IsInEvent || !float.IsFinite(toward.X + toward.Y + toward.Z))
        {
            return;
        }
        var flat = toward with { Y = 0 };
        var length = flat.LengthSquared();
        if (length < 1e-6f && !IsPanicking && !IsFeared)
        {
            Body.Movement = Vector3.Zero;
            Body.WantedDirection = Body.Facing;
            return;
        }
        var way = length > 1e-6f ? flat / MathF.Sqrt(length) : flat;
        if (!walk)
        {
            if (!IsPanicking && !IsFeared && length > 0.1f)
            {
                Body.Movement = way;                                          // it turns that way...
            }
            way = Vector3.Zero;                                               // ...and stands
        }
        if (!IsFeared && !IsPanicking)
        {
            Body.Movement = way;
        }
    }

    /// <summary>
    /// The avatar walks a stick's way, as fast as the stick is pushed (up to its pace), as the original's pad
    /// direction: a way too short to tell stops it, facing as it was; fear keeps its own way. Its scripted
    /// events take it over meanwhile.
    /// </summary>
    public void Drive(Vector3 way)
    {
        if (IsInEvent || !float.IsFinite(way.X + way.Y + way.Z))
        {
            return;
        }
        way = way with { Y = 0 };
        if (way.LengthSquared() < 1e-6f && !IsPanicking && !IsFeared)
        {
            Body.Movement = Vector3.Zero;
            Body.WantedDirection = Body.Facing;
            return;
        }
        if (!IsFeared && !IsPanicking)
        {
            Body.Movement = way.LengthSquared() > 1 ? Vector3.Normalize(way) : way;
        }
    }

    /// <summary>It holds a gun (its weapon's slot): a held attack keeps firing.</summary>
    public bool IsWieldingGun => HeldItem(0) is { } weapon && Guns.IsGun(weapon.WeaponClass);

    /// <summary>
    /// The attack button (docs/engine/ENTITIES_AI.md §6.1): with elements queued (no magick) or a charged
    /// weapon, a Weapon cast when <paramref name="weaponCast"/> allows it; else a melee swing, unless one is
    /// under way and not near its end: a heavy weapon's two clips in turn, another's at random. Nothing
    /// while it casts, charges, or its weapon cools down.
    /// </summary>
    public void Attack(bool weaponCast = true)
    {
        if (Dead || CurrentSpell is not null || State == CharacterStates.Cast || State == CharacterStates.Charge || ItemIn(0) is { Cooldown: > 0 })
        {
            return;
        }
        if ((SpellQueue.Count > 0 && SpellQueue.Entries[0].Magick == MagickType.None) || ItemIn(0)?.IsSpellCharged == true)
        {
            if (weaponCast)
            {
                PressCast(CastType.Weapon);
            }
            return;
        }
        if (Attacking && Animator?.Clock is { Clip: { } clip } clock && clock.Time <= clip.Duration - 0.1f)
        {
            return;                                                           // a swing under way
        }
        string swing;
        if (ItemIn(0)?.WeaponClass == WeaponClass.Heavy)
        {
            if (Attacking)
            {
                return;
            }
            swing = heavyReload ? "attack_melee1" : "attack_melee0";
            heavyReload = !heavyReload;
        }
        else
        {
            swing = NextRandom() < 0.5 ? "attack_melee1" : "attack_melee0";
        }
        PlayAttack(swing, IsWieldingGun);
    }

    /// <summary>The attack button let go: the Weapon cast's button, and a gun stops firing.</summary>
    public void ReleaseAttack()
    {
        ReleaseCast(CastType.Weapon);
        if (IsWieldingGun)
        {
            NextAttackAnimation = null;
        }
    }
}
