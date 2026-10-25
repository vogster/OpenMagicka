using OpenMagicka.Content.Particles;
using OpenMagicka.Engine.Particles;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The level's running visual effects on the render thread (OM-75): the logic thread's commands start,
/// move and stop them by number; each frame they step into the particle pool, and one that played out
/// (or has no effect of its name) is told back. Until the effect library is in, the commands wait.
/// </summary>
public sealed class EffectStage(Task<EffectLibrary> library, EffectChannel channel)
{
    /// <summary>Longer frames are cut to this, so a pause does not spawn a burst.</summary>
    public const float MaxStep = 0.1f;

    private readonly Task<EffectLibrary> library = library ?? throw new ArgumentNullException(nameof(library));
    private readonly EffectChannel channel = channel ?? throw new ArgumentNullException(nameof(channel));
    private readonly Dictionary<long, VisualEffectInstance> running = [];
    private readonly HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);

    public ParticlePool Pool { get; } = new();

    /// <summary>The effect library is in (or failed): the commands are taken.</summary>
    public bool IsReady => library.IsCompleted;

    /// <summary>How many effects run now.</summary>
    public int Running => running.Count;

    /// <summary>Why the effect library did not load, or null.</summary>
    public string? Error => library.IsFaulted ? library.Exception?.GetBaseException().Message : null;

    /// <summary>The effects' names the library does not have, as met.</summary>
    public IReadOnlyCollection<string> Missing => missing;

    public void Step(float dt)
    {
        if (!library.IsCompleted)
        {
            return;
        }
        var effects = library.IsCompletedSuccessfully ? library.Result : null;
        while (channel.Commands.TryDequeue(out var command))
        {
            Apply(effects, command);
        }
        dt = Math.Clamp(dt, 0, MaxStep);
        foreach (var (id, effect) in running.ToList())
        {
            effect.Update(dt, Pool);
            if (!effect.IsActive)
            {
                running.Remove(id);
                channel.Ended.Enqueue(id);                                    // it played out
            }
        }
        Pool.Update(dt);
    }

    private void Apply(EffectLibrary? effects, EffectCommand command)
    {
        switch (command.Kind)
        {
            case EffectCommandKind.Start when effects is not null && effects.TryGet(command.Effect ?? "", out var content):
                var instance = new VisualEffectInstance(content, seed: unchecked((int)command.Id * 7919));
                instance.Start(ModelCache.Xna(command.World));
                running[command.Id] = instance;
                break;
            case EffectCommandKind.Start:
                if (command.Effect is { } name)
                {
                    missing.Add(name);
                }
                channel.Ended.Enqueue(command.Id);                            // none to play: it is over at once
                break;
            case EffectCommandKind.Move when running.TryGetValue(command.Id, out var moved):
                moved.Move(ModelCache.Xna(command.World));
                break;
            case EffectCommandKind.Stop when running.Remove(command.Id, out var stopped):
                stopped.Stop();                                               // its particles fly on in the pool
                break;
        }
    }
}
