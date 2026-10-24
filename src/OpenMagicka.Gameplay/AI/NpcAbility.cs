using System.Numerics;
using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// What an NPC does with one of its template's abilities (ENTITIES_AI.md §7.8): how much it is
/// wanted, whether it is in reach and aimed, and what doing it is. One kind per template record.
/// </summary>
public abstract class NpcAbility
{
    protected NpcAbility(AbilityTemplate template, int index) => (Template, Index) = (template ?? throw new ArgumentNullException(nameof(template)), index);

    public AbilityTemplate Template { get; }

    /// <summary>Its place among the template's abilities (its cooldown's, and the script's priority).</summary>
    public int Index { get; }

    public static NpcAbility For(AbilityTemplate template, int index) => template switch
    {
        CastSpellAbility spell => new CastSpellBehaviour(spell, index),
        MeleeAbility melee => new MeleeBehaviour(melee, index),
        RangedAbility ranged => new RangedBehaviour(ranged, index),
        BlockAbility block => new BlockBehaviour(block, index),
        DashAbility dash => new DashBehaviour(dash, index),
        JumpAbility jump => new JumpBehaviour(jump, index),
        ElementStealAbility steal => new ElementStealBehaviour(steal, index),
        RemoveStatusAbility remove => new RemoveStatusBehaviour(remove, index),
        SpecialAbilityAbility special => new SpecialAbilityBehaviour(special, index),
        _ => new PendingAbility(template, index),                             // the grips, with OM-33's
    };

    /// <summary>One of its clips at random.</summary>
    protected string Pick(Agent agent) => Template.Animations[(int)(agent.Owner.NextRandom() * Template.Animations.Count) % Template.Animations.Count];

    /// <summary>
    /// How much it is wanted against the target: nothing when useless or cooling; else its
    /// expression's value, or its own measure when it has none.
    /// </summary>
    public float Desirability(Agent agent, in FuzzyArguments args)
    {
        if (!IsUseful(agent, args) || agent.Cooldown(Index) > 0)
        {
            return float.MinValue;
        }
        return Template.Desire is { } desire ? desire.Value(args) : OwnDesirability(agent, args);
    }

    protected virtual bool IsUseful(Agent agent, in FuzzyArguments args) => IsUseful(agent);

    /// <summary>Whether it is of any use now (a melee needs a weapon, a grip a grippable target…).</summary>
    public virtual bool IsUseful(Agent agent) => true;

    /// <summary>Which way to face while using it: at the current target, on the flat.</summary>
    public virtual Vector3 DesiredDirection(Agent agent)
    {
        var way = agent.CurrentTarget is { } target ? (target.Position - agent.Owner.Position) with { Y = 0 } : Vector3.Zero;
        return way.LengthSquared() > 1e-12f ? Vector3.Normalize(way) : agent.Owner.Body.Facing;
    }

    /// <summary>
    /// A spot around the current target to attack it from (§7.8): at the middle of its reach (plus the
    /// target's radius) from it, on its side half-circle toward the agent, up to three random tries for
    /// one with no level wall from the target. As the original, characters in the way are looked at but
    /// do not stop the choice. The offset from the target; false when all three are walled.
    /// </summary>
    public virtual bool ChooseAttackAngle(Agent agent, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (agent.CurrentTarget is not { } target)
        {
            return false;
        }
        var reach = (MinRange(agent) + MaxRange(agent)) * 0.5f + target.Radius;
        var back = (agent.Owner.Position - target.Position) with { Y = 0 };
        if (back.LengthSquared() <= 1e-6f)
        {
            return false;
        }
        back = Vector3.Normalize(back) * reach;
        for (var i = 0; i < 3; i++)
        {
            var turn = (float)agent.Owner.NextRandom() * MathF.PI - MathF.PI / 2;
            var tried = Vector3.Transform(back, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
            if (agent.Owner.World.CastSegment(target.Position, target.Position + tried, levelOnly: true) is null)
            {
                offset = tried;
                return true;
            }
        }
        return false;
    }

    protected abstract float OwnDesirability(Agent agent, in FuzzyArguments args);

    public abstract float MinRange(Agent agent);

    public abstract float MaxRange(Agent agent);

    public abstract float Arc(Agent agent);

    /// <summary>Readies itself while it is the next (an NPC chanting its elements).</summary>
    public virtual void Update(Agent agent, float dt)
    {
    }

    /// <summary>Does it; true when it went (it is then under way). One with no clip to play does nothing.</summary>
    public bool Execute(Agent agent)
    {
        var done = (Template.Animations.Count > 0 || this is SpecialAbilityBehaviour || this is BlockBehaviour || this is JumpBehaviour) && Act(agent);
        if (done)
        {
            agent.BusyAbility = this;
        }
        return done;
    }

    protected abstract bool Act(Agent agent);

    /// <summary>Whether the current target is within its reach on the flat (the radii counted).</summary>
    public virtual bool InRange(Agent agent)
    {
        if (agent.CurrentTarget is not { } target)
        {
            return false;
        }
        var max = MaxRange(agent) + agent.Owner.Radius + target.Radius;
        var min = MathF.Max(MinRange(agent) - target.Radius, 0);
        var distance = Vector3.DistanceSquared(agent.Owner.Position with { Y = 0 }, target.Position with { Y = 0 });
        return distance <= max * max && distance >= min * min;
    }

    /// <summary>Whether it faces the current target within its arc (always, when they stand together).</summary>
    public virtual bool FacingTarget(Agent agent)
    {
        if (agent.CurrentTarget is not { } target)
        {
            return false;
        }
        var way = (target.Position - agent.Owner.Position) with { Y = 0 };
        var length = way.Length();
        if (length < 1e-6f)
        {
            return true;
        }
        var facing = agent.Owner.Body.Facing;
        var cos = Math.Clamp(Vector3.Dot(facing, way / length), -1, 1);
        return MathF.Acos(cos) < Arc(agent);
    }
}

/// <summary>A grip's ability (grips come with OM-33): never wanted until then.</summary>
internal sealed class PendingAbility(AbilityTemplate template, int index) : NpcAbility(template, index)
{
    protected override bool IsUseful(Agent agent, in FuzzyArguments args) => false;

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => float.MinValue;

