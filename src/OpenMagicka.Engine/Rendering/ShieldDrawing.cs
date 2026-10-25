using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>A spell shield's form: a whole sphere, a sphere cut to a cap facing its way, or a wall.</summary>
public enum ShieldForm : byte { Sphere, Disc, Wall }

/// <summary>
/// A spell shield as drawn: its form, where (the mesh's place, scaled to it), its colour and how far it has faded
/// in, its three noise layers' drift, its ripples (up to 16: where, how strong), its cap (the cosine of its half
/// angle; −1 for a whole one), its way and its noise's tiling.
/// </summary>
public sealed record ShieldPicture(ShieldForm Form, Matrix World, Vector3 Colour, float Fade, Vector2 Noise0, Vector2 Noise1, Vector2 Noise2,
    Vector4[] Ripples, float MinDot, Vector3 Direction, float TextureScale);

/// <summary>
/// The spell shields, as the original draws them (docs/engine/ARCHITECTURE.md §17): <c>ShieldEffect</c> over a sphere
/// built in code (its bottom open; a disc is the sphere cut by the shader) or a lens-shaped wall, its noise from
/// <c>EffectTextures/shield</c>, added onto the frame after the particles and cut where the scene stands in front
/// (by the depth map). Render thread only.
/// </summary>
public sealed class ShieldDrawing : IDisposable
{
    public const int Ripples = 16;

