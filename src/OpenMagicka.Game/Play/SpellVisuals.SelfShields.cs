using System.Numerics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The self shields, as the original's (docs/engine/ARCHITECTURE.md §17): a bubble is the character's own mesh again,
/// swollen and shimmering, flashing a third of a second after each blow; an armour is <c>combined_armor</c> on the
/// character's bones, its earth part, its ice part or both. Each blow bursts where it struck, and an armour bursts as
/// it falls away.
/// </summary>
public sealed partial class SpellVisuals
{
    public const string Armour = "Models/Effects/combined_armor";

    /// <summary>How long a bubble flashes after a blow, and how much.</summary>
    public const float FlashTime = 0.333f, FlashGain = 60, BubbleBrightness = 4;

    private readonly Dictionary<Character, (SelfShield Shield, SelfShieldLook Look)> selfShields = new(ReferenceEqualityComparer.Instance);

    /// <summary>What lasts of a self shield's look between frames: its noise's drift and the time since its last blow.</summary>
    public sealed class SelfShieldLook
    {
        public Vector2 Noise0 { get; private set; }
        public Vector2 Noise1 { get; private set; }
        public Vector2 Noise2 { get; private set; }
        public float SinceHit { get; set; } = 1;

        public float Brightness => BubbleBrightness + MathF.Max(FlashTime - SinceHit, 0) * FlashGain;

        public void Update(float dt, SelfShield shield)
        {
            SinceHit += dt;
            var wear = 0.5f * (shield.MaxHitPoints - Math.Clamp(shield.HitPoints, 0, shield.MaxHitPoints)) / shield.MaxHitPoints + 0.05f;
            Noise2 += new Vector2(0, -wear * dt);
            Noise0 += new Vector2(-0.7f * wear * dt, 0.3f * wear * dt);
            Noise1 += new Vector2(0.4f * wear * dt, 0.7f * wear * dt);
        }
    }

    private void AgeSelfShields(float dt)
    {
        foreach (var character in world.Entities.Entities.OfType<Character>())
        {
            if (character.SelfShield is SelfShield { IsActive: true } shield
                && !(selfShields.TryGetValue(character, out var known) && ReferenceEquals(known.Shield, shield)))
            {
                if (selfShields.TryGetValue(character, out var replaced) && replaced.Shield.IsArmour)
                {
                    FallAway(character.Position, replaced.Shield.Spell);       // a new cast takes the old armour's place
                }
                selfShields[character] = (shield, new SelfShieldLook());
            }
        }
        foreach (var (character, (shield, look)) in selfShields.ToList())
        {
            if (character.Manager is null || !ReferenceEquals(character.SelfShield, shield) || !shield.IsActive)
            {
                selfShields.Remove(character);
                if (shield.IsArmour)
                {
                    FallAway(character.Position, shield.Spell);
                }
                continue;
            }
            look.Update(dt, shield);
        }
    }

    private void FallAway(Vector3 at, Spell spell)
    {
        foreach (var (element, effect) in new[] { (Elements.Earth, "barrier_earth_death"), (Elements.Ice, "barrier_ice_death") })
        {
            if (spell[element] > 0)
            {
                effects.Once(effect, Matrix4x4.CreateTranslation(at));
            }
        }
    }

    private void Strike(SelfShieldHitCue hit)
    {
        var way = Vector3.Normalize(hit.At - hit.Owner.Position + new Vector3(0.1f, 0, 0));
        var at = EffectDirector.Facing(hit.At, way);
        if (!hit.Shield.IsArmour)
        {
            effects.Once("pure_self_shield_hit", at);
            if (selfShields.TryGetValue(hit.Owner, out var known) && ReferenceEquals(known.Shield, hit.Shield))
            {
                known.Look.SinceHit = 0;
            }
            return;
        }
        if (hit.Shield.Spell.Earth > 0)
        {
            effects.Once("earth_self_shield_hit", at);
        }
        if (hit.Shield.Spell.Ice > 0)
        {
            effects.Once("ice_self_shield_hit", at);
        }
    }

    /// <summary>The armours on their wearers' bones (its earth part, its ice part).</summary>
    private void WriteArmours(List<DrawnModel> into)
    {
        foreach (var (character, (shield, _)) in selfShields)
        {
            var parts = (shield.Spell.Earth > 0 ? 1 : 0) | (shield.Spell.Ice > 0 ? 2 : 0);
            if (shield.IsArmour && parts != 0 && character.BoneTransforms is { Length: > 0 } bones)
            {
                into.Add(new DrawnModel(Armour, character.RootMatrix(), bones.ToArray(), Vector3.One, parts));
            }
        }
    }

    /// <summary>The bubbles, on their wearers' models.</summary>
    private void WriteBubbles(SpellPictures into)
    {
        foreach (var (character, (shield, look)) in selfShields)
        {
            if (!shield.IsArmour && looks(character.Template) is { } model && character.BoneTransforms is { Length: > 0 } bones)
            {
                var palette = new Xna.Matrix[bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    palette[i] = ModelCache.Xna(bones[i]);
                }
                into.Bubbles.Add(new BubblePicture(model.Asset, palette, ToXna(character.Position),
                    ToXna(character.Body.Facing), look.Brightness, ToXna2(look.Noise0), ToXna2(look.Noise1), ToXna2(look.Noise2)));
            }
        }
    }
}
