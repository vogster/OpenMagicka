using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Particles;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The particles' sprite sheets (<c>EffectTextures/ParticlesA…D</c>): volume textures whose slices
/// the shader picks by each particle's sprite number.
/// </summary>
public static class ParticleSheets
{
    public static readonly string[] Assets = ["EffectTextures/ParticlesA", "EffectTextures/ParticlesB", "EffectTextures/ParticlesC", "EffectTextures/ParticlesD"];

    /// <summary>Reads the sheets on the calling thread and creates them on the render thread.</summary>
    public static async Task<Texture3D[]> Load(ContentLoader loader, RenderQueue<GraphicsDevice> gpu)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(gpu);
        var sheets = Assets.Select(asset => loader.LoadUncached(asset).Root as Texture3DContent is { Format: SurfaceFormatXna31.Color } sheet
            ? sheet
            : throw new InvalidDataException($"{asset} is not a volume texture of colours")).ToArray();
        var created = sheets.Select(sheet => gpu.Enqueue(device => Create(device, sheet))).ToArray();
        try
        {
            return await Task.WhenAll(created).ConfigureAwait(false);
        }
        catch
        {
            var made = created.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
            if (made.Length > 0)
            {
                _ = gpu.Enqueue(_ => Array.ForEach(made, t => t.Dispose()));
            }
            throw;
        }
    }

    /// <summary>Uploads the full mip chain when the file has it, otherwise the top level only.</summary>
    private static Texture3D Create(GraphicsDevice device, Texture3DContent sheet)
    {
        var chain = 1 + (int)Math.Floor(Math.Log2(Math.Max(Math.Max(sheet.Width, sheet.Height), Math.Max(sheet.Depth, 1))));
        var mipmaps = sheet.Levels.Count > 1 && sheet.Levels.Count >= chain;
        var texture = new Texture3D(device, sheet.Width, sheet.Height, sheet.Depth, mipmaps, TextureFormats.ToFna(sheet.Format));
        try
        {
            for (var level = 0; level < (mipmaps ? texture.LevelCount : 1); level++)
            {
                var pixels = TextureFormats.Pixels(sheet.Format, sheet.Levels[level]);
                var (w, h, d) = (Math.Max(sheet.Width >> level, 1), Math.Max(sheet.Height >> level, 1), Math.Max(sheet.Depth >> level, 1));
                if (pixels.Length != w * h * d * 4)
                {
                    throw new InvalidDataException($"a sprite sheet level {level} has {pixels.Length} bytes for {w} × {h} × {d} pixels");
                }
                texture.SetData(level, 0, 0, w, h, 0, d, pixels, 0, pixels.Length);
            }
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Draws the particles as the original does (docs/engine/ARCHITECTURE.md §5.1): one quad, instanced
/// once per live particle with its 56-byte record, through <c>ParticleEffect</c>, which reads the
/// scene's depth for soft edges. Blend One / SrcAlpha with no depth test, into Main after the
/// additive objects. Render thread only.
/// </summary>
internal sealed class ParticlePass : IDisposable
{
    private static readonly VertexDeclaration Corners = new(
        new VertexElement(0, VertexElementFormat.Single, VertexElementUsage.Position, 0),
        new VertexElement(4, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    private static readonly VertexDeclaration Records = new(
        new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 1),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.Normal, 1),
        new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.Color, 1),
        new VertexElement(40, VertexElementFormat.Byte4, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(44, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0));

    /// <summary>One state for both kinds: additive particles write alpha 1, alpha-blended ones premultiplied colour and 1 − a.</summary>
    private static readonly BlendState ParticleBlend = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.SourceAlpha,
    };

    private static readonly string[] Parameters =
        ["View", "Projection", "DestinationDimensions", "DepthTexture", "SpritesA", "SpritesB", "SpritesC", "SpritesD"];

    private readonly GameEffect effect;
    private readonly IReadOnlyList<Texture3D> sheets;
    private readonly VertexBuffer quad;
    private readonly IndexBuffer indices;
    private readonly DynamicVertexBuffer records;

    public ParticlePass(GraphicsDevice device, GameEffect effect, IReadOnlyList<Texture3D> sheets)
    {
        if (sheets.Count != 4)
        {
            throw new ArgumentException("the particles need four sprite sheets", nameof(sheets));
        }
        var parameters = new FnaEffectParameters(effect.Effect);
        var missing = Parameters.Where(name => !parameters.Contains(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"the particle effect has no parameter {string.Join(", ", missing)}");
        }
        (this.effect, this.sheets) = (effect, sheets);
        quad = new VertexBuffer(device, Corners, 4, BufferUsage.WriteOnly);
        try
        {
            // Corner number and texture coordinate; the vertex shader places the corners.
            quad.SetData(new[] { 0f, 1, 1, 1, 0, 1, 2, 0, 0, 3, 1, 0 });
            indices = new IndexBuffer(device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            indices.SetData(new short[] { 0, 1, 2, 0, 2, 3 });
            records = new DynamicVertexBuffer(device, Records, ParticlePool.DefaultCapacity, BufferUsage.WriteOnly);
        }
        catch
        {
            quad.Dispose();
            indices?.Dispose();
            throw;
        }
    }

    public void Draw(GraphicsDevice device, ParticlePool pool, Matrix view, Matrix projection, Texture2D depth)
    {
        var count = Math.Min(pool.LastActive + 1, records.VertexCount);
        if (count <= 0)
        {
            return;
        }
        records.SetData(pool.Records, 0, count, SetDataOptions.Discard);
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("View", view);
        parameters.SetValue("Projection", projection);
        // Only for the half-pixel shift (0.5 / size): an infinite size makes it zero.
        parameters.SetValue("DestinationDimensions", new Vector2(float.PositiveInfinity));
        parameters.SetValue("DepthTexture", depth);
        parameters.SetValue("SpritesA", sheets[0]);
        parameters.SetValue("SpritesB", sheets[1]);
        parameters.SetValue("SpritesC", sheets[2]);
        parameters.SetValue("SpritesD", sheets[3]);
        device.BlendState = ParticleBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SetVertexBuffers(new VertexBufferBinding(quad), new VertexBufferBinding(records, 0, 1));
        device.Indices = indices;
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 4, 0, 2, count);
        }
    }

    public void Dispose()
    {
        quad.Dispose();
        indices.Dispose();
        records.Dispose();
        foreach (var sheet in sheets)
        {
            sheet.Dispose();
        }
    }
}
