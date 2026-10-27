using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// A grip from behind (OM-89, ENTITIES_AI.md §7.8), as the original's: only at a player's wizard not held yet; the more
/// it faces away and the better the distance, the more it is wanted; it is in range only while the target faces away,
/// and the spot to strike from is behind it. It leaps at the target (its clip's grip takes hold as it lands).
/// </summary>
public sealed class GripFromBehindBehaviour(GripAbility template, int index) : NpcAbility(template, index)
{
    private GripAbility Grip => (GripAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args)
    {
        if (agent.Owner.IsGripping || args.Target is not Character { IsPlayer: true, IsGripped: false } target)
        {
            return float.MinValue;
        }
        var behind = (Vector3.Dot(args.DeltaNormalized, target.Body.Facing) + 1) / 2;
        return (2 * behind * behind + FuzzyMath.DistanceExponential(args.Distance, Grip.MinRange, Grip.MaxRange)) / 3;
    }

    public override bool IsUseful(Agent agent) => agent.CurrentTarget is Character && !agent.Owner.IsGripping;

    public override float MinRange(Agent agent) => Grip.MinRange;

    public override float MaxRange(Agent agent) => Grip.MaxRange;

    public override float Arc(Agent agent) => Grip.Angle;

    public override bool InRange(Agent agent)
    {
        if (agent.CurrentTarget is not Character target)
        {
            return false;
        }
        var way = (target.Position - agent.Owner.Position) with { Y = 0 };
        return way.LengthSquared() > 1e-12f && Vector3.Dot(target.Body.Facing, Vector3.Normalize(way)) > 0.5f && base.InRange(agent);
    }

    public override bool ChooseAttackAngle(Agent agent, out Vector3 offset)
    {
        offset = agent.CurrentTarget is Character target ? -target.Body.Facing * ((Grip.MinRange + Grip.MaxRange) / 2) : Vector3.Zero;
        return agent.CurrentTarget is Character;
    }

    protected override bool Act(Agent agent)
    {
        var way = agent.CurrentTarget!.Position - agent.Owner.Position;
        var distance = way.Length();
        if (distance < float.Epsilon)
        {
            return false;
        }
        var reach = Math.Clamp(distance * 1.2f, MathF.Min(Grip.MinRange, Grip.MaxRange), MathF.Max(Grip.MinRange, Grip.MaxRange));
        return agent.Owner.Jump(way / distance * reach, MathF.PI / 4);
    }
}

/// <summary>
/// A pick-up (OM-89), as the original's: a character no heavier than it can lift, the closer and the stiller the
/// better; its first clip takes hold, its second (when it has one) is queued as the grip attack; the drop clip is the
/// ability's. A zombie's grip is wanted above all, and plays its second clip (the original never plays the first).
/// </summary>
public sealed class PickUpBehaviour(GripAbility template, int index) : NpcAbility(template, index)
{
    private GripAbility Grip => (GripAbility)Template;

    private bool Liftable(Entities.Entity? target) => target is Character character && character.Body.Body.Mass <= Grip.MaxWeight;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args)
    {
        if (agent.Owner.IsGripping || !Liftable(args.Target) || (Grip.Zombie && args.Target is Character { IsGripped: true }))
        {
            return float.MinValue;
        }
        return Grip.Zombie ? float.MaxValue
            : FuzzyMath.DistanceExponential(args.Distance, Grip.MinRange, Grip.MaxRange) + (1 - ((Character)args.Target!).Body.Movement.Length());
    }

    public override bool IsUseful(Agent agent) => Grip.Zombie
        ? (!agent.Owner.IsGripping || (Template.Animations.Count > 0 && agent.Owner.IsPlaying(Template.Animations[0]))) && Liftable(agent.CurrentTarget)
        : !agent.Owner.IsGripping && Liftable(agent.CurrentTarget) && agent.CurrentTarget is Character { SelfShield: not { IsActive: true, IsSolid: true } };

    public override float MinRange(Agent agent) => Grip.MinRange;

    public override float MaxRange(Agent agent) => Grip.MaxRange;

    public override float Arc(Agent agent) => Grip.Angle;

    public override bool InRange(Agent agent) => agent.Owner.IsGripping || base.InRange(agent);

    public override bool FacingTarget(Agent agent) => agent.Owner.IsGripping || base.FacingTarget(agent);

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        var animations = Template.Animations;
        if (Grip.Zombie)
        {
            var first = owner.IsPlaying(animations[0]);
            if (first && !owner.IsGripping)
            {
                return false;                                                 // its first clip plays, nothing held yet: nothing to do
            }
            if (!first)
            {
                owner.DropAnimation = Grip.DropAnimation;
            }
            owner.PlayAttack(animations[Math.Min(1, animations.Count - 1)], allowRotate: false);
            return true;
        }
        owner.DropAnimation = Grip.DropAnimation;
        owner.PlayAttack(animations[0], allowRotate: false);
        if (animations.Count > 1)
        {
            owner.DamageGripped(animations[1]);
        }
        return true;
    }
}

