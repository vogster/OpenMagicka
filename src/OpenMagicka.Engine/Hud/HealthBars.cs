using System.Numerics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Engine.Hud;

/// <summary>Which health bars the HUD shows (the original's option; On by default).</summary>
public enum HealthBarOption : byte { On, PlayersOnly, Off }

/// <summary>
/// The original HUD's bars under the feet (docs/engine/ARCHITECTURE.md §19), gathered from the world each frame:
/// <list type="bullet">
/// <item>every living player's, red (green while poisoned), always shown;</item>
/// <item>a hurt NPC's, shown for 2 s after a blow and fading over the next; one with several bars stacks them, 6 px
/// apart: the first red and full, the middle grey and full, the last its share in the shield's gold;</item>
/// <item>a ward's (its health, in its spell's colour) and each self-cast aura's (its time left, gold) under them;</item>
/// <item>each shield's (its health, in its tint).</item>
/// </list>
/// A blow makes a bar flash white for 0.3 s. None while the stage's UI is off.
/// </summary>
public static class HealthBars
{
    /// <summary>The most bars a frame shows (as the original's).</summary>
    public const int MaxBars = 512;

    public const float FlashTime = 0.3f, FlashGain = 4, ShownTime = 2, FadeTime = 1, StackOffset = 6, MaxWidth = 1.5f, PlayerHeight = 1.25f;

    public static readonly Vector3 Red = new(1, 0, 0), Green = new(0, 1, 0), Grey = new(0.6f, 0.6f, 0.6f), Gold = SpellColours.Shield;

    /// <summary>The bars of the world's entities into the list (cleared first).</summary>
    public static void Collect(IEnumerable<Entity> entities, HealthBarOption option, bool uiEnabled, List<HealthBarPicture> into)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!uiEnabled || option == HealthBarOption.Off)
        {
            return;
        }
        foreach (var entity in entities)
        {
            if (into.Count >= MaxBars)
            {
                return;
            }
            switch (entity)
            {
                case Character { IsPlayer: true, IsDead: false } player:
                    Character(player, into, fades: false);
                    break;
                case Character { IsPlayer: false, HitPoints: > 0 } npc when option == HealthBarOption.On:
                    Character(npc, into, fades: true);
                    break;
                case SpellShield shield:
                    Shield(shield, into);
                    break;
            }
        }
    }

    /// <summary>A character's bars at its feet.</summary>
    private static void Character(Character character, List<HealthBarPicture> into, bool fades)
    {
        var template = character.Template;
        var feet = character.Position - Vector3.UnitY * (template.Length / 2 + template.Radius);
        var since = MathF.Min(character.TimeSinceDamage, character.TimeSinceStatusDamage);
        var alpha = fades ? Math.Clamp(ShownTime + FadeTime - since, 0, 1) : 1;
        var width = MathF.Min(template.Radius, MaxWidth);
        var share = character.MaxHitPoints > 0 ? character.HitPoints / character.MaxHitPoints : 0;
        var poisoned = character.Statuses.Has(StatusEffects.Poisoned);
        var stacked = Math.Max(1, template.HealthBars);
        if (alpha > 0)
        {
            var height = character.IsPlayer ? PlayerHeight : 1;
            for (var i = 0; i < stacked; i++)
            {
                var last = i == stacked - 1;
                var colour = stacked == 1 || last ? (poisoned ? Green : stacked == 1 ? Red : Gold) : i == 0 ? Red : Grey;
                into.Add(Bar(feet, last ? share : 1, width, height, last ? since : MathF.Max(since, FlashTime), colour, alpha, i * StackOffset));
            }
        }
        var below = stacked * StackOffset;
        if (character.SelfShield is SelfShield { IsActive: true } ward)
        {
            into.Add(Bar(feet, ward.HitPoints / ward.MaxHitPoints, width, 1, ward.TimeSinceDamage, SpellColours.Of(ward.Spell), 1, below));
        }
        foreach (var aura in character.Auras)
        {
            if (!aura.SelfCasted || !(aura.Aura.Ttl > 0))
            {
                continue;
            }
            into.Add(Bar(character.Position, aura.Ttl / aura.Aura.Ttl, width, 1, float.PositiveInfinity, Gold, 1, below));   // as the original: at the centre
        }
    }

    /// <summary>A shield's bar: before a sphere, at a disc's front, at a wall's middle.</summary>
    private static void Shield(SpellShield shield, List<HealthBarPicture> into)
    {
        var (at, lower) = shield.Shape switch
        {
            ShieldShape.Sphere => (shield.Position + Vector3.UnitZ * shield.Radius, 12f),
            ShieldShape.Disc => (shield.Position + shield.Facing * shield.ShellRadius, 16f),
            _ => (shield.Position, 24f),
        };
        into.Add(Bar(at, shield.HitPoints / SpellShield.MaxHitPoints, MathF.Min(shield.Radius, MaxWidth), 1, float.PositiveInfinity, shield.Colour, 1, lower));
    }

    /// <summary>A bar: its colour lit by a blow's flash (a white 1.2 fading over 0.3 s), its share clamped.</summary>
    public static HealthBarPicture Bar(Vector3 at, float share, float width, float height, float sinceBlow, Vector3 colour, float alpha, float lower)
    {
        var flash = Math.Clamp(FlashTime - sinceBlow, 0, FlashTime) * FlashGain;
        var lit = colour + new Vector3(flash);
        return new HealthBarPicture(new Microsoft.Xna.Framework.Vector3(at.X, at.Y, at.Z), Math.Clamp(float.IsFinite(share) ? share : 0, 0, 1), width, height,
            new Microsoft.Xna.Framework.Vector4(lit.X, lit.Y, lit.Z, alpha), lower);
    }
}
