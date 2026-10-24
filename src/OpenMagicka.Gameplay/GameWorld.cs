using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay;

/// <summary>
/// One level's simulation: physics and entities, run in the frame order of
/// docs/engine/ENTITIES_AI.md §1.5. AI, level parts and bosses join as they come. Not thread-safe.
/// </summary>
public sealed class GameWorld : IDisposable
{
    public GameWorld(PhysicsSettings? settings = null, GameSession? session = null)
    {
        Physics = new PhysicsWorld(settings);
        Entities.Session = session;
    }

    public PhysicsWorld Physics { get; }

    /// <summary>The game being played, if any.</summary>
    public GameSession? Session => Entities.Session;

    public EntityManager Entities { get; } = new();

    /// <summary>
    /// Makes a character and adds it to the level (the server's id); if the level cannot take it, its
    /// body leaves the physics world again.
    /// </summary>
    public Character Spawn(CharacterTemplate template, System.Numerics.Vector3 position, int players = 1, Random? random = null)
    {
        var character = new Character(template, Physics, position, players, random: random);
        try
        {
            Entities.Add(character);
        }
        catch
        {
            Physics.Remove(character.Body.Body);
            throw;
        }
        return character;
    }

    /// <summary>The characters in the level, in the manager's order.</summary>
    public IEnumerable<Character> Characters => Entities.Entities.OfType<Character>();

    /// <summary>
    /// One frame: the characters' bodies are set up and steered, physics steps, each body's report
    /// goes back to its character, the grid is rebuilt, entities update, and the removable leave.
    /// Returns what left, for the removal broadcast.
    /// </summary>
    public IReadOnlyList<RemovedEntity> Tick(float dt)
    {
        dt *= Entities.TimeMultiplier * Entities.TimeModifier;                // a time warp or Death slows the whole level
        var characters = Characters.ToList();
        foreach (var character in characters)
        {
            character.PrepareStep();
            character.Body.BeforeStep(dt);
        }
        Physics.Step(dt);
        foreach (var character in characters)
        {
            character.ApplyPhysics(character.Body.AfterStep());
        }
        Entities.RebuildGrid();
        Entities.Update(dt);
        Session?.Tick(dt);
        return Entities.RemoveDead();
    }

    public void Dispose()
    {
        try
        {
            Entities.Clear();
        }
        finally
        {
            Physics.Dispose();
        }
    }
}