    private static readonly BlendState Adding = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One,
    };

    private readonly GameEffect effect;
    private readonly Texture2D noise;
    private readonly (VertexBuffer Vertices, IndexBuffer Indices) sphere;
    private readonly (VertexBuffer Vertices, IndexBuffer Indices) wall;
    private readonly EffectTechnique sphereTechnique;
    private readonly EffectTechnique wallTechnique;

    public ShieldDrawing(GraphicsDevice device, GameEffect effect, Texture2D noise)
    {
        ArgumentNullException.ThrowIfNull(device);
        (this.effect, this.noise) = (effect ?? throw new ArgumentNullException(nameof(effect)), noise ?? throw new ArgumentNullException(nameof(noise)));
        if (effect.Effect.Techniques.Count < 2)
        {
            throw new InvalidDataException("ShieldEffect has no Sphere and Wall techniques");
        }
        (sphereTechnique, wallTechnique) = (effect.Effect.Techniques["Sphere"] ?? effect.Effect.Techniques[0], effect.Effect.Techniques["Wall"] ?? effect.Effect.Techniques[1]);
        EffectChecks.Require(effect, ["World", "View", "Projection", "Thickness", "ColorTint", "DamagePoints", "TextureScale", "Noise0Offset", "Noise1Offset",
            "Noise2Offset", "MinDotProduct", "Direction", "DepthMap", "NormalMap"]);
        if (effect.Effect.Parameters["DamagePoints"].Elements.Count < Ripples)
        {
            throw new InvalidDataException($"ShieldEffect's DamagePoints holds fewer than {Ripples} ripples");   // the array is copied as it is
        }
        sphere = Upload(device, Sphere());
        try
        {
            wall = Upload(device, Wall());
        }
        catch
        {
            sphere.Vertices.Dispose();
            sphere.Indices.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The unit sphere: 65 columns round by 31 rows up, from just above the bottom (its pole left open) to just
    /// short of the top; each vertex's normal its place, its U round and V up.
    /// </summary>
    public static (VertexPositionNormalTexture[] Vertices, ushort[] Indices) Sphere()
    {
        const int columns = 65, rows = 31;
        var vertices = new VertexPositionNormalTexture[columns * rows];
        for (var i = 0; i < columns; i++)
        {
            var round = MathHelper.TwoPi * i / 64;
            for (var j = 0; j < rows; j++)
            {
                var up = -MathHelper.PiOver2 + MathHelper.Pi * (j + 1.9f) / 32;
                var at = new Vector3(MathF.Cos(round) * MathF.Cos(up), MathF.Sin(up), MathF.Sin(round) * MathF.Cos(up));
                vertices[i * rows + j] = new VertexPositionNormalTexture(at, at, new Vector2(i / 64f, j / 32f));
            }
        }
        var indices = new List<ushort>();
        for (var k = 0; k < columns - 1; k++)
        {
            for (var l = 0; l < rows - 1; l++)
            {
                var b = k * rows + l;
                indices.AddRange([(ushort)(b + rows), (ushort)(b + 1), (ushort)b, (ushort)(b + rows), (ushort)(b + rows + 1), (ushort)(b + 1)]);
            }
        }
        return (vertices, [.. indices]);
    }

    /// <summary>
    /// The wall: a plate 12 long (along Z) and 6 high, bulging to 0.2 thick at its middle; its outline, two rings
    /// within it and a centre on each face, the front facing +X, the back −X.
    /// </summary>
    public static (VertexPositionNormalTexture[] Vertices, ushort[] Indices) Wall()
    {
        var outline = new (Vector3 At, Vector2 Uv)[40];
        for (var m = 0; m < 20; m++)
        {
            var t = -MathF.Cos(MathHelper.Pi * m / 20);
            var height = 3 * MathF.Pow(1 - t * t, 0.15f);
            var uv = Vector2.Normalize(new Vector2(t, MathF.Pow(1 - t * t, 0.15f)));
            outline[m] = (new Vector3(0, height, 6 * t), uv);
            outline[m + 20] = (new Vector3(0, -height, -6 * t), -uv);
        }
        var vertices = new List<VertexPositionNormalTexture>();
        foreach (var side in new[] { 1f, -1f })
        {
            foreach (var (scale, thickness) in new[] { (1f, 0f), (0.666f, 0.1332f), (0.333f, 0.18f) })
            {
                vertices.AddRange(outline.Select(o => new VertexPositionNormalTexture(new Vector3(thickness * side, o.At.Y * scale, o.At.Z * scale),
                    Vector3.UnitX * side, o.Uv * scale)));
            }
            vertices.Add(new VertexPositionNormalTexture(new Vector3(0.2f * side, 0, 0), Vector3.UnitX * side, Vector2.Zero));
        }
        var indices = new List<ushort>();
        for (var n = 0; n < 40; n++)
        {
            var n1 = (n + 1) % 40;
            indices.AddRange(Ushorts(n, n + 40, n1, n + 40, n1 + 40, n1, n + 40, n + 80, n1 + 40, n + 80, n1 + 80, n1 + 40, n + 80, 120, n1 + 80));
        }
        const int back = 121;
        for (var n = 0; n < 40; n++)
        {
            var n1 = (n + 1) % 40;
            indices.AddRange(Ushorts(back + n, back + n1, back + 40 + n, back + 40 + n, back + n1, back + 40 + n1, back + 40 + n, back + 40 + n1, back + 80 + n,
                back + 80 + n, back + 40 + n1, back + 80 + n1, back + 80 + n, back + 80 + n1, 241));
        }
        return ([.. vertices], [.. indices]);
    }

    private static IEnumerable<ushort> Ushorts(params int[] values) => values.Select(v => (ushort)v);

    private static (VertexBuffer, IndexBuffer) Upload(GraphicsDevice device, (VertexPositionNormalTexture[] Vertices, ushort[] Indices) mesh)
    {
        var vertices = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, mesh.Vertices.Length, BufferUsage.WriteOnly);
        IndexBuffer? indices = null;
        try
        {
            vertices.SetData(mesh.Vertices);
            indices = new IndexBuffer(device, IndexElementSize.SixteenBits, mesh.Indices.Length, BufferUsage.WriteOnly);
            indices.SetData(mesh.Indices);
            return (vertices, indices);
        }
        catch
        {
            vertices.Dispose();
            indices?.Dispose();
            throw;
        }
    }

    /// <summary>The shields, added onto the bound target, each cut by the scene's depth (no depth test of its own).</summary>
    public void Draw(GraphicsDevice device, IReadOnlyList<ShieldPicture> shields, GlobalEffectValues globals, Texture2D depth)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(shields);
        ArgumentNullException.ThrowIfNull(globals);
        if (shields.Count == 0)
        {
            return;
        }
        device.BlendState = Adding;
        device.DepthStencilState = DepthStencilState.None;
        var parameters = new FnaEffectParameters(effect.Effect);
        parameters.SetValue("View", globals.View);
        parameters.SetValue("Projection", globals.Projection);
        parameters.SetValue("Thickness", 0.2f);
        parameters.SetValue("NormalMap", noise);
        parameters.SetValue("DepthMap", depth);
        var ripples = effect.Effect.Parameters["DamagePoints"];
        foreach (var shield in shields.Where(s => s.Fade > 0 && s.Ripples.Length == Ripples))
        {
            var (vertices, indices) = shield.Form == ShieldForm.Wall ? wall : sphere;
            effect.Effect.CurrentTechnique = shield.Form == ShieldForm.Wall ? wallTechnique : sphereTechnique;
            parameters.SetValue("World", shield.World);
            parameters.SetValue("ColorTint", new Vector4(shield.Colour, shield.Fade));
            parameters.SetValue("TextureScale", new Vector2(shield.TextureScale));
            parameters.SetValue("Noise0Offset", shield.Noise0);
            parameters.SetValue("Noise1Offset", shield.Noise1);
            parameters.SetValue("Noise2Offset", shield.Noise2);
            parameters.SetValue("MinDotProduct", shield.MinDot);
            parameters.SetValue("Direction", shield.Direction);
            ripples.SetValue(shield.Ripples);
            device.SetVertexBuffer(vertices);
            device.Indices = indices;
            foreach (var pass in effect.Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.RasterizerState = RasterizerState.CullCounterClockwise;
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertices.VertexCount, 0, indices.IndexCount / 3);
            }
        }
        device.RasterizerState = RasterizerState.CullNone;
    }

    public void Dispose()
    {
        sphere.Vertices.Dispose();
        sphere.Indices.Dispose();
        wall.Vertices.Dispose();
        wall.Indices.Dispose();
    }
}
