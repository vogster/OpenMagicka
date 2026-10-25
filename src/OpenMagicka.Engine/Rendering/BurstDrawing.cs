using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The lightning bolts and the arcane blasts' discs, as the original draws them (docs/engine/ARCHITECTURE.md §17),
/// through <c>AdditiveEffect</c> with the additive objects. A bolt is one of <see cref="Shapes"/> fixed zig-zag
/// strips, 1 wide, a pair every 2 along, on the lightning row of <c>EffectTextures/Beams</c>, stretched from its
/// start to its end and turned to the camera; a disc is two squares of <c>EffectTextures/ArcaneDisc</c> turning
/// against each other, one band of it for each element. Render thread only.
/// </summary>
public sealed class BurstDrawing : IDisposable
{
    /// <summary>How many zig-zags the bolts take theirs from (the original keeps a pool of bolts, each with its own).</summary>
    public const int Shapes = 16;

    /// <summary>A bolt's pairs of vertices, and so at most 32 triangles.</summary>
    public const int Pairs = 17;

    public const int MaxPrimitives = (Pairs - 1) * 2;

    /// <summary>Each element's band of the disc's sheet, by the element's bit (Earth, Shield and Ice share Water's).</summary>
    public static readonly float[] DiscBands = [0, 0, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0, 0, 0.75f, 0.875f];

    private readonly GameEffect additive;
    private readonly Texture2D beams;
    private readonly Texture2D disc;
    private readonly VertexBuffer bolts;
    private readonly VertexBuffer square;

    public BurstDrawing(GraphicsDevice device, GameEffect additive, Texture2D beams, Texture2D disc)
    {
        ArgumentNullException.ThrowIfNull(device);
        (this.additive, this.beams, this.disc) = (additive ?? throw new ArgumentNullException(nameof(additive)),
            beams ?? throw new ArgumentNullException(nameof(beams)), disc ?? throw new ArgumentNullException(nameof(disc)));
        bolts = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, Shapes * Pairs * 2, BufferUsage.WriteOnly);
        try
        {
            bolts.SetData(Enumerable.Range(0, Shapes).SelectMany(Zigzag).ToArray());
            square = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
            square.SetData(Square());
        }
        catch
        {
            bolts.Dispose();
            throw;
        }
    }

    /// <summary>
    /// One bolt's zig-zag (its number seeds it): pairs 1 across along −Z, every one after the first pushed aside by up
    /// to 0.75; its U runs on from a random eighth of the sheet, its V over the lightning row.
    /// </summary>
    public static VertexPositionColorTexture[] Zigzag(int shape)
    {
        var random = new Random(shape);
        var start = random.Next(8) * 0.125f;
        var vertices = new VertexPositionColorTexture[Pairs * 2];
        for (var k = 0; k < Pairs; k++)
        {
            var aside = k == 0 ? 0 : (random.NextSingle() * 2 - 1) * 0.75f;
            for (var side = 0; side < 2; side++)
            {
                vertices[k * 2 + side] = new VertexPositionColorTexture(new Vector3(-(side - 0.5f + aside), 0, -2 * k), Color.White,
                    new Vector2(k * 0.125f + start, 0.375f + side * 0.125f));
            }
        }
        return vertices;
    }

    /// <summary>How many of the strip's triangles a bolt this long takes: its length in pairs, rounded to an even count, at most 32.</summary>
    public static int Primitives(float length) =>
        float.IsFinite(length) && length > 0 ? (int)MathF.Min(MathF.Floor(length / 2 + 0.5f) * 2, MaxPrimitives) : 0;

    /// <summary>Where a bolt's strip is put: from its start, its −Z to its end and stretched to reach it, its face to the eye.</summary>
    public static Matrix BoltPlace(BoltPicture bolt, Vector3 eye)
    {
        var along = bolt.To - bolt.From;
        var length = along.Length();
        var primitives = Primitives(length);
        var forward = along / length;
        var right = Vector3.Cross(forward, eye - bolt.From);
        right = right.LengthSquared() > 1e-12f ? Vector3.Normalize(right) : Vector3.Normalize(Vector3.Cross(forward, Vector3.Up + Vector3.Right * 0.01f));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var turn = Matrix.Identity;
        (turn.Right, turn.Up, turn.Forward, turn.Translation) = (right, up, forward, bolt.From);
        return Matrix.CreateScale(1, 1, length / primitives) * turn;
    }

    /// <summary>The disc's square, as a strip: flat, from −1 to 1, facing up.</summary>
    public static VertexPositionColorTexture[] Square() =>
    [
        new(new Vector3(-1, 0, -1), Color.White, new Vector2(0, 0)), new(new Vector3(1, 0, -1), Color.White, new Vector2(1, 0)),
        new(new Vector3(-1, 0, 1), Color.White, new Vector2(0, 1)), new(new Vector3(1, 0, 1), Color.White, new Vector2(1, 1)),
    ];

    public void Draw(GraphicsDevice device, IReadOnlyList<BoltPicture> bolts, IReadOnlyList<DiscPicture> discs, Vector3 eye)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bolts);
        ArgumentNullException.ThrowIfNull(discs);
        if (bolts.Count == 0 && discs.Count == 0)
        {
            return;
        }
        device.BlendState = DeferredRenderer.AdditiveBlend;
        device.DepthStencilState = DepthStencilState.DepthRead;
        device.RasterizerState = RasterizerState.CullNone;
        var parameters = new FnaEffectParameters(additive.Effect);
        parameters.SetValue("VertexColorEnabled", false);
        parameters.SetValue("TextureEnabled", true);
        parameters.SetValue("TextureScale", Vector2.One);
        parameters.SetValue("TextureOffset", Vector2.Zero);
        parameters.SetValue("Texture", beams);
        device.SetVertexBuffer(this.bolts);
        foreach (var bolt in bolts)
        {
            var primitives = Primitives(Vector3.Distance(bolt.From, bolt.To));
            if (primitives <= 0 || !(bolt.Brightness > 0))
            {
                continue;
            }
            parameters.SetValue("World", BoltPlace(bolt, eye));
            parameters.SetValue("ColorTint", new Vector4(bolt.Colour * 2, bolt.Brightness));
            Apply(device, primitives, ((bolt.Shape % Shapes) + Shapes) % Shapes * Pairs * 2);
        }
        parameters.SetValue("Texture", disc);
        parameters.SetValue("TextureScale", new Vector2(1, 0.125f));
        device.SetVertexBuffer(square);
        foreach (var picture in discs.Where(d => d.Alpha > 0 && d.Radius > 0))
        {
            parameters.SetValue("ColorTint", new Vector4(1, 1, 1, picture.Alpha * 0.5f));
            for (var bit = 0; bit < DiscBands.Length; bit++)
            {
                if ((picture.Elements & (1 << bit)) == 0)
                {
                    continue;
                }
                parameters.SetValue("TextureOffset", new Vector2(0, DiscBands[bit]));
                foreach (var (scale, turn) in new[] { (1f, picture.Turn), (0.9f, -picture.Turn) })
                {
                    parameters.SetValue("World", Matrix.CreateScale(picture.Radius * scale) * Matrix.CreateRotationY(turn) * Matrix.CreateTranslation(picture.Centre));
                    Apply(device, 2, 0);
                }
            }
        }
    }

    private void Apply(GraphicsDevice device, int primitives, int start)
    {
        foreach (var pass in additive.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, start, primitives);
        }
    }

    public void Dispose()
    {
        bolts.Dispose();
        square.Dispose();
    }
}
