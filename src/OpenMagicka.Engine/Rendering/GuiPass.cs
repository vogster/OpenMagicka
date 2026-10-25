using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Text;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The screen over the world, as the original's GUI (docs/engine/ARCHITECTURE.md §18): drawn into the frame after the
/// shields and before the filter and bloom (so bright boxes bloom too), blended by their alpha. A speech box is a
/// 9-slice frame of <c>UI/HUD/Dialog_Say</c> (<c>TextBoxEffect</c>, 32 px borders) with its speaker's name and its text
/// in it, in the dialogs' tan. Built with the renderer when the game's content has it. Render thread only, but loading.
/// </summary>
public sealed partial class GuiPass : IDisposable
{
    public const string Frame = "UI/HUD/Dialog_Say";
    public const int Border = 32;
    public static readonly Vector4 DialogColour = new(0.7037f, 0.5576f, 0.3202f, 1);

    private readonly GameEffect box;
    private readonly Texture2D frame;
    private readonly VertexBuffer grid;
    private readonly IndexBuffer slices;

    private GuiPass(GuiText text, GameEffect box, Texture2D frame, IReadOnlyDictionary<string, GuiFont> fonts, VertexBuffer grid, IndexBuffer slices,
        (GameEffect Effect, Texture2D Pad, Texture2D Keyboard) prompt) =>
        (Text, this.box, this.frame, Fonts, this.grid, this.slices, (this.prompt, padPrompt, keyboardPrompt)) = (text, box, frame, fonts, grid, slices, prompt);

    public GuiText Text { get; }

    /// <summary>The fonts by the original's names (Maiandra14, Maiandra16, Maiandra18).</summary>
    public IReadOnlyDictionary<string, GuiFont> Fonts { get; }

    public IEnumerable<GameEffect> Effects => [Text.Effect, box, prompt];

    public static readonly string[] FontNames = ["Maiandra14", "Maiandra16", "Maiandra18"];

