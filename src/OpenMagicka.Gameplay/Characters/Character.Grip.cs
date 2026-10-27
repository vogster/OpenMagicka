using System.Numerics;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>How a gripper holds its victim (OM-89): up on a bone, glued in front of it, or riding it.</summary>
public enum GripType : byte { Pickup, Ride, Hold }

/// <summary>
/// Grips (OM-89, docs/engine/ENTITIES_AI.md §5.6), as the original's: an NPC takes hold of a character (its mind's
/// target) and holds it every frame after the state machine: a picked-up victim hangs at the gripper's bone, a held
/// one stands still with the gripper glued in front, a ridden one stumbles backwards with the gripper on its back. The
/// victim struggles free (its strength a struggle, up to the gripper's tolerance), or the grip ends when the victim
/// burns, turns immortal, hurts the gripper past its hit tolerance, either dies, or the gripper is pushed, knocked
/// down or panics. Released, the victim is put down on the ground.
/// </summary>
public sealed partial class Character
{
    /// <summary>The heaviest a victim may be; the tolerance a grip's 0 stands for, and one below 1 stands for.</summary>
    public const float MaxGripMass = 500;

    public const int DefaultBreakFreeTolerance = 10, LeastBreakFreeTolerance = 3;

    private Matrix4x4 gripBind = Matrix4x4.Identity;

    /// <summary>Who holds it; none: free.</summary>
    public Character? Gripper { get; private set; }

    /// <summary>Whom it holds; none: no one.</summary>
    public Character? GrippedCharacter { get; private set; }

    public bool IsGripped => Gripper is not null;

    public bool IsGripping => GrippedCharacter is not null;

    /// <summary>Its grip's kind, set on both sides when a grip starts and kept after it ends, as the original's.</summary>
    public GripType GripType { get; private set; }

    /// <summary>The bone the grip follows (the gripper's for a pickup, the victim's for a ride or hold); −1: none.</summary>
    public int GripBone { get; private set; } = -1;

    /// <summary>The gripper's: how many struggle points free its victim.</summary>
    public int BreakFreeTolerance { get; private set; }

    /// <summary>The victim's: its struggle points so far (they never fade).</summary>
    public int BreakFreeCounter { get; private set; }

    /// <summary>The gripper's: what its victim has hurt it during this grip.</summary>
    public float GripDamageAccumulation { get; private set; }

    /// <summary>The gripper means to make a grip attack (a clip queued for it).</summary>
    public bool GripAttacking { get; internal set; }

    /// <summary>The grip attack's clip, queued.</summary>
    public string? NextGripAttackAnimation { get; internal set; }

    /// <summary>The gripper's clip for letting a picked-up victim drop (its ability's); none: <c>hit</c>.</summary>
    public string? DropAnimation { get; set; }

    /// <summary>Where its throwing ability means to throw its victim (a velocity); none: it does not throw.</summary>
    public Vector3? GripThrow { get; set; }

    /// <summary>What a thrown victim takes when it next meets the level (the throw's damages), once.</summary>
    public IReadOnlyList<DamageEntry> CollisionDamages { get; set; } = [];

    /// <summary>Ridden: it stumbles backwards.</summary>
    public bool IsStumbling => Gripper is not null && GripType == GripType.Ride;

    /// <summary>
    /// The gripper should let go: it holds no one, or its victim burns, is immortal, has struggled up to the tolerance,
    /// or has hurt it past its hit tolerance.
    /// </summary>
    public bool ShouldReleaseGrip => GrippedCharacter is not { } victim
        || victim.Statuses.Has(StatusEffects.Burning) || victim.IsImmortal || victim.BreakFreeCounter >= BreakFreeTolerance
        || GripDamageAccumulation > Template.HitTolerance;

