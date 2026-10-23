using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The original's fullscreen quad: a triangle strip at (±1.5, ±1.5, 1) whose texture coordinates map (±1, ±1) to 0…1.</summary>
internal sealed class FullscreenQuad : IDisposable
{
    private readonly VertexBuffer vertices;

    public FullscreenQuad(GraphicsDevice device)
    {
        vertices = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
        vertices.SetData(new[]
        {
            new VertexPositionTexture(new Vector3(1.5f, 1.5f, 1), new Vector2(1.25f, -0.25f)),
            new VertexPositionTexture(new Vector3(1.5f, -1.5f, 1), new Vector2(1.25f, 1.25f)),
            new VertexPositionTexture(new Vector3(-1.5f, 1.5f, 1), new Vector2(-0.25f, -0.25f)),
            new VertexPositionTexture(new Vector3(-1.5f, -1.5f, 1), new Vector2(-0.25f, 1.25f)),
        });
    }

    /// <summary>Draws a fullscreen effect with <paramref name="blend"/> and no depth test.</summary>
    public void Draw(GraphicsDevice device, GameEffect effect, BlendState blend)
    {
        device.BlendState = blend;
        device.DepthStencilState = DepthStencilState.None;
        // The effects only use the size for the half-pixel shift (0.5 / size); an infinite size makes it zero.
        new FnaEffectParameters(effect.Effect).SetValue("DestinationDimensions", new Vector2(float.PositiveInfinity));
        device.SetVertexBuffer(vertices);
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }
    }

    public void Dispose() => vertices.Dispose();
}
