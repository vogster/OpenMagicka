using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Navigation;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A scene being played: its world given the level's bestiary (what spawns and summons make), its nav
/// mesh (the parts' meshes moving with them) and its places, and its scripts entered. A frame runs as
/// the original's: the world (minds, physics, entities), then the level's timers, then the scene's
/// triggers, effects and animated parts.
/// </summary>
public sealed class RunningLevel
{
    /// <param name="model">The scene's level model (its nav mesh, areas and locators); none for a bare scene.</param>
    /// <param name="bestiary">Where its characters come from (a <see cref="Characters.ContentBestiary"/> in the game).</param>
    public RunningLevel(GameWorld world, LevelModelContent? model, SceneDefinition scene, IBestiary? bestiary, ScriptLevel? level = null,
        ScriptRegistry? registry = null, ScriptGame? game = null, bool looped = false, int seed = 0, IScriptAssets? assets = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);
        World = world;
        Scene = new ScriptScene(scene, level ?? new ScriptLevel(), registry ?? ScriptRegistry.Engine(), world, looped, seed, new ScriptPlaces(model), game);
        var surfaces = new Dictionary<AnimatedLevelPartContent, Gameplay.Navigation.NavSurface>(ReferenceEqualityComparer.Instance);
        (Bestiary, NavMesh) = (bestiary, model is null ? null : LevelNavigation.From(model, surfaces));
        foreach (var part in Scene.Places.Parts)
        {
            Steer(part, surfaces);
        }
        (world.Entities.Bestiary, world.Entities.NavMesh) = (Bestiary, NavMesh);
        Scene.Assets = assets;
        foreach (var placed in model?.PhysicsEntities ?? [])
        {
            if (assets?.Prop("data/physicsentities/" + placed.Template) is { } template && world.Entities.Entities.Count < Gameplay.Entities.EntityManager.Capacity)
            {
                ScriptProps.Place(world, template, LevelNavigation.ToNumerics(placed.Transform));     // the level's own props stand when it starts
            }
        }
        Scene.Enter();
    }

    public IBestiary? Bestiary { get; }

    private static void Steer(ScenePart part, Dictionary<AnimatedLevelPartContent, Gameplay.Navigation.NavSurface> surfaces)
    {
        part.Navigation = surfaces.GetValueOrDefault(part.Content);
        foreach (var child in part.Children)
        {
            Steer(child, surfaces);
        }
    }

    public Gameplay.Navigation.NavMesh? NavMesh { get; }

    public GameWorld World { get; }

    public ScriptScene Scene { get; }

    public ScriptLevel Level => Scene.Level;

    public void Tick(float dt)
    {
        World.Tick(dt);
        Level.Update(dt, Scene.Game.InCutscene);
        Scene.Update(dt);
    }

    /// <summary>The scene is left: its scripts, bestiary and nav mesh no longer reach the world.</summary>
    public void Leave()
    {
        Scene.Leave();
        if (ReferenceEquals(World.Entities.Bestiary, Bestiary))
        {
            World.Entities.Bestiary = null;
        }
        if (ReferenceEquals(World.Entities.NavMesh, NavMesh))
        {
            World.Entities.NavMesh = null;
        }
    }
}