    /// <summary>
    /// Takes hold of a victim, unless it is gripped already, dead, immortal, ethereal, heavier than 500, or (to ride it)
    /// has no stumbling clip, or the gripper lies knocked down. A victim that holds someone lets go; its vines go.
    /// </summary>
    /// <param name="bone">The gripper's bone for a pickup, the victim's for a ride or hold; −1: none.</param>
    /// <param name="tolerance">Struggle points that free it: above 0 as given, else 3.</param>
    public bool GripCharacter(Character victim, GripType type, int bone, int tolerance)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (ReferenceEquals(victim, this) || victim.IsGripped || victim.Dead || victim.HitPoints <= 0 || victim.IsDrowning || victim.IsImmortal
            || victim.IsEthereal || victim.Body.Body.Mass > MaxGripMass || IsKnockedDown || State is States.KnockDownState
            || (type == GripType.Ride && !victim.HasAnimation("move_stumble")))
        {
            return false;
        }
        (GripType, BreakFreeTolerance, GripDamageAccumulation, GripBone) = (type, tolerance > 0 ? tolerance : LeastBreakFreeTolerance, 0, bone);
        victim.ReleaseAttachedCharacter();
        victim.ReleaseEntanglement();
        gripBind = Matrix4x4.CreateRotationY(MathF.PI);                       // the bone's own frame turned round, as a held item's
        (GrippedCharacter, victim.Gripper, victim.GripType) = (victim, this, type);
        (BreakFreeCounter, victim.BreakFreeCounter) = (0, 0);
        (Body.IgnoredBody, victim.Body.IgnoredBody) = (victim.Body.Body, Body.Body);
        return true;
    }

    /// <summary>
    /// Lets go of its victim (and of its tally of the victim's blows): the victim may turn again, may walk again unless the
    /// gripper's state is one that stops it (the original's slip, the victim's next state sets it right), its struggle
    /// points gone, and it is put down on the ground below it.
    /// </summary>
    public void ReleaseAttachedCharacter()
    {
        GripDamageAccumulation = 0;
        if (GrippedCharacter is not { } victim)
        {
            return;
        }
        victim.Body.AllowMove = !(State is States.BusyState or States.DeadState or States.DropState or States.LandState or States.IdleState
            or States.JumpState or States.MoveState or States.EntangledState or States.GripAttackState or States.RessurectionState);
        victim.Body.AllowRotate = true;
        victim.BreakFreeCounter = 0;
        (GrippedCharacter, victim.Gripper) = (null, null);
        (Body.IgnoredBody, victim.Body.IgnoredBody) = (null, null);
        victim.PutDown();
    }

    /// <summary>Its gripper lets go of it.</summary>
    public void ReleaseFromGripper() => Gripper?.ReleaseAttachedCharacter();

    /// <summary>Put down on the ground under it (from 3 above, 6 down), when there is ground.</summary>
    private void PutDown()
    {
        if (world.IsDisposed || world.IsStepping || Body.Body.IsRemoved)
        {
            return;
        }
        var at = Position;
        if (world.CastSegment(at + Vector3.UnitY * 3, at - Vector3.UnitY * 3, levelOnly: true) is { } ground)
        {
            Body.Body.Position = ground.Position + Vector3.UnitY * (Body.Shape.Radius + Body.Shape.Length / 2 + 0.1f);
        }
    }

    /// <summary>
    /// A struggle against a grip: the template's strength (whole) more points, and a tear at its vines as an Earth blow of
    /// 0.5 (an NPC's only every half second).
    /// </summary>
    internal void Struggle()
    {
        if (!IsPlayer && breakFreeTimer > 0)
        {
            return;
        }
        BreakFreeCounter += (int)Template.BreakFreeStrength;
        DecreaseEntanglement(0.5f, Spells.Elements.Earth);
        breakFreeTimer = IsPlayer ? 0 : BreakFreeInterval;
    }

    /// <summary>A blow its victim dealt it counts against the grip; a grip attack it took it back to holding.</summary>
    private void NoteGripDamage(Entities.Entity? attacker, float dealt)
    {
        if (GrippedCharacter is not null && ReferenceEquals(attacker, GrippedCharacter) && dealt > 0)
        {
            GripDamageAccumulation += dealt;
            if (State is States.GripAttackState)
            {
                ChangeState(States.CharacterStates.Gripping);
            }
        }
    }

    /// <summary>
    /// The grip each frame, after the state machine (OM-89): a gripper lets go when it should or either is dead; a
    /// picked-up victim is carried at the gripper's bone; a held one stands still with the gripper put in front of it;
    /// a rider sits on its victim's back; a gripper that does not ride stands still.
    /// </summary>
    private void UpdateGrip()
    {
        if (GrippedCharacter is { } victim)
        {
            if (HitPoints <= 0 || Dead || victim.Dead || victim.HitPoints <= 0 || ShouldReleaseGrip)
            {
                ReleaseAttachedCharacter();
            }
            else if (GripType == GripType.Pickup)
            {
                var frame = gripBind * BoneWorldOrRoot(GripBone);
                Place(victim, frame.Translation, frame);
            }
            else if (GripType == GripType.Hold)
            {
                (victim.Body.SpeedMultiplier, victim.Body.AllowMove, victim.Body.AllowRotate) = (0, false, false);
                var away = victim.Position - Position;
                var dir = away.LengthSquared() > 1e-8f ? Vector3.Normalize(away) : -Vector3.UnitZ;
                Body.Body.Position = victim.Position - dir * (Body.Shape.Radius + victim.Body.Shape.Radius);
                Body.Body.Velocity = Vector3.Zero;
                Body.Face(dir);
            }
        }
        if (Gripper is { } gripper)
        {
            if (Dead || gripper.Dead)
            {
                gripper.ReleaseAttachedCharacter();
            }
            else if (GripType == GripType.Ride)
            {
                var spine = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateRotationX(MathF.PI / 2);
                var frame = spine * gripper.gripBind * BoneWorldOrRoot(gripper.GripBone);
                var lift = Vector3.Normalize(new Vector3(frame.M21, frame.M22, frame.M23)) * (gripper.Body.Shape.Length / 2 + gripper.Body.Shape.Radius);
                Place(gripper, frame.Translation + lift, frame);
            }
            else
            {
                gripper.Body.SpeedMultiplier = 0;                             // it cannot walk while it holds
            }
        }
    }

    /// <summary>A bone's place, or the root's without one.</summary>
    private Matrix4x4 BoneWorldOrRoot(int bone) => Skeleton is { } skeleton && bone >= 0 && bone < skeleton.Bones.Count ? BoneWorld(bone) : root;

    /// <summary>A body put where a frame says, still, facing along it.</summary>
    private static void Place(Character who, Vector3 at, Matrix4x4 frame)
    {
        if (!float.IsFinite(at.X + at.Y + at.Z))
        {
            return;
        }
        who.Body.Body.Position = at;
        who.Body.Body.Velocity = Vector3.Zero;
        var forward = new Vector3(-frame.M31, 0, -frame.M33);
        if (forward.LengthSquared() > 1e-8f)
        {
            who.Body.Face(Vector3.Normalize(forward));
        }
    }
}
