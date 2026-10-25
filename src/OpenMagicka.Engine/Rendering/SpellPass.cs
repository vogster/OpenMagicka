using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The spells' own pictures in the deferred pipeline (docs/engine/ARCHITECTURE.md §17): their lights with the
/// level's (<see cref="CapsuleLights"/>), their shapes with the additive objects (<see cref="BeamDrawing"/>,
/// <see cref="BurstDrawing"/>), the shields over everything (<see cref="ShieldDrawing"/>). Built
/// with the renderer when the game's content has what they need; without it the spells show only their particles.
/// Render thread only, but for loading.
/// </summary>
public sealed class SpellPass : IDisposable
{
    public const string BeamSheet = "EffectTextures/Beams";
    public const string DiscSheet = "EffectTextures/ArcaneDisc";
    public const string ShieldNoise = "EffectTextures/shield";

    private readonly Texture2D[] sheets;

    private SpellPass(CapsuleLights capsules, BeamDrawing beams, BurstDrawing bursts, ShieldDrawing shields, BubbleDrawing bubbles, Texture2D[] sheets,
        GameEffect arcane, GameEffect shield, GameEffect bubble) =>
        (Capsules, Beams, Bursts, Shields, Bubbles, this.sheets, Arcane, Shield, Bubble) = (capsules, beams, bursts, shields, bubbles, sheets, arcane, shield, bubble);

    public CapsuleLights Capsules { get; }

    public BeamDrawing Beams { get; }

    public BurstDrawing Bursts { get; }

    public ShieldDrawing Shields { get; }

    public BubbleDrawing Bubbles { get; }

    private GameEffect Shield { get; }

    private GameEffect Bubble { get; }

    private GameEffect Arcane { get; }

    /// <summary>Its effects, which take the frame's shared values.</summary>
    public IEnumerable<GameEffect> Effects => [Arcane, Capsules.Effect, Shield, Bubble];

    /// <summary>
    /// Reads the effects, the sheets and the capsule on the calling thread and builds the pass on the render thread;
    /// <paramref name="additive"/> stays the renderer's.
    /// </summary>
    public static async Task<SpellPass> Load(ShaderLibrary shaders, RenderQueue<GraphicsDevice> gpu, ContentLoader loader, GameEffect additive)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(additive);
        var sheets = new[] { BeamSheet, DiscSheet, ShieldNoise }.Select(path => loader.LoadUncached(path).Root as Texture2DContent
            ?? throw new InvalidDataException($"{path} is not a picture")).ToArray();
        var capsule = loader.LoadUncached(CapsuleLights.Mesh).Root as ModelContent ?? throw new InvalidDataException($"{CapsuleLights.Mesh} is not a model");
        var loading = new[] { shaders.Load("ArcaneEffect"), shaders.Load("CapsuleLight"), shaders.Load("ShieldEffect"), shaders.Load("SkinnedShieldEffect") };
        try
        {
            await Task.WhenAll(loading).ConfigureAwait(false);
        }
        catch
        {
            Release(gpu, loading);
            throw;
        }
        var (arcane, light, shield, bubble) = (loading[0].Result, loading[1].Result, loading[2].Result, loading[3].Result);
        try
        {
            return await gpu.Enqueue(device => Build(device, arcane, light, shield, bubble, additive, sheets, capsule)).ConfigureAwait(false);
        }
        catch
        {
            Release(gpu, loading);
            throw;
        }
    }

    private static SpellPass Build(GraphicsDevice device, GameEffect arcane, GameEffect light, GameEffect shield, GameEffect bubble, GameEffect additive,
        Texture2DContent[] sheets, ModelContent capsule)
    {
        var textures = new List<Texture2D>();
        CapsuleLights? capsules = null;
        BeamDrawing? beams = null;
        BurstDrawing? bursts = null;
        ShieldDrawing? shields = null;
        try
        {
            textures.AddRange(sheets.Select(sheet => GpuLevel.CreateTexture2D(device, sheet)));
            capsules = CapsuleLights.Create(device, light, capsule);
            beams = new BeamDrawing(device, arcane, additive, textures[0]);
            bursts = new BurstDrawing(device, additive, textures[0], textures[1]);
            shields = new ShieldDrawing(device, shield, textures[2]);
            return new SpellPass(capsules, beams, bursts, shields, new BubbleDrawing(bubble, textures[2]), [.. textures], arcane, shield, bubble);
        }
        catch
        {
            shields?.Dispose();
            bursts?.Dispose();
            beams?.Dispose();
            capsules?.Dispose();                                              // with the light's effect (the caller's release finds it gone)
            textures.ForEach(t => t.Dispose());
            throw;
        }
    }

    private static void Release(RenderQueue<GraphicsDevice> gpu, IEnumerable<Task<GameEffect>> effects)
    {
        var made = effects.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
        if (made.Length > 0)
        {
            _ = gpu.Enqueue(_ => Array.ForEach(made, e => e.Dispose()));
        }
    }

    /// <summary>The spells' shapes, with the additive objects (after the composition, before the particles).</summary>
    /// <param name="modelOf">The frame's models by asset (the bubbles are drawn on theirs); none: no bubbles.</param>
    public void Draw(GraphicsDevice device, SpellPictures pictures, GlobalEffectValues globals, Func<string, GpuLevel?>? modelOf = null)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        Beams.Draw(device, pictures.Beams, globals);
        Bursts.Draw(device, pictures.Bolts, pictures.Discs, globals.CameraPosition);
        if (modelOf is not null)
        {
            Bubbles.Draw(device, pictures.Bubbles, modelOf);
        }
    }

    /// <summary>The shields, the original's post effects: over the particles, cut by the scene's depth.</summary>
    public void DrawShields(GraphicsDevice device, SpellPictures pictures, GlobalEffectValues globals, Texture2D depth)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        Shields.Draw(device, pictures.Shields, globals, depth);
    }

    public void Dispose()
    {
        Beams.Dispose();
        Bursts.Dispose();
        Shields.Dispose();
        Capsules.Dispose();
        Array.ForEach(sheets, s => s.Dispose());
        Arcane.Dispose();
        Shield.Dispose();
        Bubble.Dispose();
    }
}
