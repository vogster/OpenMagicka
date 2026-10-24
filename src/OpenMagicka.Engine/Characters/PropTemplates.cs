using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Navigation;
using OpenMagicka.Gameplay.Props;
using Numerics = System.Numerics;

namespace OpenMagicka.Engine.Characters;

/// <summary>
/// A physics prop's template from its content (docs/engine/ENTITIES_AI.md §8.4): the original takes its
/// first collision box only (the others are read and left), none when it has no box (a mesh-only
/// template stands as a box of no size); its gibs are the models' paths.
/// </summary>
public static class PropTemplates
{
    public static PropTemplate From(PhysicsEntityTemplateContent content, string asset)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(asset);
        var box = content.Boxes.Count > 0
            ? new PropBox(Vector(content.Boxes[0].Corner), Vector(content.Boxes[0].SideLengths), Turn(content.Boxes[0].Orientation))
            : PropBox.None;
        var template = new PropTemplate(
            string.IsNullOrEmpty(content.Id) ? asset : content.Id,
            content.Movable,
            content.Pushable,
            content.Solid,
            content.Mass,
            content.MaxHitPoints,
            ItemTemplates.Resistances(content.Resistances),
            box,
            [.. content.Gibs.Where(g => g.Model is not null).Select(g => new PropGib(g.Model!.Path, g.Mass, g.Scale))],
            content.GibTrailEffect,
            [.. content.Effects.Select(e => new PropEffect(e.Effect, LevelNavigation.ToNumerics(e.Transform)))],
            ItemTemplates.Events(content.Events),
            content.CanHaveStatus,
            content.HitEffect,
            content.HitSound);
        template.Validate();
        return template;
    }

    private static Numerics.Vector3 Vector(Microsoft.Xna.Framework.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Numerics.Quaternion Turn(Microsoft.Xna.Framework.Quaternion q) =>
        q.LengthSquared() > 1e-12f ? new(q.X, q.Y, q.Z, q.W) : Numerics.Quaternion.Identity;
}
