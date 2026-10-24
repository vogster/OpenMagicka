using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Animation;
using StatusEffects = OpenMagicka.Gameplay.Combat.StatusEffects;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Engine.Characters;

/// <summary>
/// The gameplay action for a template's action record (docs/engine/ENTITIES_AI.md §4.2); null for
/// the kinds that wait for items, spells, grips or the frog's tongue.
/// </summary>
public static class CharacterAnimationActions
{
    public static AnimationAction? Create(AnimationActionContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (start, end) = (content.StartTime, content.EndTime);
        return content switch
        {
            MoveAction a => new Actions.Move(start, end, new System.Numerics.Vector3(a.Velocity.X, a.Velocity.Y, a.Velocity.Z)),
            JumpAction a => new Actions.Jump(start, end, a.Elevation, a.MinRange, a.MaxRange),
            CrouchAction a => new Actions.Crouch(start, end, a.Radius, a.Length),
            EtherealAction a => new Actions.Ethereal(start, end, a.Ethereal, a.Alpha, a.Speed),
            InvisibleAction a => new Actions.Invisible(start, end, a.NoEffect),
            ImmortalAction a => new Actions.Immortal(start, end, a.Collide),
            RemoveStatusAction a => new Actions.RemoveStatus(start, end, Status(a.StatusEffect)),
            SuicideAction a => new Actions.Suicide(start, end, a.Overkill),
            BlockAction a => new Actions.Block(start, end, a.Weapon),
            BreakFreeAction a => new Actions.BreakFree(start, end, a.Magnitude),
            PlaySoundAction a => new Actions.PlaySound(start, end, a.Cue, (int)a.Banks),
            PlayEffectAction a => new Actions.PlayEffect(start, end, a.Effect, a.Bone, a.Attach),
            FootstepAction => new Actions.Footstep(start, end),
            CameraShakeAction a => new Actions.CameraShake(start, end, a.Duration, a.Magnitude),
            _ => null,
        };
    }

    /// <summary>A status by its name in the data, ignoring case as the original (Life is healing).</summary>
    private static StatusEffects Status(string name) =>
        Enum.TryParse<OpenMagicka.Content.Magicka.StatusEffects>(name, ignoreCase: true, out var status) && Enum.IsDefined(status)
            ? (StatusEffects)(short)status
            : throw new InvalidDataException($"unknown status effect '{name}'");
}