/// <summary>
/// Throwing its victim (OM-89), as the original's: wanted the more the victim has hurt it; any clip, in a random
/// direction (picked once a throw); the victim takes the ability's damages when it lands, and (when the clip's action
/// throws) flies to a spot ahead at the middle of the reach by the ballistic speed for the elevation.
/// </summary>
public sealed class ThrowGripBehaviour(ThrowGripAbility template, int index) : NpcAbility(template, index)
{
    private ThrowGripAbility Throw => (ThrowGripAbility)Template;

    private Vector3? way;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) =>
        agent.Owner.IsGripping ? agent.Owner.GripDamageAccumulation / MathF.Max(agent.Owner.Template.HitTolerance, 1) : float.MinValue;

    public override bool IsUseful(Agent agent) => agent.Owner.IsGripping;

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => 0;

    public override float Arc(Agent agent) => MathF.PI;

    public override bool InRange(Agent agent) => true;

    public override bool FacingTarget(Agent agent) => true;

    public override Vector3 DesiredDirection(Agent agent)
    {
        if (way is not { } chosen || !agent.Owner.IsGripping)
        {
            var yaw = (float)agent.Owner.NextRandom() * MathF.Tau;
            way = chosen = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        }
        return chosen;
    }

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        if (owner.GrippedCharacter is not { } victim)
        {
            return false;
        }
        owner.PlayAttack(Pick(agent), allowRotate: false);
        victim.CollisionDamages = Throw.Damages;
        owner.GripThrow = new GripThrowAim(Throw.MinRange, Throw.MaxRange, Throw.Elevation);   // reckoned as the clip throws, as the original's
        way = null;
        return true;
    }

    /// <summary>
    /// The throw's velocity, as the original's: to a spot ahead at the middle of the reach, along the facing tipped up by
    /// the elevation, at the speed a body under gravity needs to get there; where none does, the direction alone.
    /// </summary>
    public static Vector3 Velocity(Vector3 from, Vector3 facing, Vector3 victim, float min, float max, float elevation, float gravity)
    {
        var aim = from + facing * ((min + max) / 2);
        var height = aim.Y - victim.Y;
        var distance = new Vector2(aim.X - victim.X, aim.Z - victim.Z).Length();
        var flat = new Vector2(facing.X, facing.Z);
        flat = flat.LengthSquared() > 1e-12f ? Vector2.Normalize(flat) : Vector2.UnitY;
        var cos = MathF.Cos(elevation);
        var direction = new Vector3(flat.X * cos, MathF.Sin(elevation), flat.Y * cos);
        var speed = MathF.Sqrt(gravity * distance * distance / (2 * (distance * MathF.Tan(elevation) - height) * cos * cos));
        return float.IsFinite(speed) ? direction * speed : direction;
    }
}

/// <summary>
/// Hurting (or confusing) its victim (OM-89), as the original's: wanted while it holds; to hurt, less the more the
/// victim has hurt it, and only while that is under half its tolerance; a hurt is queued as a grip attack, a
/// confusion is a plain attack.
/// </summary>
public sealed class GripActionBehaviour(GripActionAbility template, int index) : NpcAbility(template, index)
{
    private bool Confuse => ((GripActionAbility)Template).Confuse;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args)
    {
        var owner = agent.Owner;
        if (!owner.IsGripping)
        {
            return float.MinValue;
        }
        return Confuse ? 1 : (1 - 2 * owner.GripDamageAccumulation / MathF.Max(owner.Template.HitTolerance, 1)) * 2;
    }

    public override bool IsUseful(Agent agent) =>
        agent.Owner.IsGripping && (Confuse || agent.Owner.GripDamageAccumulation < agent.Owner.Template.HitTolerance / 2f);

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => 0;

    public override float Arc(Agent agent) => MathF.PI;

    public override bool InRange(Agent agent) => true;

    public override bool FacingTarget(Agent agent) => true;

    public override Vector3 DesiredDirection(Agent agent) => Confuse ? -Vector3.UnitZ : Vector3.UnitZ;   // the original's Forward and Backward

    protected override bool Act(Agent agent)
    {
        if (Confuse)
        {
            agent.Owner.PlayAttack(Pick(agent), allowRotate: false);
        }
        else
        {
            agent.Owner.DamageGripped(Pick(agent));
        }
        return true;
    }
}
