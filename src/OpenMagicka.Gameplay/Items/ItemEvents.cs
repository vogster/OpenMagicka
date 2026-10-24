using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Items;

/// <summary>What sets off a group of events (docs/engine/ENTITIES_AI.md §8.2).</summary>
[Flags]
public enum EventTrigger : byte
{
    None = 0,
    Default = 0x1,
    Hit = 0x2,
    Collision = 0x4,
    Damaged = 0x8,
    Timer = 0x10,
    Death = 0x20,
    OverKill = 0x40,
}

/// <summary>What happened, for the conditions to test.</summary>
/// <param name="Position">Where the events happen, instead of at their source.</param>
public readonly record struct EventContext(EventTrigger Trigger, float Threshold = 0, int HitPoints = 0, Elements Elements = Elements.None,
    float Time = 0, Vector3? Position = null);

/// <summary>A group's condition: its triggers and their thresholds; without repeat it fires once.</summary>
public sealed record EventCondition(EventTrigger Trigger, int HitPoints = 0, Elements Elements = Elements.None, float Threshold = 0, float Time = 0,
    bool Repeat = true);

/// <summary>One event of a group.</summary>
public abstract record ItemEvent;

/// <summary>Damages the target, from the source's position.</summary>
public sealed record DamageEvent(DamageEntry Damage, bool VelocityBased = false) : ItemEvent;

/// <summary>Damages the source's owner.</summary>
public sealed record DamageOwnerEvent(DamageEntry Damage, bool VelocityBased = false) : ItemEvent;

/// <param name="Magnitude">The cue's magnitude variable.</param>
public sealed record SoundEvent(string Cue, int Banks, float Magnitude) : ItemEvent;

/// <param name="Follow">Stays on the source while it lives.</param>
/// <param name="WorldAlign">Points up rather than along the source.</param>
public sealed record EffectEvent(string Effect, bool Follow, bool WorldAlign) : ItemEvent;

/// <summary>A condition and the events it sets off.</summary>
public sealed record EventGroup(EventCondition Condition, IReadOnlyList<ItemEvent> Events)
{
    /// <summary>Throws on what the events could not act on (loaders call it as they read).</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Condition);
        ArgumentNullException.ThrowIfNull(Events);
        if (!float.IsFinite(Condition.Threshold) || !float.IsFinite(Condition.Time))
        {
            throw new ArgumentOutOfRangeException(nameof(Condition), Condition, "thresholds must be finite");
        }
        foreach (var e in Events)
        {
            var valid = e switch
            {
                DamageEvent d => float.IsFinite(d.Damage.Amount) && float.IsFinite(d.Damage.Magnitude),
                DamageOwnerEvent d => float.IsFinite(d.Damage.Amount) && float.IsFinite(d.Damage.Magnitude),
                SoundEvent s => s.Cue is not null && float.IsFinite(s.Magnitude),
                EffectEvent f => f.Effect is not null,
                _ => e is not null,
            };
            if (!valid)
            {
                throw new ArgumentOutOfRangeException(nameof(Events), e, "an event the rules cannot act on");
            }
        }
    }
}

/// <summary>
/// A copy of a source's event groups with each condition's own state (whether it fired, a timer's
/// count), as each item of the original keeps its own. Changes in place.
/// </summary>
public sealed class EventGroups
{
    private readonly bool[] activated;
    private readonly int[] counts;
    private readonly EventTrigger triggers;

    public EventGroups(IReadOnlyList<EventGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Groups = groups;
        (activated, counts) = (new bool[groups.Count], new int[groups.Count]);
        triggers = groups.Aggregate(EventTrigger.None, (all, g) => all | g.Condition.Trigger);
    }

    public static EventGroups None { get; } = new([]);

    public IReadOnlyList<EventGroup> Groups { get; }

    public bool Any => Groups.Count > 0;

    /// <summary>Whether a group of this trigger is among them.</summary>
    public bool Has(EventTrigger trigger) => (triggers & trigger) != 0;

    /// <summary>The groups whose condition the context meets, in order; each runs its events through <paramref name="run"/>.</summary>
    public DamageResult Fire(in EventContext context, Func<ItemEvent, Vector3?, DamageResult> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = DamageResult.None;
        for (var i = 0; i < Groups.Count; i++)
        {
            if (IsMet(i, context))
            {
                foreach (var e in Groups[i].Events)
                {
                    result |= run(e, context.Position);
                }
            }
        }
        return result;
    }

    /// <summary>As the original: the first trigger bit both share decides, once unless repeating.</summary>
    private bool IsMet(int group, in EventContext context)
    {
        var condition = Groups[group].Condition;
        var shared = context.Trigger & condition.Trigger;
        if (shared == EventTrigger.None || (activated[group] && !condition.Repeat))
        {
            return false;
        }
        for (var bit = 0; bit < 7; bit++)
        {
            var met = (EventTrigger)(1 << bit) & shared;
            if (met is EventTrigger.Default or EventTrigger.Hit or EventTrigger.Death or EventTrigger.OverKill
                || (met == EventTrigger.Collision && context.Threshold >= condition.Threshold)
                || (met == EventTrigger.Damaged && context.HitPoints >= condition.HitPoints && (context.Elements & condition.Elements) != 0))
            {
                return activated[group] = true;
            }
            if (met == EventTrigger.Timer && context.Time >= condition.Time * (counts[group] + 1))
            {
                counts[group]++;
                return activated[group] = true;
            }
        }
        return false;
    }
}
