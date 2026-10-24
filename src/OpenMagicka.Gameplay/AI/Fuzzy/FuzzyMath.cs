using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>The fuzzy measures the AI weighs by, each between 0 and 1 (ENTITIES_AI.md §7.5).</summary>
public static class FuzzyMath
{
    /// <summary>A bell centred on the middle of the range: 1 there, a tenth at its ends, falling exponentially.</summary>
    public static float DistanceExponential(float distance, float min, float max)
    {
        var middle = ((double)min + max) * 0.5;
        var steepness = Math.Pow(Math.E, -Math.Log(10) / (middle - max));
        return (float)Math.Pow(steepness, -Math.Abs(distance - middle));
    }

    /// <summary>The bell over (−max, max).</summary>
    public static float DistanceExponential(float distance, float max) => DistanceExponential(distance, -max, max);

    /// <summary>A tent over the range: 1 at its middle, 0 at its ends (and below 0 beyond them).</summary>
    public static float DistanceLinear(float distance, float min, float max)
    {
        var middle = (max + min) * 0.5f;
        var slope = 2f / (max - min);
        return distance < middle ? (distance - min) * slope : (max - distance) * slope;
    }

    /// <summary>How much a grudge of <paramref name="grudge"/> angers: 1 − 0.977237221^grudge.</summary>
    public static float Anger(float grudge) => 1 - (float)Math.Pow(0.977237221, grudge);

    /// <summary>(a·b + 1) / 2: 1 along, 0 against.</summary>
    public static float Along(Vector3 a, Vector3 b) => (Vector3.Dot(a, b) + 1) * 0.5f;

    /// <summary>1 − 0.5^n for the n other characters within the range sharing (or, for enemies, sharing none of) the factions.</summary>
    public static float Density(EntityManager? manager, Entity around, Factions factions, float range, bool friendly)
    {
        if (manager is null)
        {
            return 1;
        }
        var count = manager.Query(around.Position, range).OfType<Character>()
            .Count(c => !ReferenceEquals(c, around) && (c.Factions & factions) != 0 == friendly);
        return 1 - (float)Math.Pow(0.5, count);
    }

    /// <summary>
    /// How much a character threatens the thinker with the elements: how squarely it faces it, or how
    /// near it is (within 20), times a fifth of those elements in its queue (the fourth root of that while
    /// it charges or casts). Attacking, only its facing and nearness count.
    /// </summary>
    public static float Threat(Character self, Character threat, Elements elements)
    {
        var way = self.Position - threat.Position;
        var distance = way.Length();
        if (!(distance > 1e-6f))
        {
            return 1;
        }
        way /= distance;
        if (threat.State == CharacterStates.Attack)
        {
            return Along(way, threat.Body.Facing) * DistanceExponential(distance, 20);
        }
        var aim = MathF.Max(Along(way, threat.Body.Facing), DistanceExponential(distance, 20));
        var spell = threat.SpellQueue.Combine();
        var strength = 0f;
        for (var i = 0; i < 11; i++)
        {
            var element = (Elements)(1 << i);
            if ((element & elements) == element)
            {
                strength += spell[element];
            }
        }
        strength *= 0.2f;
        if (threat.State == CharacterStates.Charge || threat.State == CharacterStates.Cast)
        {
            strength = MathF.Pow(strength, 0.25f);
        }
        return aim * strength;
    }

    /// <summary>
    /// 1 − 0.25^(the threats of the characters of other factions within the alert radius, plus the
    /// thinker's own danger, plus 1 when lightning is asked about and it was just shocked).
    /// </summary>
    public static float Danger(Character self, float alertRadius, Elements elements)
    {
        if (self.Manager is not { } manager)
        {
            return 1;
        }
        var sum = manager.Query(self.Position, alertRadius, ignoreProtected: true).OfType<Character>()
            .Where(c => !ReferenceEquals(c, self) && (c.Factions & self.Factions) == 0)
            .Sum(c => Threat(self, c, elements));
        sum += self.Danger;
        if (self.ZapTimer > 0 && (elements & Elements.Lightning) != 0)
        {
            sum += 1;
        }
        return 1 - (float)Math.Pow(0.25, sum);
    }
}