    /// <summary>Reads the effects, the frame and a language's fonts (English's when it ships none) and builds the pass on the render thread.</summary>
    public static async Task<GuiPass> Load(ShaderLibrary shaders, RenderQueue<GraphicsDevice> gpu, ContentLoader loader, string language)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(loader);
        Texture2DContent Picture(string asset) => loader.LoadUncached(asset).Root as Texture2DContent ?? throw new InvalidDataException($"{asset} is not a picture");
        var (frame, pad, keyboard, hud, digits) = (Picture(Frame), Picture(PadPrompt), Picture(KeyboardPrompt), Picture(HudAtlas), Picture(DigitsAtlas));
        var fonts = FontNames.ToDictionary(n => n, n =>
        {
            var own = $"Languages/{language}/Font/{n}";
            var asset = loader.Files.Exists(own + ".xnb") ? own : $"Languages/eng/Font/{n}";
            var font = loader.LoadUncached(asset).Root as BitmapFontContent ?? throw new InvalidDataException($"{n} is not a bitmap font");
            var atlas = font.Texture.Inline ?? (font.Texture.External is { } external ? loader.LoadReference(asset, external).Root as Texture2DContent : null)
                ?? throw new InvalidDataException($"{n} has no atlas");
            return (font, atlas);
        });
        var effects = new[] { shaders.Load("GUIBasicEffect"), shaders.Load("TextBoxEffect"), shaders.Load("NotifierButtonEffect") };
        try
        {
            await Task.WhenAll(effects).ConfigureAwait(false);
            return await gpu.Enqueue(device => Build(device, [.. effects.Select(e => e.Result)], [frame, pad, keyboard, hud, digits], fonts)).ConfigureAwait(false);
        }
        catch
        {
            var made = effects.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
            _ = gpu.Enqueue(_ => Array.ForEach(made, e => e.Dispose()));
            throw;
        }
    }

    /// <summary>Builds the pass from its effects (basic, box, prompt) and pictures (frame, pad prompt, keyboard prompt).</summary>
    private static GuiPass Build(GraphicsDevice device, GameEffect[] effects, Texture2DContent[] pictures,
        Dictionary<string, (BitmapFontContent Font, Texture2DContent Atlas)> fonts)
    {
        var (basic, box, prompt) = (effects[0], effects[1], effects[2]);
        EffectChecks.Require(box, ["ScreenSize", "Position", "Size", "Scale", "BorderSize", "Color", "Texture"]);
        EffectChecks.Require(prompt, ["ScreenSize", "Position", "Width", "Scale", "Color", "Texture"]);
        var made = new List<IDisposable>();
        try
        {
            var text = new GuiText(basic);
            var textures = new Texture2D[pictures.Length];
            for (var i = 0; i < pictures.Length; i++)
            {
                made.Add(textures[i] = GpuLevel.CreateTexture2D(device, pictures[i]));
            }
            var frameTexture = textures[0];
            var gpuFonts = new Dictionary<string, GuiFont>();
            foreach (var (name, (font, atlas)) in fonts)
            {
                var gpuFont = new GuiFont(new BitmapFont(font, BitmapFont.SpacingOf(name)), GpuLevel.CreateTexture2D(device, atlas));
                made.Add(gpuFont);
                gpuFonts[name] = gpuFont;
            }
            var (vertices, indices) = Grid();
            var grid = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            made.Add(grid);
            grid.SetData(vertices);
            var slices = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            made.Add(slices);
            slices.SetData(indices);
            return new GuiPass(text, box, frameTexture, gpuFonts, grid, slices, (prompt, textures[1], textures[2])) { hud = textures[3], digits = textures[4] };
        }
        catch
        {
            made.ForEach(m => m.Dispose());
            throw;
        }
    }

    /// <summary>
    /// The frame's 4 × 4 grid: corners at ±0.5 (the inner box, scaled by its size), "normals" −1/0/+1 that push the outer
    /// ring out by the border, texture coordinates 0, ¼, ¾, 1; nine quads.
    /// </summary>
    public static (VertexPositionNormalTexture[] Vertices, short[] Indices) Grid()
    {
        float[] corner = [-0.5f, -0.5f, 0.5f, 0.5f], push = [-1, 0, 0, 1], uv = [0, 0.25f, 0.75f, 1];
        var vertices = new VertexPositionNormalTexture[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                vertices[row * 4 + column] = new VertexPositionNormalTexture(new Vector3(corner[column], corner[row], 0),
                    new Vector3(push[column], push[row], 0), new Vector2(uv[column], uv[row]));
            }
        }
        var indices = new List<short>();
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var a = (short)(row * 4 + column);
                indices.AddRange([a, (short)(a + 1), (short)(a + 4), (short)(a + 1), (short)(a + 5), (short)(a + 4)]);
            }
        }
        return (vertices, [.. indices]);
    }

    /// <summary>A box's frame: its inner size, scaled about its centre, the border round it.</summary>
    public void DrawFrame(GraphicsDevice device, Vector2 centre, Point size, float scale, int width, int height)
    {
        var parameters = new FnaEffectParameters(box.Effect);
        parameters.SetValue("ScreenSize", new Vector2(width, height));
        parameters.SetValue("Position", centre);
        parameters.SetValue("Size", new Vector2(size.X, size.Y));
        parameters.SetValue("Scale", scale);
        parameters.SetValue("BorderSize", (float)Border);
        parameters.SetValue("Color", Vector4.One);
        parameters.SetValue("Texture", frame);
        device.SetVertexBuffer(grid);
        device.Indices = slices;
        foreach (var pass in box.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 16, 0, 18);
        }
    }

    public void Dispose()
    {
        grid.Dispose();
        slices.Dispose();
        frame.Dispose();
        foreach (var font in Fonts.Values)
        {
            font.Dispose();
        }
        Text.Effect.Dispose();
        box.Dispose();
        prompt.Dispose();
        padPrompt.Dispose();
        keyboardPrompt.Dispose();
        hud.Dispose();
        digits.Dispose();
    }
}
