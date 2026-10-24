using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.AI;

/// <summary>
/// A level's minds (ENTITIES_AI.md §7.1): each frame the window moves on by 20, and the 20 minds in
/// it think, each with the time gathered since it last did. Only the host thinks.
/// </summary>
public sealed class AiManager
{
    public const int PerFrame = 20;

    private readonly List<Agent> agents = [];
    private Agent[] thinking = [];
    private int window;

    /// <summary>Whether this side thinks: the host (or a game alone); a client's minds wait for the host.</summary>
    public bool IsHost { get; set; } = true;

    public IReadOnlyList<Agent> Agents => agents;

    /// <summary>Whether any mind has a target (the music's cue for a fight).</summary>
    public bool Threat { get; private set; }

    internal void Add(Agent agent)
    {
        if (!agents.Contains(agent))
        {
            agents.Add(agent);
        }
    }

    internal void Remove(Agent agent) => agents.Remove(agent);

    private float stuck;

    /// <summary>The time chanted elements have waited with no target, one count for all the level's minds, as the original's.</summary>
    internal float Stuck(float dt) => stuck += dt;

    internal void ResetStuck() => stuck = 0;

    public void Update(float dt)
    {
        var threat = false;
        if (IsHost && agents.Count > 0)
        {
            window = (window + PerFrame) % agents.Count;
            var count = Math.Min(PerFrame, agents.Count);
            if (thinking.Length < count)
            {
                thinking = new Agent[PerFrame];
            }
            for (var i = 0; i < count; i++)
            {
                thinking[i] = agents[(i + window) % agents.Count];   // a reaction's trigger may add or take out minds meanwhile
            }
            for (var i = 0; i < count; i++)
            {
                if (thinking[i].Owner.Manager is not null)
                {
                    thinking[i].Think();
                }
                thinking[i] = null!;
            }
            foreach (var agent in agents)
            {
                agent.Pass(dt);                                               // as the original: after the thinking
                threat |= agent.CurrentTarget is not null;
            }
        }
        Threat = threat;
    }

    /// <summary>The mind a character of this template gets: an NPC with a mind in its template.</summary>
    internal static Agent? For(Character character) =>
        character.Template.Ai is { } profile && profile != AiProfile.None ? new Agent(character, profile) : null;
}
