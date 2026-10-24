using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.AI;

/// <summary>A blow by a clip of its own (the weapon's damage comes from the clip's actions); not while gripping (§7.8).</summary>
public sealed class MeleeBehaviour(MeleeAbility template, int index) : NpcAbility(template, index)
{
    private MeleeAbility Melee => (MeleeAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => FuzzyMath.DistanceExponential(args.Distance, Melee.MinRange, Melee.MaxRange);

    public override float MinRange(Agent agent) => Melee.MinRange;

    public override float MaxRange(Agent agent) => Melee.MaxRange;

    public override float Arc(Agent agent) => Melee.Arc;

    protected override bool Act(Agent agent)
    {
        agent.Owner.PlayAttack(Pick(agent), Melee.Rotate);
        return true;
    }
}

/// <summary>
/// A throw or a shot (§7.8): not with a fellow on the line (within 0.75 of it, the line shortened by
/// the cosine of the elevation); the weapons then aim at the target by the ability's elevation and
/// accuracy (<see cref="Items.Ranged"/>).
/// </summary>
public sealed class RangedBehaviour(RangedAbility template, int index) : NpcAbility(template, index)
{
    public RangedAbility Ranged => (RangedAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => FuzzyMath.DistanceExponential(args.Distance, Ranged.MinRange, Ranged.MaxRange);

    public override float MinRange(Agent agent) => Ranged.MinRange;

    public override float MaxRange(Agent agent) => Ranged.MaxRange;

    public override float Arc(Agent agent) => Ranged.Arc;

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        var target = agent.CurrentTarget!;
        var line = target.Position - owner.Position;
        owner.Body.WantedDirection = line;
        line *= MathF.Cos(Ranged.Elevation);
        foreach (var other in owner.Manager?.Query(owner.Position + line * 0.5f, Ranged.MaxRange).OfType<Character>() ?? [])
        {
            if (!ReferenceEquals(other, owner) && (other.Factions & owner.Factions) != 0 && other.SegmentIntersect(new Entities.Segment(owner.Position, line), 0.75f))
            {
                return false;                                                // a fellow in the way
            }
        }
        foreach (var slot in Ranged.Weapons)
        {
            if (owner.HeldItem(slot) is { } item)
            {
                item.AimedBy = (Ranged, target);                              // the throw goes at this target, though the mind moves on
            }
        }
        owner.PlayAttack(Pick(agent), allowRotate: true);
        return true;
    }

