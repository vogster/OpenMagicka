using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game;

/// <summary>The outcome of warming up the game's effects; Skipped are the ones known to crash MojoShader.</summary>
public sealed record EffectWarmupReport(IReadOnlyList<string> Ready, IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed)
{
    public string Summary =>
        $"effects: {Ready.Count} ready, {Skipped.Count} skipped, {Failed.Count} failed"
        + string.Concat(Skipped.Select(f => "\n  SKIPPED " + f)) + string.Concat(Failed.Select(f => "\n  FAILED " + f));

    /// <summary>For --check-effects: 0 when effects ran and none failed, 1 otherwise.</summary>
    public int ExitCode => Ready.Count > 0 && Failed.Count == 0 ? 0 : 1;
}

/// <summary>
/// Creates every game effect and applies each of its passes once with the global parameters, on
/// the render thread: the check that the original shaders run through FNA and MojoShader.
/// Effects known to crash MojoShader are skipped by <see cref="ShaderSource"/>.
/// </summary>
public static class EffectWarmup
{
    public static async Task<EffectWarmupReport> Run(ShaderLibrary shaders, RenderQueue<GraphicsDevice> gpu)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        var view = Matrix.CreateLookAt(new Vector3(0, 144, 171), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(5), 16f / 9, 105, 500);
        var globals = new GlobalEffectValues(view, projection, new Vector3(0, 144, 171), Vector3.Zero, 0);

        var ready = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        foreach (var name in shaders.Source.Names)
        {
            try
            {
                var effect = await shaders.Load(name).ConfigureAwait(false);
                var passes = await gpu.Enqueue(_ => ApplyAll(effect, globals)).ConfigureAwait(false);
                ready.Add($"{name} ({passes} passes, globals: {string.Join(' ', effect.Globals.Bound)})");
            }
            catch (KnownBrokenEffectException e)
            {
                skipped.Add($"{name}: {e.Message}");
            }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
            {
                failed.Add($"{name}: {e.GetType().Name}: {e.Message}");
            }
        }
        return new EffectWarmupReport(ready, skipped, failed);
    }

    private static int ApplyAll(GameEffect effect, GlobalEffectValues globals)
    {
        effect.Globals.Apply(globals);
        var passes = 0;
        foreach (var technique in effect.Effect.Techniques)
        {
            effect.Effect.CurrentTechnique = technique;
            foreach (var pass in technique.Passes)
            {
                pass.Apply();
                passes++;
            }
        }
        return passes;
    }
}
