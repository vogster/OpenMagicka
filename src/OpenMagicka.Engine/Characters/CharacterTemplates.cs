using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;
using Shapes = OpenMagicka.Physics.Characters;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's character template as the gameplay core needs it.</summary>
public static class CharacterTemplates
{
    /// <summary>The template with its slots filled from the items it refers to (from <paramref name="templateAsset"/>).</summary>
    public static Gameplay.Characters.CharacterTemplate From(ContentLoader loader, string templateAsset, CharacterTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(content);
        var equipment = content.Equipment
            .Where(e => e.Item is not null)
            .Select(e => new Gameplay.Characters.EquipmentTemplate(e.Slot, e.Bone, new System.Numerics.Vector3(e.Rotation.X, e.Rotation.Y, e.Rotation.Z),
                ItemTemplates.Load(loader, templateAsset, e.Item!)))
            .ToList();
        return From(content) with { Equipment = equipment };
    }

    /// <summary>The template alone, with empty slots.</summary>
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
            ItemTemplates.Resistances(content.Resistances),
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
            Flies: content.MoveAnimations.Any(m => m.Movement.HasFlag(MovementProperties.Fly)),
            ModelScale: content.Models.Count > 0 && content.Models[0].Scale > 0 ? content.Models[0].Scale : 1);   // the first variant until spawns pick
    }
}