    /// <summary>
    /// Its own spot to shoot from: as the base's, but at the middle of its reach, and with no level
    /// wall on the first or last part of the shot (the lower the elevation, the longer the parts).
    /// </summary>
    public override bool ChooseAttackAngle(Agent agent, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (agent.CurrentTarget is not { } target)
        {
            return false;
        }
        var reach = (Ranged.MinRange + Ranged.MaxRange) * 0.5f;
        var back = (agent.Owner.Position - target.Position) with { Y = 0 };
        if (back.LengthSquared() <= 1e-6f)
        {
            return false;
        }
        back = Vector3.Normalize(back) * reach;
        var part = (1 - Ranged.Elevation / (MathF.PI / 2)) * 0.5f;
        for (var i = 0; i < 3; i++)
        {
            var turn = (float)agent.Owner.NextRandom() * MathF.PI - MathF.PI / 2;
            var tried = Vector3.Transform(back, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
            var world = agent.Owner.World;
            if (world.CastSegment(target.Position, target.Position + tried * part, levelOnly: true) is null
                && world.CastSegment(target.Position + tried, target.Position + tried - tried * part, levelOnly: true) is null)
            {
                offset = tried;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// A raised guard for a second (§7.8): it must have an expression, as the original. Its reach has no
/// end; the clip's block action ends it as under way.
/// </summary>
public sealed class BlockBehaviour(BlockAbility template, int index) : NpcAbility(template, index)
{
    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => float.MinValue;

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => float.MaxValue;

    public override float Arc(Agent agent) => ((BlockAbility)Template).Arc;

    protected override bool Act(Agent agent)
    {
        agent.Block();
        return true;
    }
}

/// <summary>A dash by a clip of its own, without turning; not while gripping (§7.8).</summary>
public sealed class DashBehaviour(DashAbility template, int index) : NpcAbility(template, index)
{
    private DashAbility Dash => (DashAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => FuzzyMath.DistanceExponential(args.Distance, Dash.MinRange, Dash.MaxRange);

    public override float MinRange(Agent agent) => Dash.MinRange;

    public override float MaxRange(Agent agent) => Dash.MaxRange;

    public override float Arc(Agent agent) => Dash.Arc;

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        (owner.NextAttackAnimation, owner.AllowAttackRotate, owner.Dashing) = (Pick(agent), false, true);
        owner.NextDashAnimation = Pick(agent);                                // as the original: a second pick
        return true;
    }
}

/// <summary>
/// A leap at a wizard (§7.8): wanted beyond measure against one, never against anything else. It
/// lands where the target will be in half a second, 1.1 times as far, within its reach.
/// </summary>
public sealed class JumpBehaviour(JumpAbility template, int index) : NpcAbility(template, index)
{
    private JumpAbility Jump => (JumpAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => args.Target is Character { IsPlayer: true } ? float.MaxValue : float.MinValue;

    public override float MinRange(Agent agent) => Jump.MinRange;

    public override float MaxRange(Agent agent) => Jump.MaxRange;

    public override float Arc(Agent agent) => Jump.Angle;

    /// <summary>Its reach is in three dimensions, the radii not counted.</summary>
    public override bool InRange(Agent agent)
    {
        if (agent.CurrentTarget is not { } target)
        {
            return false;
        }
        var distance = Vector3.DistanceSquared(agent.Owner.Position, target.Position);
        return distance >= Jump.MinRange * Jump.MinRange && distance <= Jump.MaxRange * Jump.MaxRange;
    }

    protected override bool Act(Agent agent)
    {
        var target = agent.CurrentTarget!;
        var aim = target.Position;
        if (target.Velocity.LengthSquared() > float.Epsilon)
        {
            aim += target.Velocity * 0.5f;
        }
        var way = aim - agent.Owner.Position;
        var distance = way.Length();
        if (distance < float.Epsilon)
        {
            return false;
        }
        var reach = Math.Clamp(distance * 1.1f, MathF.Min(Jump.MinRange, Jump.MaxRange), MathF.Max(Jump.MinRange, Jump.MaxRange));
        return agent.Owner.Jump(way / distance * reach, Jump.Elevation);   // a leap no speed makes is not under way
    }
}

/// <summary>
/// Stealing the elements chanted (§7.8): it plays its clip; once that is on, the target (or, with no
/// angle, every foe within reach) loses its queue. It must have an expression.
/// </summary>
public sealed class ElementStealBehaviour(ElementStealAbility template, int index) : NpcAbility(template, index)
{
    private ElementStealAbility Steal => (ElementStealAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => float.MinValue;

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => Steal.Range;

    public override float Arc(Agent agent) => Steal.Angle != 0 ? Steal.Angle : MathF.Tau;

    public override Vector3 DesiredDirection(Agent agent) => agent.Owner.Body.Facing;

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        if (Template.Animations.Count == 0)
        {
            return false;
        }
        var clip = Template.Animations[0];
        if (owner.NextAttackAnimation == clip)
        {
            return false;
        }
        if (owner.CurrentAnimation != clip)
        {
            owner.PlayAttack(clip, allowRotate: false);
            return false;
        }
        if (Steal.Angle == 0)
        {
            foreach (var foe in owner.Manager?.Query(owner.Position, Steal.Range, ignoreProtected: true).OfType<Character>() ?? [])
            {
                if (foe.SpellQueue.Count > 0 && (foe.Factions & owner.Factions) == 0)
                {
                    foe.ClearQueue();
                }
            }
        }
        else if (agent.CurrentTarget is Character target)
        {
            target.ClearQueue();
        }
        return true;
    }
}

/// <summary>Shaking off a status by a clip (its expression says when); no use while one of its clips plays (§7.8).</summary>
public sealed class RemoveStatusBehaviour(RemoveStatusAbility template, int index) : NpcAbility(template, index)
{
    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => float.MinValue;

    public override bool IsUseful(Agent agent) => !Template.Animations.Contains(agent.Owner.CurrentAnimation);

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => float.MaxValue;

    public override float Arc(Agent agent) => MathF.PI;

    public override Vector3 DesiredDirection(Agent agent) => agent.Owner.Body.Facing;

    protected override bool Act(Agent agent)
    {
        agent.Owner.PlayAttack(Pick(agent), allowRotate: false);
        return true;
    }
}

/// <summary>A held item's special ability, once it is ready: by a clip of its own, or straight away with none (§7.8).</summary>
public sealed class SpecialAbilityBehaviour(SpecialAbilityAbility template, int index) : NpcAbility(template, index)
{
    private SpecialAbilityAbility Special => (SpecialAbilityAbility)Template;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => FuzzyMath.DistanceExponential(args.Distance, Special.MinRange, Special.MaxRange);

    public override bool IsUseful(Agent agent) => agent.Owner.HeldItem(Special.Weapon)?.IsSpecialReady == true;

    public override float MinRange(Agent agent) => Special.MinRange;

    public override float MaxRange(Agent agent) => Special.MaxRange;

    public override float Arc(Agent agent) => Special.Angle;

    protected override bool Act(Agent agent)
    {
        if (Template.Animations.Count == 0)
        {
            agent.Owner.UseSpecialAbility(Special.Weapon);
        }
        else
        {
            agent.Owner.PlayAttack(Pick(agent), allowRotate: false);
        }
        return true;
    }
}