    public override float MinRange(Agent agent) => 0;

    public override float MaxRange(Agent agent) => 0;

    public override float Arc(Agent agent) => 0;

    protected override bool Act(Agent agent) => false;
}

/// <summary>
/// Chanting a spell's elements, then casting it (§7.8). While it is the next ability and nothing is
/// under way or cast, its target within 1.25 times its reach, and no attack playing, it chants one
/// element each chant interval (a weapon spell: all at once). With all chanted, using it sets the
/// spell's power and cast type and plays its clip, whose cast action casts.
/// </summary>
public class CastSpellBehaviour(CastSpellAbility template, int index) : NpcAbility(template, index)
{
    public CastSpellAbility Spell => (CastSpellAbility)Template;

    public override void Update(Agent agent, float dt)
    {
        var owner = agent.Owner;
        if (ReferenceEquals(agent.BusyAbility, agent.NextAbility) || owner.CurrentSpell is not null || !(agent.ChantCooldown < 0)
            || owner.SpellQueue.Count >= Spell.Elements.Count || owner.Attacking || agent.CurrentTarget is not { } target
            || !(Vector3.DistanceSquared(owner.Position, target.Position) <= Spell.MaxRange * Spell.MaxRange * 1.25f * 1.25f))
        {
            return;
        }
        agent.ChantCooldown = Spell.ChantSpeed;
        if (Spell.CastType == CastType.Weapon)
        {
            foreach (var element in Spell.Elements)
            {
                owner.Conjure(element);
            }
        }
        else
        {
            owner.Conjure(Spell.Elements[owner.SpellQueue.Count]);
        }
    }

    /// <summary>As the original's, a spell turns to face its target; on itself, ahead.</summary>
    public override Vector3 DesiredDirection(Agent agent) =>
        ReferenceEquals(agent.CurrentTarget, agent.Owner) ? agent.Owner.Body.Facing : base.DesiredDirection(agent);

    /// <summary>Whether the elements chanted so far begin this spell's (each sharing a flag, as far as both go).</summary>
    public bool Matches(ElementQueue queue)
    {
        var count = Math.Min(Spell.Elements.Count, queue.Count);
        for (var i = 0; i < count; i++)
        {
            if ((Spell.Elements[i] & queue.Entries[i].Element) == 0)
            {
                return false;
            }
        }
        return true;
    }

    protected override float OwnDesirability(Agent agent, in FuzzyArguments args) => 0;

    public override float MinRange(Agent agent) => Spell.MinRange;

    public override float MaxRange(Agent agent) => Spell.MaxRange;

    public override float Arc(Agent agent) => Spell.Arc;

    protected override bool Act(Agent agent)
    {
        var owner = agent.Owner;
        if (ReferenceEquals(agent.BusyAbility, agent.NextAbility) || owner.SpellQueue.Count < Spell.Elements.Count || owner.Attacking)
        {
            return false;
        }
        (owner.SpellPower, owner.CastType, agent.ChantCooldown) = (Spell.Power, Spell.CastType, Spell.ChantSpeed);
        owner.PlayAttack(Pick(agent), allowRotate: true);
        return true;
    }
}

/// <summary>
/// Finishing a queue no ability wants any more: cast as the last spell ability did, of the elements
/// chanted (§7.4). It is wanted just above nothing. As the original's, it counts as the first
/// ability, whose cooldown its use starts.
/// </summary>
public sealed class SpellRecovery(CastSpellBehaviour last, ElementQueue chanted)
    : CastSpellBehaviour(last.Spell with { Elements = [.. chanted.Entries.Select(e => e.Element)], Desire = null }, 0)
{
}
