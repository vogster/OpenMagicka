using Microsoft.Xna.Framework;
using OpenMagicka.Content.Particles;
using OpenMagicka.Engine.Particles;

namespace OpenMagicka.Game.Viewer;

/// <summary>
/// The shown level's visual effects in the viewer: started with the level, as the original does,
/// and stepped each frame. The viewer steps them on the render thread with the frame time (the game
/// will step them on its logic thread). A new level gets a short warm-up, so its fires and dust are
/// already going in the first frame. Render thread only.
/// </summary>
public sealed class ViewerParticles
{
    /// <summary>Longer frames are cut to this, so a pause does not spawn a burst.</summary>
    private const float MaxStep = 0.1f;
    private const float WarmUp = 2;
    private const float WarmUpStep = 1 / 30f;

    private readonly Task<EffectLibrary> library;
    private readonly List<VisualEffectInstance> running = [];

    public ViewerParticles(Task<EffectLibrary> library)
    {
        ArgumentNullException.ThrowIfNull(library);
        this.library = library;
    }

    public ParticlePool Pool { get; } = new();

    /// <summary>Why the effect library did not load, or null.</summary>
    public string? Error => library.IsFaulted ? library.Exception?.GetBaseException().Message : null;

    /// <summary>Effect names the library does not have, reported once per level.</summary>
    public IReadOnlyList<string> Missing { get; private set; } = [];

    /// <summary>Replaces the running effects with a level's; false while the library is still loading (try again next frame).</summary>
    public bool Start(IReadOnlyList<PlacedEffect> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (!library.IsCompletedSuccessfully)
        {
            return library.IsFaulted || library.IsCanceled;
        }
        running.Clear();
        Pool.Clear();
        var missing = new List<string>();
        foreach (var (placement, index) in placements.Select((p, i) => (p, i)))
        {
            if (!library.Result.TryGet(placement.Effect, out var effect))
            {
                missing.Add(placement.Effect);
                continue;
            }
            var instance = new VisualEffectInstance(effect, seed: index * 7919);
            instance.Start(placement.World);
            running.Add(instance);
        }
        Missing = missing;
        for (var t = 0f; t < WarmUp; t += WarmUpStep)
        {
            Step(WarmUpStep);
        }
        return true;
    }

    public void Step(float dt)
    {
        dt = MathF.Min(dt, MaxStep);
        foreach (var effect in running)
        {
            effect.Update(dt, Pool);
        }
        Pool.Update(dt);
    }
}
