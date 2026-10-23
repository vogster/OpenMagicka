using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The original's fullscreen quad: a triangle strip at (±1.5, ±1.5, 1) whose texture coordinates map (±1, ±1) to 0…1.</summary>
internal sealed class FullscreenQuad : IDisposable
{
    private static readonly VertexPositionTexture[] Corners =
    [
        new(new Vector3(1.5f, 1.5f, 1), new Vector2(1.25f, -0.25f)),
        new(new Vector3(1.5f, -1.5f, 1), new Vector2(1.25f, 1.25f)),
        new(new Vector3(-1.5f, 1.5f, 1), new Vector2(-0.25f, -0.25f)),
        new(new Vector3(-1.5f, -1.5f, 1), new Vector2(-0.25f, 1.25f)),
    ];

    private readonly VertexBuffer vertices;
    private readonly DynamicVertexBuffer shifted;

    public FullscreenQuad(GraphicsDevice device)
    {
        vertices = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
        try
        {
            vertices.SetData(Corners);
            shifted = new DynamicVertexBuffer(device, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
        }
        catch
        {
            vertices.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Draws a fullscreen effect with <paramref name="blend"/>, no depth test unless <paramref name="depth"/>
    /// says otherwise, and the target's size as <paramref name="destination"/> for an effect that needs it.
    /// <paramref name="uvShift"/> moves the texture coordinates, to cancel a half-pixel shift an effect
    /// adds itself (see the half-pixel note in docs/engine/ARCHITECTURE.md §3.2).
    /// </summary>
    public void Draw(GraphicsDevice device, GameEffect effect, BlendState blend, DepthStencilState? depth = null, Vector2? destination = null,
        Vector2? uvShift = null)
    {
        device.BlendState = blend;
        device.DepthStencilState = depth ?? DepthStencilState.None;
        // Most effects only use the size for the half-pixel shift (0.5 / size); an infinite size makes it zero.
        new FnaEffectParameters(effect.Effect).SetValue("DestinationDimensions", destination ?? new Vector2(float.PositiveInfinity));
        if (uvShift is { } shift)
        {
            shifted.SetData(Corners.Select(c => c with { TextureCoordinate = c.TextureCoordinate + shift }).ToArray(), 0, 4, SetDataOptions.Discard);
            device.SetVertexBuffer(shifted);
        }
        else
        {
            device.SetVertexBuffer(vertices);
        }
        foreach (var pass in effect.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }
    }

    public void Dispose()
    {
        vertices.Dispose();
        shifted.Dispose();
    }
}
