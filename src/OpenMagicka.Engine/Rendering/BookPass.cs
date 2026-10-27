using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The Tome's book drawn (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's menu: the room (desk, wall and
/// the things on the desk), the book, then its right and left stacks of pages, each with XNAnimation's forward technique
/// (lit by the candle alone, its light over the squared distance, written in the scene's encoded light) into a
/// multisampled target with depth, then the turning sheets while it riffles; then the colour filter (left as it is) and
/// the bloom onto the screen, which decode it.
/// The pages show the page atlas (<see cref="MenuPass.DrawAtlas"/>), their texels decoded by the effect. The candle's
/// shadow map is drawn first (<see cref="DrawShadows"/>), and every model drawn reads it. Render thread only.
/// </summary>
public sealed class BookPass : IDisposable
{
    public const string Room = "UI/ToM/background_mesh", Book = "UI/ToM/tome", Stack = "UI/ToM/page", Sheet = "UI/ToM/page_flip";

    /// <summary>The parameters set on the forward effect (checked once it is made).</summary>
    internal static readonly string[] Parameters =
    [
        "View", "Projection", "ViewProjection", "CameraPosition", "LightPosition", "LightAmbientColor", "LightDiffuseColor", "LightSpecularColor",
        "LightViewProjection", "NormalPower0", "ShadowMapScale", "shadowMapEnabled", "bloat", "matBones", "DiffuseColor", "SpecularAmount",
        "SpecularPower", "diffuseIsHDR", "diffuseMap0", "diffuseMap0Enabled", "specularMap0", "specularMapEnabled", "normalMap0", "normalMapEnabled",
        "shadowMap",
    ];

    private readonly GameEffect effect;
    private readonly FnaEffectParameters parameters;
    private readonly GpuLevel room, book, stack, sheet;
    private readonly PostPass post;
    private readonly FullscreenQuad quad;
    private readonly Matrix[] still = [Matrix.Identity];
    private RenderTarget2D? scene, shadow;

    /// <summary>The shadow map's side, where the candle's camera looks, its lens, and the sampler the effect reads it with.</summary>
    public const int ShadowSize = 1024, ShadowSlot = 3;

    public const float ShadowFieldOfView = 1.885f;

    public static readonly Vector3 ShadowAim = new(-2, 0, 2.5f);

    private static readonly DepthStencilState LessEqual = new() { DepthBufferEnable = true, DepthBufferWriteEnable = true,
        DepthBufferFunction = CompareFunction.LessEqual };

    private BookPass(GameEffect effect, GpuLevel room, GpuLevel book, GpuLevel stack, GpuLevel sheet, PostPass post, FullscreenQuad quad)
    {
        (this.effect, this.room, this.book, this.stack, this.sheet, this.post, this.quad) = (effect, room, book, stack, sheet, post, quad);
        parameters = new FnaEffectParameters(effect.Effect);
        effect.Effect.CurrentTechnique = effect.Effect.Techniques[XnAnimationEffect.Forward];
    }

    /// <summary>
    /// Reads the effect from the player's <c>XNAnimation.dll</c> (next to the content), the book's models and their
    /// pictures, and makes them on the render thread; fails when any is missing or does not read.
    /// </summary>
    public static async Task<BookPass> Load(ShaderLibrary shaders, RenderQueue<GraphicsDevice> gpu, ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(loader);
        var patched = EffectPatcher.Patch(XnAnimationEffect.Read(XnAnimationEffect.PathIn(loader.Files.Root))).Bytecode;
        var program = new ShaderProgram("XNAnimation/" + XnAnimationEffect.Forward, patched,
            [.. Fx2Effect.Parse(patched).Parameters.Where(p => p.IsShared).Select(p => p.Name)]);
        var inputs = EffectInputs.Read(patched);
        LevelBatch Prepare(LevelBatch batch) => VertexPadding.Complete(Unframed(batch), inputs);
        var effectTask = gpu.Enqueue(device => new GameEffect(program, new Effect(device, program.Bytecode)));
        var modelTasks = new[] { Room, Book, Stack, Sheet }.Select(asset => GpuLevel.Load(loader, asset, gpu, prepare: Prepare)).ToArray();
        var postTasks = new[] { shaders.Load("FilterEffect"), shaders.Load("BloomEffect") };   // the library's: not ours to dispose
        try
        {
            await Task.WhenAll([effectTask, .. modelTasks, .. postTasks]).ConfigureAwait(false);
            return await gpu.Enqueue(device =>
            {
                if (effectTask.Result.Effect.Techniques[XnAnimationEffect.Forward] is null
                    || effectTask.Result.Effect.Techniques[XnAnimationEffect.Shadow] is null)
                {
                    throw new InvalidDataException($"the book's effect lacks {XnAnimationEffect.Forward} or {XnAnimationEffect.Shadow}");
                }
                EffectChecks.Require(effectTask.Result, Parameters);
                var post = new PostPass(postTasks[0].Result, postTasks[1].Result);       // checks, and makes its targets only when drawing
                return new BookPass(effectTask.Result, modelTasks[0].Result, modelTasks[1].Result, modelTasks[2].Result, modelTasks[3].Result, post,
                    new FullscreenQuad(device));
            }).ConfigureAwait(false);
        }
        catch
        {
            var made = new List<IDisposable>();
            made.AddRange(modelTasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result));
            if (effectTask.IsCompletedSuccessfully)
            {
                made.Add(effectTask.Result);
            }
            _ = gpu.Enqueue(_ => made.ForEach(m => m.Dispose()));
            throw;
        }
    }

    /// <summary>
    /// A mesh without the tangent frame the effect reads (the room's) gets a frame of nothing, which leaves its normal
    /// as it is: what the original's Direct3D 9 read for an element a mesh lacked (ours: a reading, not seen).
    /// </summary>
    internal static LevelBatch Unframed(LevelBatch batch)
    {
        foreach (var usage in new[] { VertexElementUsageXna31.Tangent, VertexElementUsageXna31.Binormal })
        {
            if (!batch.Declaration.Elements.Any(e => e.Usage == usage && e.UsageIndex == 0))
            {
                batch = VertexPadding.Append(batch, new ShaderInput(usage, 0), VertexElementFormatXna31.Vector3, new byte[12]);
            }
        }
        return batch;
    }

    /// <summary>The book's frame onto the screen (the back buffer), its pages showing the atlas.</summary>
    public void Draw(GraphicsDevice device, BookPicture picture, Texture2D pages, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(pages);
        if (scene is null || scene.Width != width || scene.Height != height)
        {
            scene?.Dispose();
            scene = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, PostPass.GuiSamples,
                RenderTargetUsage.DiscardContents);
        }
        var lightViewProjection = LightViewProjection(picture.Light);
        DrawShadows(device, picture, pages, lightViewProjection);
        device.SetRenderTarget(scene);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        effect.Effect.CurrentTechnique = effect.Effect.Techniques[XnAnimationEffect.Forward];
        parameters.SetValue("View", picture.View);
        parameters.SetValue("Projection", picture.Projection);
        parameters.SetValue("ViewProjection", picture.View * picture.Projection);
        parameters.SetValue("CameraPosition", picture.Camera);
        parameters.SetValue("LightPosition", picture.Light);
        parameters.SetValue("LightAmbientColor", picture.Ambient);
        parameters.SetValue("LightDiffuseColor", picture.Diffuse);
        parameters.SetValue("LightSpecularColor", picture.Specular);
        parameters.SetValue("LightViewProjection", lightViewProjection);
        parameters.SetValue("NormalPower0", 1f);
        parameters.SetValue("ShadowMapScale", 1f / ShadowSize);
        parameters.SetValue("shadowMapEnabled", true);
        parameters.SetValue("shadowMap", shadow);
        device.SamplerStates[ShadowSlot] = SamplerState.PointClamp;          // as the effect declares it: it blends its four taps itself
        Samplers(device, SamplerState.LinearWrap);
        if (picture.Room)
        {
            Draw(device, room, still, BlendState.NonPremultiplied, null);    // as the GUI left the blending: the quill's feather blends
        }
        Draw(device, book, picture.Book, BlendState.Opaque, null);
        Samplers(device, SamplerState.LinearClamp);
        Draw(device, stack, picture.Right, BlendState.NonPremultiplied, pages);
        Draw(device, stack, picture.Left, BlendState.NonPremultiplied, pages);
        for (var i = 0; i < Math.Min(picture.SheetCount, picture.Sheets.Length); i++)
        {
            Draw(device, sheet, picture.Sheets[i], BlendState.NonPremultiplied, pages);
        }
        device.SetRenderTarget(null);
        for (var slot = 0; slot <= ShadowSlot; slot++)
        {
            device.Textures[slot] = null;
        }
        post.Draw(device, quad, scene, PostProcessing.Default);
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
    }

    /// <summary>The candle's camera: from the candle towards the book's middle, a wide square lens (108°) from 1 to 15.</summary>
    public static Matrix LightViewProjection(Vector3 light) =>
        Matrix.CreateLookAt(light, ShadowAim, Vector3.Up) * Matrix.CreatePerspectiveFieldOfView(ShadowFieldOfView, 1, 1, 15);

    /// <summary>
    /// The candle's shadow map, as the original's: a 1024² single-float target cleared to 1, each caster's distance from
    /// the candle in its camera written by the effect's shadow technique (depth tested, less or equal): the sheets, the
    /// right stack, the left one, the book, the room.
    /// </summary>
    private void DrawShadows(GraphicsDevice device, BookPicture picture, Texture2D pages, Matrix lightViewProjection)
    {
        shadow ??= new RenderTarget2D(device, ShadowSize, ShadowSize, false, SurfaceFormat.Single, DepthFormat.Depth24);
        device.SetRenderTarget(shadow);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Vector4.One, 1, 0);
        device.DepthStencilState = LessEqual;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        effect.Effect.CurrentTechnique = effect.Effect.Techniques[XnAnimationEffect.Shadow];
        parameters.SetValue("ViewProjection", lightViewProjection);
        parameters.SetValue("shadowMap", null);                               // not read while it is drawn into
        parameters.SetValue("shadowMapEnabled", false);
        parameters.SetValue("bloat", 0f);
        Samplers(device, SamplerState.LinearClamp);
        for (var i = 0; i < Math.Min(picture.SheetCount, picture.Sheets.Length); i++)
        {
            Draw(device, sheet, picture.Sheets[i], BlendState.Opaque, pages);
        }
        Draw(device, stack, picture.Right, BlendState.Opaque, pages);
        Draw(device, stack, picture.Left, BlendState.Opaque, pages);
        Samplers(device, SamplerState.LinearWrap);
        Draw(device, book, picture.Book, BlendState.Opaque, null);
        Draw(device, room, still, BlendState.Opaque, null);                   // a caster even where it is not drawn, as the original's
        device.SetRenderTarget(null);
    }

    /// <summary>A model's parts with its materials (the pages' picture, decoded, in place of their paper) and a palette.</summary>
    private void Draw(GraphicsDevice device, GpuLevel model, Matrix[] palette, BlendState blend, Texture2D? pages)
    {
        device.BlendState = blend;
        effect.Effect.Parameters["matBones"].SetValue(palette);
        foreach (var batch in model.Batches)
        {
            if (batch.Material is not SkinnedModelBasicEffectContent material)
            {
                continue;
            }
            parameters.SetValue("DiffuseColor", material.DiffuseColor);
            parameters.SetValue("SpecularAmount", material.SpecularAmount);
            parameters.SetValue("SpecularPower", material.SpecularPower);
            parameters.SetValue("diffuseIsHDR", pages is not null);
            Map("diffuseMap0", "diffuseMap0Enabled", pages ?? Texture(model, material.DiffuseMap0Enabled, material.DiffuseMap0));
            Map("specularMap0", "specularMapEnabled", Texture(model, material.SpecularMapEnabled, material.SpecularMap));
            Map("normalMap0", "normalMapEnabled", Texture(model, material.NormalMapEnabled, material.NormalMap));
            device.SetVertexBuffer(batch.Vertices);
            device.Indices = batch.Indices;
            foreach (var pass in effect.Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.VertexCount, batch.StartIndex, batch.PrimitiveCount);
            }
        }
    }

    /// <summary>The diffuse, normal and specular maps' samplers (the effect's own states, where it has them, come over these).</summary>
    private static void Samplers(GraphicsDevice device, SamplerState state)
    {
        for (var slot = 0; slot < 3; slot++)
        {
            device.SamplerStates[slot] = state;
        }
    }

    private static Texture? Texture(GpuLevel model, bool enabled, ContentRef<Texture2DContent> map) => enabled ? model.TextureFor(map.External) : null;

    private void Map(string name, string flag, Texture? texture)
    {
        parameters.SetValue(name, texture);
        parameters.SetValue(flag, texture is not null);
    }

    public void Dispose()
    {
        scene?.Dispose();
        shadow?.Dispose();
        post.Dispose();
        quad.Dispose();
        room.Dispose();
        book.Dispose();
        stack.Dispose();
        sheet.Dispose();
        effect.Dispose();
    }
}
