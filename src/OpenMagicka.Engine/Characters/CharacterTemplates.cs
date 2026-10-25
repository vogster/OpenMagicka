using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;
using Shapes = OpenMagicka.Physics.Characters;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's character template as the gameplay core needs it.</summary>
public static class CharacterTemplates
{
    /// <summary>The template with its slots filled from the items it refers to (from <paramref name="templateAsset"/>), through a fresh library.</summary>
    public static Gameplay.Characters.CharacterTemplate From(ContentLoader loader, string templateAsset, CharacterTemplateContent content) =>
        From(new TemplateLibrary(loader), templateAsset, content);

    /// <summary>The template with its slots filled from the library's items, shared with every other template that holds them.</summary>
    public static Gameplay.Characters.CharacterTemplate From(TemplateLibrary library, string templateAsset, CharacterTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(content);
        var equipment = content.Equipment
            .Where(e => e.Item is not null)
            .Select(e => new Gameplay.Characters.EquipmentTemplate(e.Slot, e.Bone, new System.Numerics.Vector3(e.Rotation.X, e.Rotation.Y, e.Rotation.Z),
                library.Item(templateAsset, e.Item!)))
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
            ModelScale: content.Models.Count > 0 && content.Models[0].Scale > 0 ? content.Models[0].Scale : 1,   // the first variant until spawns pick
            Auras: [.. content.Auras.Select(BuffTemplates.Aura)],                 // the template's own buff list is never used, as in the original
            HasGibs: content.Gibs.Count > 0,                                      // gore is taken as on
            Fearless: content.Fearless,
            Uncharmable: content.Uncharmable,
            Nonslippery: content.Nonslippery,
            Ai: AiProfiles.From(content),
            CanSeeInvisible: content.CanSeeInvisible,
            MoveAbilities: content.MoveAnimations.Aggregate(Gameplay.Navigation.NavMovement.Default, (all, m) => all | (Gameplay.Navigation.NavMovement)(byte)m.Movement),
            MaxPanic: stats.MaxPanic)
        {
            Sounds = ItemTemplates.Sounds(content.Sounds),
            HealthBars = Math.Max(1, stats.NumberOfHealthBars),
        };
    }
}
