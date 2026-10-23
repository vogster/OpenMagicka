using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// A batch on the GPU: buffers with the full vertex layout, its material, the material the G-buffer
/// pass draws it with RenderDeferredEffect (its own, or <see cref="RenderDeferredMaterial.StandIn"/>;
/// null for additive batches and characters, whose material has its own effect) and its diffuse texture.
/// </summary>
public sealed record GpuBatch(
    VertexBuffer Vertices, IndexBuffer Indices, int VertexCount, int StartIndex, int PrimitiveCount, BoundingBox Bounds,
    Matrix World, object? Material, RenderDeferredEffectContent? Deferred, Texture2D? Diffuse, Vector3 DiffuseColor, bool Additive, bool Lit)
    : IDisposable
{
    public void Dispose()
    {
        Vertices.Dispose();
        Indices.Dispose();
    }
}

/// <summary>
/// A level's geometry (static and animated parts) or a model on the GPU with every texture its
/// materials reference, for the original deferred pipeline (<see cref="DeferredRenderer"/>) or the
/// simple BasicEffect view (<see cref="Draw"/>). <see cref="Load"/> reads and decodes on the calling
/// thread, without keeping the content cached, and creates each texture and batch as its own
/// render-queue item, so the per-frame budget holds; drawing and disposal belong to the render thread.
/// </summary>
public sealed class GpuLevel : IDisposable
{
    private readonly GpuBatch[] batches;
    private readonly Dictionary<string, Texture> textures;
    private readonly string asset;
    private readonly BoundingFrustum frustum = new(Matrix.Identity);

    private GpuLevel(string asset, GpuBatch[] batches, Dictionary<string, Texture> textures, BoundingBox bounds, IReadOnlyList<string> warnings,
        DirectionalLighting sun, IReadOnlyList<PointLighting> pointLights, IReadOnlyList<SpotLighting> spotLights, Texture2D? skyMap, Vector3 skyColor)
    {
        this.asset = asset;
        Sun = sun;
        PointLights = pointLights;
        SpotLights = spotLights;
        SkyMap = skyMap;
        SkyColor = skyColor;
        this.batches = batches;
        this.textures = textures;
        Bounds = bounds;
        Warnings = warnings;
    }

    /// <summary>The box to frame: the opaque geometry.</summary>
    public BoundingBox Bounds { get; }

    public IReadOnlyList<GpuBatch> Batches => batches;

    /// <summary>The level's directional light, or a default one for models.</summary>
    public DirectionalLighting Sun { get; }

    /// <summary>The level's point lights; none for models.</summary>
    public IReadOnlyList<PointLighting> PointLights { get; }

    /// <summary>The level's spot lights; none for models.</summary>
    public IReadOnlyList<SpotLighting> SpotLights { get; }

    /// <summary>The scene's sky picture, drawn behind everything (null when the scene has none or it failed to load).</summary>
    public Texture2D? SkyMap { get; }

    /// <summary>The sky's tint (1, 1, 1 when the scene sets none).</summary>
    public Vector3 SkyColor { get; }

    public int BatchCount => batches.Length;

    /// <summary>Textures that could not be read; their batches are drawn without them.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The texture a material reference names (relative to this asset), or null when it is missing.</summary>
    public Texture? TextureFor(ExternalReference? reference) =>
        reference is not null && ContentFileSystem.Combine(asset, reference.Path) is { } path && textures.TryGetValue(path, out var texture)
            ? texture
            : null;

