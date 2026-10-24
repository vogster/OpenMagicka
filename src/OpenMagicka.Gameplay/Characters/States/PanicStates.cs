using System.Numerics;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>Shared steering of a frightened character (docs/gameplay/MAGICKS.md §6.1).</summary>
internal static class Panicking
{
    public const float Slow = 0.8f;

    /// <summary>The wander heading drifts by up to ±30 radians a second, as the original.</summary>
    public static Vector3 Wander(Character character, float dt)
    {
        character.WanderAngle = WrapAngle(character.WanderAngle + ((float)character.NextRandom() - 0.5f) * 60 * dt);
        var drift = new Vector3(MathF.Cos(character.WanderAngle), 0, MathF.Sin(character.WanderAngle));
        var way = character.Body.Facing * 4 + drift;
        return way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) : character.Body.Facing;
    }

    /// <summary>The flat way from what it fears.</summary>
    public static Vector3 Away(Character character)
    {
        var away = (character.Position - character.FearPosition) with { Y = 0 };
        away.X += 0.0001f;
        return Vector3.Normalize(away);
    }

    /// <summary>The way, normalised; the fallback when it is (nearly) nothing.</summary>
    public static Vector3 Or(Vector3 way, Vector3 fallback) => way.LengthSquared() > 1e-6f ? Vector3.Normalize(way) : fallback;

    /// <summary>Gives back where it was going before the fear (each panic state, on leaving; the next takes it again).</summary>
    public static void GiveBack(Character character)
    {
        if (character.WantedMovement is { } wanted)
        {
            (character.Body.Movement, character.WantedMovement) = (wanted, null);
        }
    }

    private static float WrapAngle(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
}

/// <summary>
/// Panicking or fleeing in fear (MAGICKS.md §6.1, ENTITIES_AI.md §5): a panic runs along the wander
/// heading, blended with where it was going by the template's panic limit; a fear runs away from what
/// it fears, halfway with where it was going. Either at 0.8 speed.
/// </summary>
internal sealed class PanicState : CharacterState
{
    /// <summary>Where it was going is kept, to blend with and to give back.</summary>
    protected internal override void Enter(Character character)
    {
        character.RemoveInvisibility();
        (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
        character.WantedMovement ??= character.Body.Movement;
        character.GoToAnimation("move_panic", 0.2f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } dead)
        {
            return dead;
        }
        if (character.IsPlayer && character.CastType != CastType.None)
        {
            return CharacterStates.PanicCast;
        }
        if (character.IsEntangled)
        {
            return CharacterStates.Entangled;
        }
        var wander = Panicking.Wander(character, dt);
        if (!character.IsFeared && !character.IsPanicking && !character.IsStumbling)
        {
            return CharacterStates.Idle;
        }
        character.Body.SpeedMultiplier *= Panicking.Slow;
        var wanted = (character.IsPlayer ? character.WantedMovement : character.Body.Movement) ?? Vector3.Zero;
        if (character.IsPanicking)
        {
            var share = character.Template.MaxPanic;                         // as the original: the limit is the blend
            character.Body.Movement = wanted.LengthSquared() > 1e-6f ? Panicking.Or(wander * share + wanted * (1 - share), wander) : wander;
            return null;
        }
        wanted = character.WantedMovement ?? Vector3.Zero;
        var away = Panicking.Away(character);
        character.Body.Movement = wanted.LengthSquared() > 1e-6f ? Panicking.Or(away * 0.5f + wanted * 0.5f, away) : away;
        return null;
    }

    protected internal override void Exit(Character character)
    {
        Panicking.GiveBack(character);
    }
}

/// <summary>Casting in fear: the spell goes at once while it keeps fleeing (MAGICKS.md §6.1).</summary>
internal sealed class PanicCastState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.RemoveInvisibility();
        (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
        character.WantedMovement ??= character.Body.Movement;
        character.CastSpell(fromStaff: true);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } dead)
        {
            return dead;
        }
        if (character.IsEntangled)
        {
            return CharacterStates.Entangled;
        }
        if (character.CurrentSpell is null or { IsActive: false } || character.CastType == CastType.Weapon)
        {
            return CharacterStates.Panic;
        }
        var wander = Panicking.Wander(character, dt);
        if (!character.IsFeared)
        {
            return GoOn(character);
        }
        var away = Panicking.Away(character);
        character.Body.Movement = Panicking.Or(away + wander, away);
        return null;
    }

    protected internal override void Exit(Character character)
    {
        character.StopSpell();
        character.CastType = CastType.None;
        Panicking.GiveBack(character);
    }
}
