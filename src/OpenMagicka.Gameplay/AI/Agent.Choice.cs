using OpenMagicka.Gameplay.AI.Fuzzy;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.AI;

public sealed partial class Agent
{
    /// <summary>How far it looks for targets, and how many it keeps of each kind (foes, and the rest).</summary>
    public const float SearchRadius = 40;

    public const int Ranked = 4;

    /// <summary>
    /// Grudges fade by 25 a second, capped at 500, and go when spent. One against a wizard whose
    /// staff calms (or, for the undead, deters them) halves each time it thinks, as the original.
    /// </summary>
    private void Fade(float dt)
    {
        for (var i = grudges.Count - 1; i >= 0; i--)
        {
            var (attacker, grudge) = grudges[i];
            grudge = MathF.Min(grudge - GrudgeDecay * dt, MaxGrudge);
            if (grudge < 0)
            {
                grudges.RemoveAt(i);
                continue;
            }
            var passive = attacker.IsPlayer ? attacker.HeldItem(Character.ResistanceSlot)?.Template.PassiveAbility : null;
            var calmed = passive == PassiveAbility.ReduceAggro || (passive == PassiveAbility.ZombieDeterrent && (Owner.Factions & Factions.Undead) == Factions.Undead);
            grudges[i] = (attacker, calmed ? grudge * 0.5f : grudge);
        }
    }