    /// <param name="sky">The scene's sky map, loaded with the level's textures.</param>
    public static async Task<GpuLevel> Load(ContentLoader loader, string modelAsset, RenderQueue<GraphicsDevice> gpu, SkyMapSettings? sky = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(gpu);
        var file = loader.LoadUncached(modelAsset);
        IReadOnlyList<LevelBatch> read = file.Root is LevelModelContent level
            ?
            [
                .. level.Geometry is null ? [] : LevelGeometry.Batches(level.Geometry),
                .. LevelParts.Batches(level.AnimatedParts, file.SharedResources),
                .. LevelLiquids.Batches(level.Liquids),
            ]
            : file.Root is ModelContent or SkinnedModelContent ? ModelMeshes.ForModel(file)
            : throw new InvalidDataException($"{modelAsset} is neither a level model nor a model");
        // Every vertex gets the elements its effect reads (OM-63).
        var content = read.Select(b => VertexPadding.Complete(b, EffectInputs.For(b))).ToArray();

        var warnings = new List<string>();
        var images = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in content.SelectMany(References).Select(r => ContentFileSystem.Combine(modelAsset, r.Path)).OfType<string>()
            .Concat(sky is null ? [] : [sky.Texture])
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var image = loader.LoadUncached(path).Root;
                switch (image)
                {
                    case Texture2DContent t: TextureFormats.ToFna(t.Format); images[path] = t; break;
                    case TextureCubeContent c when c.Faces.Count == 6 && c.Faces.All(f => f.Count > 0):
                        TextureFormats.ToFna(c.Format);
                        images[path] = c;
                        break;
                    case TextureCubeContent:
                        throw new InvalidDataException("a cube map needs six faces with at least one level each");
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
        var batchTasks = content.Select(batch =>
        {
            var diffuse = batch.DiffuseTexture is { } d && ContentFileSystem.Combine(modelAsset, d.Path) is { } path
                && textures.TryGetValue(path, out var found) ? found as Texture2D : null;
            return gpu.Enqueue(device => CreateBatch(device, batch, diffuse));
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
        var lights = file.Root is LevelModelContent lit ? lit.Lights : [];
        var sun = SceneLighting.Sun(lights);
        if (sky is not null && textures.TryGetValue(sky.Texture, out var skyTexture) && skyTexture is not Texture2D)
        {
            warnings.Add($"{sky.Texture}: the sky map is not a 2D texture");
        }
        return new GpuLevel(modelAsset, [.. batchTasks.Select(t => t.Result)], textures, LevelGeometry.FramingBounds(content), warnings, sun,
            SceneLighting.PointLights(lights), SceneLighting.SpotLights(lights),
            sky is not null ? textures.GetValueOrDefault(sky.Texture) as Texture2D : null, sky?.Color ?? Vector3.One);
    }

    /// <summary>Every texture a batch's material references.</summary>
    private static IEnumerable<ExternalReference> References(LevelBatch batch)
    {
        var all = new List<ExternalReference?> { batch.DiffuseTexture };
        if (batch.Material is SkinnedModelBasicEffectContent skinned)
        {
            all.AddRange(SkinnedDeferredMaterial.References(skinned));
        }
        if (batch.Material is RenderDeferredLiquidEffectContent or LavaEffectContent)
        {
            all.AddRange(LiquidMaterial.References(batch.Material));
        }
        if (DeferredMaterial(batch) is { } m)
        {
            all.Add(m.ReflectionMap);
            foreach (var layer in new[] { m.Layer0, m.Layer1 })
            {
                all.AddRange([layer?.DiffuseTexture, layer?.MaterialTexture, layer?.NormalTexture]);
            }
        }
        return all.OfType<ExternalReference>();
    }

    /// <summary>The RenderDeferredEffect material a batch is drawn with; none for additive batches and characters.</summary>
    private static RenderDeferredEffectContent? DeferredMaterial(LevelBatch batch) =>
        batch.Additive || batch.Material is SkinnedModelBasicEffectContent or RenderDeferredLiquidEffectContent or LavaEffectContent ? null
        : batch.Material as RenderDeferredEffectContent
          ?? RenderDeferredMaterial.StandIn(batch.HasUv ? batch.DiffuseTexture : null, batch.DiffuseColor);

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

    private static Texture CreateTexture(GraphicsDevice device, object image) => image switch
    {
        Texture2DContent t => CreateTexture2D(device, t),
        TextureCubeContent c => CreateTextureCube(device, c),
        _ => throw new InvalidDataException($"not a texture: {image.GetType().Name}"),
    };

    /// <summary>Uploads the stored mip levels; a partial chain is not used (FNA would allocate the rest uninitialised).</summary>
    private static Texture2D CreateTexture2D(GraphicsDevice device, Texture2DContent image)
    {
        var mipmaps = image.Levels.Count > 1 && image.Levels.Count >= FullChain(Math.Max(image.Width, image.Height));
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

    private static TextureCube CreateTextureCube(GraphicsDevice device, TextureCubeContent image)
    {
        var levels = image.Faces.Min(f => f.Count);
        var mipmaps = levels > 1 && levels >= FullChain(image.Size);
        var texture = new TextureCube(device, image.Size, mipmaps, TextureFormats.ToFna(image.Format));
        try
        {
            for (var face = 0; face < 6 && face < image.Faces.Count; face++)
            {
                for (var level = 0; level < (mipmaps ? texture.LevelCount : 1); level++)
                {
                    var pixels = TextureFormats.Pixels(image.Format, image.Faces[face][level]);
                    texture.SetData((CubeMapFace)face, level, null, pixels, 0, pixels.Length);
                }
            }
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static int FullChain(int size) => 1 + (int)Math.Floor(Math.Log2(Math.Max(size, 1)));

    private static GpuBatch CreateBatch(GraphicsDevice device, LevelBatch batch, Texture2D? diffuse)
    {
        var declaration = new VertexDeclaration(batch.Stride, VertexLayouts.Full(batch.Declaration));
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
            var deferred = DeferredMaterial(batch);
            return new GpuBatch(vertices, indices, batch.VertexCount, batch.StartIndex, batch.PrimitiveCount, batch.Bounds, batch.World,
                batch.Material, deferred, batch.HasUv ? diffuse : null, batch.DiffuseColor, batch.Additive, batch.HasNormal);
        }
        catch
        {
            vertices?.Dispose();
            indices?.Dispose();
            throw;
        }
    }

    /// <summary>The simple view: opaque batches then additive ones with BasicEffect; batches outside the view are skipped.</summary>
    public int Draw(GraphicsDevice device, BasicEffect effect, Matrix view, Matrix projection)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(effect);
        frustum.Matrix = view * projection;
        effect.View = view;
        effect.Projection = projection;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.LinearWrap;
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
            effect.TextureEnabled = batch.Diffuse is not null;
            effect.Texture = batch.Diffuse;
            effect.DiffuseColor = batch.Diffuse is null ? batch.DiffuseColor : Vector3.One;
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
        foreach (var texture in textures.Values)
        {
            texture.Dispose();
        }
    }
}
