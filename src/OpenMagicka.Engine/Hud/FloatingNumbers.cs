using System.Globalization;
using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Engine.Hud;

/// <summary>A number over a head: where, its text (digits, a heal's with a leading '+'), its colour, its scale (of 22 × 32 a glyph) and alpha.</summary>
public readonly record struct FloatingNumber(Vector3 At, string Text, Vector4 Colour, float Scale, float Alpha);

/// <summary>
/// The damage and heal numbers over characters' heads (docs/engine/ARCHITECTURE.md §19), read from their health each
/// tick: a blow's number follows its target for a third of a second, adding the blows that come meanwhile, then shows
/// 0.4 s; burning, poison, bleeding and healing add up and show once a second for 0.7 s. A number stays opaque and
/// drops away at its very end; a big one is bigger.
/// </summary>
public sealed class FloatingNumbers
{
    public const float BlowLock = 0.333f, BlowLife = 0.4f, TallyEvery = 1, TallyLife = 0.7f, MaxScaleGain = 2, ScaleStep = 1000;

    public static readonly Vector4 Damage = new(1, 0.9f, 0, 1), Heal = new(0, 1, 0, 1), Burning = new(1.8f, 0.6f, 0.4f, 1), Poison = new(1, 1.2f, 0, 1),
        Bleeding = new(1.25f, 0.125f, 0.063f, 1);

    private enum Kind : byte { Burning, Poison, Bleeding, Heal }

    private sealed class Watch(float health, int blows)
    {
        public float Health { get; set; } = health;
        public int Blows { get; set; } = blows;
        public float[] Tally { get; } = new float[4];
        public float Clock { get; set; }
        public Shown? Blow { get; set; }
    }

    private sealed class Shown(Character? follow, Vector3 at, float value, Vector4 colour, float life, float locked)
    {
        public Character? Follow { get; set; } = follow;
        public Vector3 At { get; set; } = at;
        public float Value { get; set; } = value;
        public Vector4 Colour { get; } = colour;
        public float Life { get; } = life;
        public float Left { get; set; } = life;
        public float Locked { get; set; } = locked;
    }

    private readonly Dictionary<Character, Watch> watched = new(ReferenceEqualityComparer.Instance);
    private readonly List<Shown> shown = [];
    private readonly List<Character> gone = [];
    private readonly HashSet<Character> seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>The most numbers shown at once (as the original's).</summary>
    public const int MaxShown = 1024;

    public int Count => shown.Count;

    /// <summary>A tick, after the world's: health changes become numbers; heals wait outside cutscenes.</summary>
    public void Update(float dt, IEnumerable<Character> characters, bool inCutscene)
    {
        ArgumentNullException.ThrowIfNull(characters);
        seen.Clear();
        foreach (var character in characters)
        {
            seen.Add(character);
            if (!watched.TryGetValue(character, out var watch))
            {
                watched[character] = new Watch(character.HitPoints, character.BlowsTaken);
                continue;
            }
            var change = character.HitPoints - watch.Health;
            var struck = character.BlowsTaken != watch.Blows;
            (watch.Health, watch.Blows) = (character.HitPoints, character.BlowsTaken);
            if (change < 0 && struck)
            {
                Blow(character, watch, -change);
            }
            else if (change < 0 && StatusKind(character) is { } status)
            {
                watch.Tally[(int)status] += -change;                          // a loss of no blow and no status (a script's) shows nothing
            }
            else if (change > 0)
            {
                watch.Tally[(int)Kind.Heal] += change;
            }
            watch.Clock += dt;
            if (watch.Clock >= TallyEvery)
            {
                watch.Clock = 0;
                Flush(character, watch, inCutscene);
            }
        }
        gone.Clear();
        foreach (var character in watched.Keys)
        {
            if (!seen.Contains(character))
            {
                gone.Add(character);
            }
        }
        foreach (var character in gone)
        {
            watched.Remove(character);
        }
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            var number = shown[i];
            if (number.Locked > 0)
            {
                number.Locked -= dt;
                if (number.Follow is { } follow)
                {
                    number.At = Head(follow);
                }
            }
            else if ((number.Left -= dt) <= 0)
            {
                shown.RemoveAt(i);
            }
        }
    }

    /// <summary>Everything forgotten (the numbers turned off).</summary>
    public void Clear()
    {
        watched.Clear();
        shown.Clear();
    }

    public void Write(List<FloatingNumber> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var number in shown)
        {
            var magnitude = MathF.Round(MathF.Abs(number.Value));
            if (magnitude < 1)
            {
                continue;
            }
            var text = (number.Value < 0 ? "+" : "") + magnitude.ToString(CultureInfo.InvariantCulture);
            var scale = (1 + MathF.Min(MaxScaleGain, magnitude / ScaleStep)) * 0.5f;
            var alpha = 1 - MathF.Pow(1 - Math.Clamp(number.Left / number.Life, 0, 1), 5);
            into.Add(new FloatingNumber(number.At, text, number.Colour, scale, alpha));
        }
    }

    /// <summary>A character's head: its centre raised by half its length and its radius.</summary>
    public static Vector3 Head(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.Position + Vector3.UnitY * (character.Template.Length / 2 + character.Template.Radius);
    }

    private void Blow(Character character, Watch watch, float amount)
    {
        if (watch.Blow is { Locked: > 0 } open)
        {
            open.Value += amount;                                             // blows within a third of a second add up
            return;
        }
        watch.Blow = new Shown(character, Head(character), amount, Damage, BlowLife, BlowLock);
        Show(watch.Blow);
    }

    private void Flush(Character character, Watch watch, bool inCutscene)
    {
        for (var kind = 0; kind < watch.Tally.Length; kind++)
        {
            var amount = watch.Tally[kind];
            if (amount >= 0.5f && !(kind == (int)Kind.Heal && inCutscene))
            {
                var colour = (Kind)kind switch { Kind.Burning => Burning, Kind.Poison => Poison, Kind.Bleeding => Bleeding, _ => Heal };
                Show(new Shown(null, Head(character), kind == (int)Kind.Heal ? -amount : amount, colour, TallyLife, 0));
            }
            watch.Tally[kind] = 0;
        }
    }

    private void Show(Shown number)
    {
        if (shown.Count < MaxShown)
        {
            shown.Add(number);
        }
    }

    private static Kind? StatusKind(Character character) =>
        character.Statuses.Has(StatusEffects.Burning) ? Kind.Burning : character.Statuses.Has(StatusEffects.Poisoned) ? Kind.Poison
        : character.Statuses.Has(StatusEffects.Bleeding) ? Kind.Bleeding : null;
}