    /// <summary>
    /// A blow from a character (amount × magnitude) becomes a grudge, unless the attacker shares a
    /// faction. A wizard's blow is also held against it by the NPCs within the alert radius sharing a
    /// faction with this one (and not with the wizard); as the original, this one is among them and
    /// takes it twice.
    /// </summary>
    public void Grudge(Character attacker, float amount)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        var mine = Add(attacker, (attacker.Factions & Owner.Factions) == 0 ? amount : 0);
        if (!(mine > float.Epsilon))
        {
            grudges.RemoveAll(g => ReferenceEquals(g.Attacker, attacker));
            return;
        }
        if (!attacker.IsPlayer || Owner.Manager is not { } manager)
        {
            return;
        }
        foreach (var other in manager.Query(Owner.Position, AlertRadius).OfType<Character>())
        {
            if (!other.IsPlayer && other.Mind is { } mind && (Owner.Factions & other.Factions) != 0)
            {
                var share = (attacker.Factions & other.Factions) != 0 ? 0 : amount;
                if (share > float.Epsilon)
                {
                    mind.Add(attacker, share);
                }
            }
        }
    }

    private float Add(Character attacker, float amount)
    {
        var index = grudges.FindIndex(g => ReferenceEquals(g.Attacker, attacker));
        var grudge = (index >= 0 ? grudges[index].Grudge : 0) + amount;
        if (index >= 0)
        {
            grudges[index] = (attacker, grudge);
        }
        else
        {
            grudges.Add((attacker, grudge));
        }
        return grudge;
    }

    /// <summary>
    /// Chooses whom to go for and with what (§7.4). Of the characters within 40, every wizard and the
    /// priority target, each is scored by anger, wounds and nearness (times its aggro multiplier), +1
    /// for the priority target; the best four foes and the best four others are kept, each with the
    /// best ability against it added. Itself, with its best ability on itself, may win. The score is returned.
    /// </summary>
    public float ChooseTarget(out Entity? target, out NpcAbility? ability)
    {
        Array.Fill(ranked, (null, float.MinValue, null));
        foreach (var candidate in Candidates())
        {
            var character = candidate as Character;
            if (!candidate.IsDamageable || candidate.IsDead || ReferenceEquals(candidate, Owner)
                || (!ReferenceEquals(candidate, PriorityTarget) && character is null)
                || (character is not null && character.IsInvisible && !Owner.Template.CanSeeInvisible))
            {
                continue;
            }
            var score = ReferenceEquals(candidate, PriorityTarget) ? 1f : 0f;
            score += FuzzyMath.Anger(GrudgeAgainst(candidate)) * Profile.AngerWeight;
            score += (1 - Health(candidate)) * Profile.HealthWeight;
            score += FuzzyMath.DistanceExponential(System.Numerics.Vector3.Distance(Owner.Position, candidate.Position), AlertRadius) * Profile.DistanceWeight;
            score *= character?.AggroMultiplier ?? 1;
            var foe = character is not null && (character.Factions & Owner.Factions) == 0;
            Rank(ranked, foe ? 0 : Ranked, candidate, score);
        }
        for (var i = 0; i < ranked.Length; i++)
        {
            if (ranked[i].Target is { } ranking)
            {
                ranked[i].Score += ChooseAbility(new FuzzyArguments(Owner, ranking, AlertRadius), out ranked[i].Ability);
            }
        }
        (target, ability) = (null, null);
        var best = float.MinValue;
        foreach (var (who, score, with) in ranked)
        {
            if (score > best)
            {
                (target, ability, best) = (who, with, score);
            }
        }
        var onSelf = ChooseAbility(FuzzyArguments.OnSelf(Owner, AlertRadius), out var selfAbility);
        if (onSelf > best)
        {
            (target, ability, best) = (Owner, selfAbility, onSelf);
        }
        if (ability is not SpellRecovery)
        {
            LastSpellAbility = ability as CastSpellBehaviour;
        }
        return best;
    }

    private readonly (Entity? Target, float Score, NpcAbility? Ability)[] ranked = new (Entity?, float, NpcAbility?)[Ranked * 2];

    /// <summary>
    /// The last ordinary spell ability chosen, which a recovery finishes a queue by. As the original,
    /// any other choice (none too) forgets it.
    /// </summary>
    internal CastSpellBehaviour? LastSpellAbility { get; private set; }

    private IEnumerable<Entity> Candidates()
    {
        var near = Owner.Manager?.Query(Owner.Position, SearchRadius) ?? [];
        var all = new List<Entity>(near);
        foreach (var player in Owner.Manager?.Session?.Players ?? [])
        {
            if (player.Avatar is { } avatar && !all.Contains(avatar))
            {
                all.Add(avatar);
            }
        }
        if (PriorityTarget is { } priority && !all.Contains(priority))
        {
            all.Add(priority);
        }
        return all;
    }

    private static void Rank((Entity? Target, float Score, NpcAbility? Ability)[] ranked, int from, Entity candidate, float score)
    {
        for (var i = from; i < from + Ranked; i++)
        {
            if (score > ranked[i].Score)
            {
                Array.Copy(ranked, i, ranked, i + 1, from + Ranked - 1 - i);
                ranked[i] = (candidate, score, null);
                return;
            }
        }
    }

    private static float Health(Entity entity) => entity is Character c ? c.HitPoints / c.MaxHitPoints : 1;

    /// <summary>
    /// The most wanted ability against the target (§7.4): off cooldown, meant for such a target (self
    /// for Self; a fellow, not itself, for Friendly; anything but a fellow for Enemy), and, with elements
    /// already chanted, a spell of those same elements. +0.5 for the priority ability against the
    /// priority target; the best above 0 wins. With none and elements chanted, a recovery that casts
    /// them as the last spell ability did.
    /// </summary>
    public float ChooseAbility(in FuzzyArguments args, out NpcAbility? ability)
    {
        var best = float.MinValue;
        ability = null;
        var character = args.Target as Character;
        foreach (var candidate in Abilities)
        {
            if (cooldowns[candidate.Index] > 0 || !Suits(candidate.Template.Target, args.Target, character)
                || (Owner.SpellQueue.Count > 0 && !(candidate is CastSpellBehaviour spell && spell.Matches(Owner.SpellQueue))))
            {
                continue;
            }
            var desire = candidate.Desirability(this, args);
            if (candidate.Index == PriorityAbility && ReferenceEquals(args.Target, PriorityTarget))
            {
                desire += 0.5f;
            }
            if (desire > 0 && desire > best)
            {
                (best, ability) = (desire, candidate);
            }
        }
        if (ability is null && Owner.SpellQueue.Count > 0 && LastSpellAbility is { } last && NextAbility is not SpellRecovery
            && last.Template.Target == AbilityTarget.Enemy && (character is null || (character.Factions & Owner.Factions) == 0))
        {
            ability = new SpellRecovery(last, Owner.SpellQueue);
            best = 3e-45f;                                                     // as the original: a hair above nothing
        }
        return best;
    }

    private bool Suits(AbilityTarget aim, Entity target, Character? character) => aim switch
    {
        AbilityTarget.Self => ReferenceEquals(target, Owner),
        AbilityTarget.Friendly => !ReferenceEquals(target, Owner) && character is not null && (character.Factions & Owner.Factions) != 0,
        AbilityTarget.Enemy => character is null || (character.Factions & Owner.Factions) == 0,
        _ => true,
    };
}
