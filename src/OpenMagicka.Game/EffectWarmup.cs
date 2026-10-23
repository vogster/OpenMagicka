using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Game;

/// <summary>
/// The outcome of warming up the game's effects. Skipped are the ones known to crash MojoShader and
/// those the game never draws whose test draw failed.
/// </summary>
public sealed record EffectWarmupReport(IReadOnlyList<string> Ready, IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed)
{
    public string Summary =>
        $"effects: {Ready.Count} ready, {Skipped.Count} skipped, {Failed.Count} failed"
        + string.Concat(Skipped.Select(f => "\n  SKIPPED " + f)) + string.Concat(Failed.Select(f => "\n  FAILED " + f));

    /// <summary>For --check-effects: 0 when effects ran and none failed, 1 otherwise.</summary>
    public int ExitCode => Ready.Count > 0 && Failed.Count == 0 ? 0 : 1;
}

/// <summary>
/// Creates every game effect and draws one degenerate triangle with each of its passes, with the
/// global parameters and a vertex holding every input its vertex shaders read, into a 1×1 target,
/// on the render thread: the check that the original shaders run through FNA and MojoShader. The
/// draw matters: a driver compiles and links the translated shaders only then (OM-65). It runs at
/// every start; the device's blend, depth and rasterizer states are put back afterwards. Effects
/// known to crash MojoShader are skipped by <see cref="ShaderSource"/>.
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
                var probe = VertexPadding.Probe(EffectInputs.Read(shaders.Source.Bytecode(name)));
                var passes = await gpu.Enqueue(device => DrawAll(device, effect, globals, probe)).ConfigureAwait(false);
                ready.Add($"{name} ({passes} passes drawn, globals: {string.Join(' ', effect.Globals.Bound)})");
            }
            catch (KnownBrokenEffectException e)
            {
                skipped.Add($"{name}: {e.Message}");
            }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
            {
                var (skip, line) = Outcome(name, $"{e.GetType().Name}: {e.Message.ReplaceLineEndings(" | ")}");
                (skip ? skipped : failed).Add(line);
            }
        }
        return new EffectWarmupReport(ready, skipped, failed);
    }

    /// <summary>
    /// How a failed effect is reported: as skipped when the game never draws it (its translation
    /// may then stay broken, as ChasmEffect's is for D3D11), otherwise as failed.
    /// </summary>
    public static (bool Skipped, string Line) Outcome(string name, string error) =>
        NeverDrawn.TryGetValue(name, out var reason) ? (true, $"{name} (the game never draws it: {reason}): {error}") : (false, $"{name}: {error}");

    /// <summary>
    /// Effects no game content uses (docs/engine/RISK_CHECKS.md §1.1). ChasmEffect fails to link on
    /// D3D11 only (§1.6); a failure on any driver is reported as skipped.
    /// </summary>
    private static readonly Dictionary<string, string> NeverDrawn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ChasmEffect"] = "no content uses it",
    };

    private static int DrawAll(GraphicsDevice device, GameEffect effect, GlobalEffectValues globals,
        (VertexDeclarationContent Declaration, byte[] Vertex) probe)
    {
        if (probe.Vertex.Length == 0)
        {
            throw new InvalidDataException("the effect's vertex shaders read no inputs");
        }
        var declaration = new VertexDeclaration(probe.Declaration.Stride, VertexLayouts.Full(probe.Declaration));
        using var vertices = new VertexBuffer(device, declaration, 3, BufferUsage.WriteOnly);
        vertices.SetData([.. probe.Vertex, .. probe.Vertex, .. probe.Vertex]);
        using var target = new RenderTarget2D(device, 1, 1);
        var (blend, depth, rasterizer) = (device.BlendState, device.DepthStencilState, device.RasterizerState);
        try
        {
            device.SetRenderTarget(target);
            effect.Globals.Apply(globals);
            var passes = 0;
            foreach (var technique in effect.Effect.Techniques)
            {
                effect.Effect.CurrentTechnique = technique;
                foreach (var pass in technique.Passes)
                {
                    pass.Apply();
                    device.SetVertexBuffer(vertices);
                    device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
                    passes++;
                }
            }
            return passes;
        }
        finally
        {
            device.SetVertexBuffer(null);
            device.SetRenderTarget(null);
            (device.BlendState, device.DepthStencilState, device.RasterizerState) = (blend, depth, rasterizer);
        }
    }
}
