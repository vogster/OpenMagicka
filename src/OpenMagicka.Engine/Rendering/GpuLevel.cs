using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// A level's geometry (static and animated parts) or a model on the GPU, drawn with FNA's BasicEffect (diffuse texture and simple
/// lighting): the viewer's first look at a level before the original deferred pipeline (OM-23).
/// <see cref="Load"/> reads and decodes on the calling thread, without keeping the content cached,
/// and creates each texture and batch as its own render-queue item, so the per-frame budget holds;
/// <see cref="Draw"/> and <see cref="Dispose"/> belong to the render thread.
/// </summary>
public sealed class GpuLevel : IDisposable
{
    private readonly DrawBatch[] batches;
    private readonly Texture2D[] textures;
    private readonly BoundingFrustum frustum = new(Matrix.Identity);

    private GpuLevel(DrawBatch[] batches, Texture2D[] textures, BoundingBox bounds, IReadOnlyList<string> warnings)
    {
        this.batches = batches;
        this.textures = textures;
        Bounds = bounds;
        Warnings = warnings;
    }

    /// <summary>The box to frame: the opaque geometry.</summary>
    public BoundingBox Bounds { get; }

    public int BatchCount => batches.Length;

    /// <summary>Textures that could not be read; their batches are drawn untextured.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public static async Task<GpuLevel> Load(ContentLoader loader, string modelAsset, RenderQueue<GraphicsDevice> gpu)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(gpu);
        var file = loader.LoadUncached(modelAsset);
        IReadOnlyList<LevelBatch> content = file.Root is LevelModelContent level
            ?
            [
                .. level.Geometry is null ? [] : LevelGeometry.Batches(level.Geometry),
                .. LevelParts.Batches(level.AnimatedParts, file.SharedResources),
            ]
            : file.Root is ModelContent or SkinnedModelContent ? ModelMeshes.ForModel(file)
            : throw new InvalidDataException($"{modelAsset} is neither a level model nor a model");
        var paths = content.Select(b => b.DiffuseTexture is null ? null : loader.ResolveReference(modelAsset, b.DiffuseTexture)).ToArray();
        var warnings = new List<string>();
        var images = new Dictionary<string, Texture2DContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (loader.LoadUncached(path).Root is Texture2DContent image)
                {
                    TextureFormats.ToFna(image.Format);   // reject unsupported formats here, not on the render thread
                    images[path] = image;
                }
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                warnings.Add($"{path}: {e.Message}");
            }
        }

        // Everything is queued at once; the render thread works through it within its per-frame budget.
        var textureTasks = images.ToDictionary(p => p.Key, p => gpu.Enqueue(device => CreateTexture(device, p.Value)), StringComparer.OrdinalIgnoreCase);
        try
        {
            await Task.WhenAll(textureTasks.Values).ConfigureAwait(false);
        }
        catch
        {
            Release(gpu, textureTasks.Values);
            throw;
        }
        var textures = textureTasks.ToDictionary(p => p.Key, p => p.Value.Result, StringComparer.OrdinalIgnoreCase);
        var batchTasks = content.Select((batch, i) =>
        {
            var texture = paths[i] is { } path && textures.TryGetValue(path, out var found) ? found : null;
            return gpu.Enqueue(device => CreateBatch(device, batch, texture));
        }).ToArray();
        try
        {
            await Task.WhenAll(batchTasks).ConfigureAwait(false);
        }
        catch
        {
            Release(gpu, batchTasks);
            Release(gpu, textureTasks.Values);
            throw;
        }
        return new GpuLevel([.. batchTasks.Select(t => t.Result)], [.. textures.Values], LevelGeometry.FramingBounds(content), warnings);
    }

    /// <summary>Disposes, on the render thread, what was created before a load failed (nothing if the queue is closed: the device is going away).</summary>
    private static void Release<T>(RenderQueue<GraphicsDevice> gpu, IEnumerable<Task<T>> tasks)
        where T : IDisposable
    {
        var created = tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
        if (created.Length > 0)
        {
            _ = gpu.Enqueue(_ => Array.ForEach(created, c => c.Dispose()));
        }
    }

    /// <summary>Uploads the stored mip levels; a partial chain is not used (FNA would allocate the rest uninitialised).</summary>
    private static Texture2D CreateTexture(GraphicsDevice device, Texture2DContent image)
    {
        var fullChain = 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width, image.Height)));
        var mipmaps = image.Levels.Count > 1 && image.Levels.Count >= fullChain;
        var texture = new Texture2D(device, image.Width, image.Height, mipmaps, TextureFormats.ToFna(image.Format));
        try
        {
            for (var level = 0; level < (mipmaps ? texture.LevelCount : 1); level++)
            {
                var pixels = TextureFormats.Pixels(image.Format, image.Levels[level]);
                texture.SetData(level, null, pixels, 0, pixels.Length);
            }
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static DrawBatch CreateBatch(GraphicsDevice device, LevelBatch batch, Texture2D? texture)
    {
        var declaration = new VertexDeclaration(batch.Stride, VertexLayouts.Basic(batch.Declaration));
        VertexBuffer? vertices = null;
        IndexBuffer? indices = null;
        try
        {
            vertices = new VertexBuffer(device, declaration, batch.VertexCount, BufferUsage.WriteOnly);
            vertices.SetData(batch.VertexData, 0, batch.VertexCount * batch.Stride);
            var indexSize = batch.Is16BitIndices ? 2 : 4;
            var count = batch.IndexData.Length / indexSize;
            indices = new IndexBuffer(device, batch.Is16BitIndices ? IndexElementSize.SixteenBits : IndexElementSize.ThirtyTwoBits,
                count, BufferUsage.WriteOnly);
            indices.SetData(batch.IndexData, 0, count * indexSize);
            // Only what drawing needs is kept: the CPU vertex and index data can be collected.
            return new DrawBatch(vertices, indices, batch.HasUv ? texture : null, batch.VertexCount, batch.StartIndex,
                batch.PrimitiveCount, batch.Bounds, batch.DiffuseColor, batch.Additive, batch.HasNormal, batch.World);
        }
        catch
        {
            vertices?.Dispose();
            indices?.Dispose();
            throw;
        }
    }

    /// <summary>Opaque batches first, then additive ones over them; batches outside the view are skipped. Returns how many were drawn.</summary>
    public int Draw(GraphicsDevice device, BasicEffect effect, Matrix view, Matrix projection)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(effect);
        frustum.Matrix = view * projection;
        effect.View = view;
        effect.Projection = projection;
        device.RasterizerState = RasterizerState.CullNone;
        return DrawPass(device, effect, additive: false) + DrawPass(device, effect, additive: true);
    }

    private int DrawPass(GraphicsDevice device, BasicEffect effect, bool additive)
    {
        device.BlendState = additive ? BlendState.Additive : BlendState.Opaque;
        device.DepthStencilState = additive ? DepthStencilState.DepthRead : DepthStencilState.Default;
        var drawn = 0;
        foreach (var batch in batches)
        {
            if (batch.Additive != additive || !frustum.Intersects(batch.Bounds))
            {
                continue;
            }
            effect.World = batch.World;
            effect.TextureEnabled = batch.Texture is not null;
            effect.Texture = batch.Texture;
            effect.DiffuseColor = batch.Texture is null ? batch.DiffuseColor : Vector3.One;
            effect.LightingEnabled = !additive && batch.Lit;
            device.SetVertexBuffer(batch.Vertices);
            device.Indices = batch.Indices;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.VertexCount, batch.StartIndex, batch.PrimitiveCount);
            drawn++;
        }
        return drawn;
    }

    public void Dispose()
    {
        foreach (var batch in batches)
        {
            batch.Dispose();
        }
        foreach (var texture in textures)
        {
            texture.Dispose();
        }
    }

    /// <summary>A batch on the GPU; the texture is owned by the level, shared between batches.</summary>
    private sealed record DrawBatch(
        VertexBuffer Vertices, IndexBuffer Indices, Texture2D? Texture, int VertexCount, int StartIndex, int PrimitiveCount,
        BoundingBox Bounds, Vector3 DiffuseColor, bool Additive, bool Lit, Matrix World) : IDisposable
    {
        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }
}
