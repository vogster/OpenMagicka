using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Navigation;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Navigation;

namespace OpenMagicka.Engine.Scripting;

/// <summary>What a scene needs of its level model when it is entered: its nav mesh (the parts' meshes moving with them) and its props.</summary>
internal static class SceneSetup
{
    /// <summary>The model's nav mesh, each part of the places handed its own mesh; none without a model.</summary>
    public static NavMesh? Navigation(LevelModelContent? model, ScriptPlaces places)
    {
        if (model is null)
        {
            return null;
        }
        var surfaces = new Dictionary<AnimatedLevelPartContent, NavSurface>(ReferenceEqualityComparer.Instance);
        var mesh = LevelNavigation.From(model, surfaces);
        foreach (var part in places.Parts)
        {
            Steer(part, surfaces);
        }
        return mesh;
    }

    /// <summary>The props the model places, standing in the world (those the assets know, while there is room).</summary>
    public static void PlaceProps(GameWorld world, LevelModelContent? model, IScriptAssets? assets)
    {
        foreach (var placed in model?.PhysicsEntities ?? [])
        {
            if (assets?.Prop("data/physicsentities/" + placed.Template) is { } template && world.Entities.Entities.Count < Gameplay.Entities.EntityManager.Capacity)
            {
                ScriptProps.Place(world, template, LevelNavigation.ToNumerics(placed.Transform));
            }
        }
    }

    private static void Steer(ScenePart part, Dictionary<AnimatedLevelPartContent, NavSurface> surfaces)
    {
        part.Navigation = surfaces.GetValueOrDefault(part.Content);
        foreach (var child in part.Children)
        {
            Steer(child, surfaces);
        }
    }
}
