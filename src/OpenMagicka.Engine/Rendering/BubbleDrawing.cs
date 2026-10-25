using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// A character's shield bubble as drawn: its model (by asset) posed by its bones (in the world already), where it stands
/// and faces, how bright (a flash after a blow), and its three noise layers' drift.
/// </summary>
public sealed record BubblePicture(string Asset, Matrix[] Bones, Vector3 Position, Vector3 Facing, float Brightness, Vector2 Noise0, Vector2 Noise1,
    Vector2 Noise2);

/// <summary>
/// The shield bubbles, as the original draws them (docs/engine/ARCHITECTURE.md §17): the character's own first mesh
/// part again, swollen a little along its normals, through <c>SkinnedShieldEffect</c> with the noise of
/// <c>EffectTextures/Shield</c> cast on it from three sides, with the additive objects. Render thread only.
/// </summary>
public sealed class BubbleDrawing(GameEffect effect, Texture2D noise)
{
    public const float Bloat = 0.075f;

    private readonly GameEffect effect = Checked(effect);
    private readonly Texture2D noise = noise ?? throw new ArgumentNullException(nameof(noise));
    private Matrix[] palette = [];

    private static GameEffect Checked(GameEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        EffectChecks.Require(effect, ["ProjectionMap", "Bones", "Color", "TextureScale", "TextureOffset0", "TextureOffset1", "TextureOffset2",
            "ProjectionMapMatrix0", "ProjectionMapMatrix1", "ProjectionMapMatrix2", "Bloat"]);
        return effect;
    }

    /// <summary>The three projections of the noise: from where the character looks, then turned a third round twice.</summary>
    public static Matrix[] Projections(Vector3 position, Vector3 facing)
    {
        var flat = facing with { Y = 0 };
        var first = Matrix.CreateLookAt(position, position + (flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : Vector3.Forward), Vector3.Up);
        var second = first * Matrix.CreateRotationY(MathHelper.TwoPi / 3);
        return [first, second, second * Matrix.CreateRotationY(MathHelper.TwoPi / 3)];
    }

    /// <param name="modelOf">A model by asset, once it is on the GPU; null while it loads.</param>
    public void Draw(GraphicsDevice device, IReadOnlyList<BubblePicture> bubbles, Func<string, GpuLevel?> modelOf)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bubbles);
        ArgumentNullException.ThrowIfNull(modelOf);
        if (bubbles.Count == 0)
        {
            return;
        }
        device.BlendState = DeferredRenderer.AdditiveBlend;
        device.DepthStencilState = DepthStencilState.DepthRead;
        var parameters = new FnaEffectParameters(effect.Effect);
        var bones = effect.Effect.Parameters["Bones"];
        if (palette.Length != bones.Elements.Count)
        {
            palette = new Matrix[bones.Elements.Count];
        }
        parameters.SetValue("ProjectionMap", noise);
        parameters.SetValue("TextureScale", new Vector2(0.5f));
        parameters.SetValue("Bloat", Bloat);
        foreach (var bubble in bubbles)
        {
            if (modelOf(bubble.Asset)?.Batches.FirstOrDefault(b => b.Material is SkinnedModelBasicEffectContent) is not { } batch)
            {
                continue;
            }
            for (var i = 0; i < palette.Length; i++)
            {
                palette[i] = i < bubble.Bones.Length ? bubble.Bones[i] : Matrix.Identity;
            }
            bones.SetValue(palette);
            parameters.SetValue("Color", new Vector4(2, 1.5f, 1, bubble.Brightness));
            parameters.SetValue("TextureOffset0", bubble.Noise0);
            parameters.SetValue("TextureOffset1", bubble.Noise1);
            parameters.SetValue("TextureOffset2", bubble.Noise2);
            var projections = Projections(bubble.Position, bubble.Facing);
            for (var i = 0; i < projections.Length; i++)
            {
                parameters.SetValue($"ProjectionMapMatrix{i}", projections[i]);
            }
            device.SetVertexBuffer(batch.Vertices);
            device.Indices = batch.Indices;
            foreach (var pass in effect.Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.RasterizerState = RasterizerState.CullNone;
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.VertexCount, batch.StartIndex, batch.PrimitiveCount);
            }
        }
    }
}
