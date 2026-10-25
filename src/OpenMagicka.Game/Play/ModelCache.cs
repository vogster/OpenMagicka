using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Threading;
using Numerics = System.Numerics;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The entities' models on the GPU, by asset (render thread): each loads in the background the first
/// time it is asked for, and is drawn once it is in; one that fails is said once and never drawn.
/// </summary>
public sealed class ModelCache(ContentLoader loader, RenderQueue<GraphicsDevice> gpu) : IDisposable
{
    private readonly ContentLoader loader = loader ?? throw new ArgumentNullException(nameof(loader));
    private readonly RenderQueue<GraphicsDevice> gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
    private readonly Dictionary<string, Task<GpuLevel>> models = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The model, or null while it loads (or when it failed).</summary>
    public GpuLevel? Get(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!models.TryGetValue(asset, out var loading))
        {
            loading = Task.Run(() => GpuLevel.Load(loader, asset, gpu));
            models[asset] = loading;
        }
        if (loading.IsCompletedSuccessfully)
        {
            return loading.Result;
        }
        if (loading.IsCompleted && reported.Add(asset))
        {
            Console.Error.WriteLine($"OpenMagicka: model {asset}: {loading.Exception?.GetBaseException().Message ?? "cancelled"}");
        }
        return null;
    }

    /// <summary>The frame's models that are in, as the renderer takes them.</summary>
    public List<ModelInstance> Instances(IReadOnlyList<DrawnModel> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        var instances = new List<ModelInstance>(drawn.Count);
        foreach (var model in drawn)
        {
            if (Get(model.Asset) is { } gpuModel)
            {
                instances.Add(new ModelInstance(gpuModel, Xna(model.World), model.Bones?.Select(Xna).ToArray(), new Vector3(model.Tint.X, model.Tint.Y, model.Tint.Z)));
            }
        }
        return instances;
    }

    internal static Matrix Xna(Numerics.Matrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    public void Dispose()
    {
        foreach (var model in models.Values.Where(m => m.IsCompletedSuccessfully))
        {
            model.Result.Dispose();
        }
        models.Clear();
    }
}
