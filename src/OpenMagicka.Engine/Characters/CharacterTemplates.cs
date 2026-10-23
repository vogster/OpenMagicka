using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;
using Shapes = OpenMagicka.Physics.Characters;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's character template as the gameplay core needs it.</summary>
public static class CharacterTemplates
{
    public static Gameplay.Characters.CharacterTemplate From(CharacterTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stats = content.Stats;
        return new Gameplay.Characters.CharacterTemplate(
            content.Id,
            (Gameplay.Characters.Factions)(int)content.Factions,
            stats.MaxHitpoints,
            MathF.Max(stats.Length, Shapes.CharacterShape.MinLength),   // the original's reader raises it so (67 templates have 0)
            stats.Radius,
            stats.Mass,
            stats.Speed,
            stats.TurnSpeed,
            Resistances(content.Resistances),
            Regeneration: stats.Regeneration,
            HitTolerance: stats.HitTolerance,
            KnockdownTolerance: stats.KnockdownTolerance,
            ZapModifier: stats.ZapModifier,
            BleedRate: stats.BleedRate,
            StunTime: stats.StunTime,
            Undying: stats.Undying,
            UndieTime: stats.UndieTime,
            UndieHitPoints: stats.UndieHitPoints,
            IsEthereal: content.IsEthereal,
            Flies: content.MoveAnimations.Any(m => m.Movement.HasFlag(MovementProperties.Fly)));
    }

    /// <summary>The template's overrides on top of the neutral table, one element each.</summary>
    private static ResistanceTable Resistances(IReadOnlyList<ResistanceContent> overrides) =>
        overrides.Aggregate(ResistanceTable.Neutral, (table, r) =>
            table.With((Gameplay.Spells.Elements)(int)r.Elements, r.Multiplier, r.Modifier, r.StatusResistance));
}
