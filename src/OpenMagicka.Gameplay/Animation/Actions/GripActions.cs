using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Animation.Actions;

/// <summary>
/// An NPC takes hold of its mind's target (OM-89), each frame of the window, as the original's: the target within reach
/// on the ground plane (from a bone and the action's radius, or from the NPC and its own), in sight past the level and
/// any spell shield; the grip follows the NPC's bone for a pickup, the target's for a ride or hold (ours: that bone found
/// on the target's own skeleton). Finishing on the grip, it goes to holding at once.
/// </summary>
/// <param name="tolerance">Struggle points that free the victim: the content's 0 stands for 10.</param>
public sealed class Grip(float start, float end, GripType type, float radius, float tolerance, string gripBone, string targetBone, bool finishOnGrip)
    : AnimationAction(start, end)
{
    public GripType Type { get; } = type;

    public float Radius { get; } = Finite(radius, nameof(radius));

    public int Tolerance { get; } = (int)tolerance == 0 ? Character.DefaultBreakFreeTolerance : (int)tolerance;

    public override void Run(Character owner, bool first)
    {
        if (owner.IsPlayer || owner.IsGripping || owner.Target is not Character victim || owner.World.IsDisposed || owner.World.IsStepping
            || victim.SelfShield is { IsActive: true, IsSolid: true })
        {
            return;
        }
        var ownBone = owner.Skeleton?.IndexOf(gripBone) ?? -1;
        var (reachAt, reach) = ownBone >= 0 ? (owner.BoneWorld(ownBone).Translation, Radius) : (owner.Position, owner.Body.Shape.Radius);
        var flat = new Vector2(victim.Position.X - reachAt.X, victim.Position.Z - reachAt.Z);
        if (flat.Length() >= victim.Body.Shape.Radius + reach || owner.World.CastSegment(owner.Position, victim.Position, levelOnly: true) is not null)
        {
            return;
        }
        foreach (var shield in owner.Manager?.Shields ?? [])
        {
            if (shield.Blocks(owner.Position, victim.Position, Radius, out _))
            {
                return;
            }
        }
        var bone = Type == GripType.Pickup ? ownBone : victim.Skeleton?.IndexOf(targetBone) ?? -1;
        if (owner.GripCharacter(victim, Type, bone, Tolerance) && finishOnGrip)
        {
            owner.ChangeState(Characters.States.CharacterStates.Gripping);
        }
    }
}

/// <summary>Each frame of the window, the damages hit the NPC itself or, holding, its victim (the gripper the attacker).</summary>
public sealed class DamageGrip(float start, float end, bool toOwner, IReadOnlyList<DamageEntry> damages) : AnimationAction(start, end)
{
    public IReadOnlyList<DamageEntry> Damages { get; } = damages ?? throw new ArgumentNullException(nameof(damages));

    public override void Run(Character owner, bool first)
    {
        var target = toOwner ? owner : owner.GrippedCharacter;
        foreach (var damage in target is null ? [] : Damages)
        {
            target!.Damage(damage, owner, owner.Position);
        }
    }
}

/// <summary>Each frame of the window, it lets go of its victim (whose throw damages go).</summary>
public sealed class ReleaseGrip(float start, float end) : AnimationAction(start, end)
{
    public override void Run(Character owner, bool first)
    {
        if (owner.GrippedCharacter is { } victim)
        {
            victim.CollisionDamages = [];
        }
        owner.ReleaseAttachedCharacter();
    }
}

/// <summary>
/// At the window's start, holding, it throws its victim: an NPC by its throwing ability's aim (none: nothing), a player
/// along its facing tipped up a tenth; the victim is let go (put down) and then pushed.
/// </summary>
public sealed class ThrowGrip(float start, float end) : AnimationAction(start, end)
{
    public override void Run(Character owner, bool first)
    {
        if (!first || owner.GrippedCharacter is not { } victim)
        {
            return;
        }
        var velocity = owner.IsPlayer ? owner.Body.Facing with { Y = 0.1f } : owner.GripThrow;
        owner.GripThrow = null;                                               // one aim, one throw
        if (velocity is not { } push)
        {
            return;
        }
        owner.ReleaseAttachedCharacter();
        victim.Body.PushVelocity(push);
    }
}

/// <summary>Each frame of the window, its victim is shattered.</summary>
public sealed class OverkillGrip(float start, float end) : AnimationAction(start, end)
{
    public override void Run(Character owner, bool first) => owner.GrippedCharacter?.OverKill();
}
