using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The beams, as the original draws them (docs/engine/ARCHITECTURE.md §17). A beam of Arcane or Life alone is
/// <c>ArcaneEffect</c>'s Beam technique over one strip of 512 vertices: the shader bends the strip along the beam
/// and turns it to the camera; first a straight core, then helices wound round it (wide near the caster, then
/// narrowing; a branch's stay narrow). A beam with other elements shows only their rows of the sheet
/// <c>EffectTextures/Beams</c>, two crossed strips each, through <c>AdditiveEffect</c>. Render thread only.
/// </summary>
public sealed class BeamDrawing : IDisposable
{
    public const int StripVertices = 512;
    public const int StripPrimitives = 510;

    /// <summary>The sheet's rows: each element's V, a row being an eighth of the sheet.</summary>
    public static readonly IReadOnlyList<(BeamRows Row, float V)> Rows =
    [
        (BeamRows.Water, 0), (BeamRows.Cold, 0.125f), (BeamRows.Fire, 0.25f), (BeamRows.Lightning, 0.375f),
        (BeamRows.Steam, 0.75f), (BeamRows.Poison, 0.875f),
    ];

    /// <summary>The Arcane row, which the shader's strip samples (its texture coordinates name it).</summary>
    public const float ArcaneRow = 0.5f;

    private static readonly VertexDeclaration StripLayout = new(
        new VertexElement(0, VertexElementFormat.Single, VertexElementUsage.Position, 0),
        new VertexElement(4, VertexElementFormat.Single, VertexElementUsage.Normal, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    /// <summary>The arcane beam's blend: its effect adds what it draws (source alpha, which it writes 1, and one).</summary>
    private static readonly BlendState Adding = new()
    {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.SourceAlpha, AlphaDestinationBlend = Blend.One,
    };

    private readonly GameEffect arcane;
    private readonly GameEffect additive;
    private readonly Texture2D sheet;
    private readonly VertexBuffer strip;
    private readonly VertexBuffer quad;

    public BeamDrawing(GraphicsDevice device, GameEffect arcane, GameEffect additive, Texture2D sheet)
    {
        ArgumentNullException.ThrowIfNull(device);
        (this.arcane, this.additive, this.sheet) = (arcane ?? throw new ArgumentNullException(nameof(arcane)),
            additive ?? throw new ArgumentNullException(nameof(additive)), sheet ?? throw new ArgumentNullException(nameof(sheet)));
        if (arcane.Effect.Techniques["Beam"] is null)
        {
            throw new InvalidDataException("ArcaneEffect has no Beam technique");
        }
        EffectChecks.Require(arcane, ["Origin", "Direction", "Length", "ColorCenter", "ColorEdge", "Cut", "Alpha", "StartLength", "Dropoff", "Texture",
            "TextureScale", "Time", "Clockwice", "WaveScale", "MinRadius", "MaxRadius", "RayRadius"]);
        EffectChecks.Require(additive, ["World", "TextureScale", "TextureOffset", "ColorTint", "VertexColorEnabled", "TextureEnabled", "Texture"]);
        strip = new VertexBuffer(device, StripLayout, StripVertices, BufferUsage.WriteOnly);
        try
        {
            strip.SetData(Strip());
            quad = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
            quad.SetData(Quad());
        }
        catch
        {
            strip.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The strip, four floats a vertex: how far along (the square of the pair's place, so the vertices crowd near
    /// the caster), the side (+1, −1), a number the shader skips, and the V of the Arcane row's top or bottom.
    /// </summary>
    public static float[] Strip()
    {
        var data = new float[StripVertices * 4];
        for (var pair = 0; pair < StripVertices / 2; pair++)
        {
            var along = MathF.Pow(pair / (StripVertices / 2f - 1), 2);
            for (var side = 0; side < 2; side++)
            {
                var at = (pair * 2 + side) * 4;
                (data[at], data[at + 1], data[at + 2], data[at + 3]) = (along, side == 0 ? 1 : -1, pair / 8f, side == 0 ? ArcaneRow : ArcaneRow + 0.125f);
            }
        }
        return data;
    }

    /// <summary>
    /// The quad a row is drawn on, as a strip: across from −1 to 1, along from 0 to 1, over one row of the sheet;
    /// white, as AdditiveEffect reads a colour (its draws need every input it reads).
    /// </summary>
    public static VertexPositionColorTexture[] Quad() =>
    [
        new(new Vector3(-1, 1, 0), Color.White, new Vector2(1, 0)), new(new Vector3(1, 1, 0), Color.White, new Vector2(1, 0.125f)),
        new(new Vector3(-1, 0, 0), Color.White, new Vector2(0, 0)), new(new Vector3(1, 0, 0), Color.White, new Vector2(0, 0.125f)),
    ];

    /// <summary>One helix's values: the k of the original's 0.5, 0.75, … and its widths.</summary>
    public readonly record struct Ring(float Time, bool Clockwise, float WaveScale, float MinRadius, float MaxRadius, float RayRadius);

    /// <summary>The helices of a beam at a time, the original's: each wider and slower than the one before, turning the other way.</summary>
    public static IEnumerable<Ring> Rings(BeamPicture beam)
    {
        var k = 0.5f;
        for (var i = 0; i < beam.Rings; i++, k += 0.25f)
        {
            var q = MathF.Sqrt(k);
            yield return new Ring((beam.Time + k * 10) * q, i % 2 == 0, 4 * q, 0.15f * q, (beam.Branch ? 0.15f : 1.25f) * q, 0.1f / q);
        }
    }

    /// <summary>
    /// Where a row's quad is drawn: turned about the beam to face the camera, from the beam's start, as long as the
    /// beam along its up and 2 wide.
    /// </summary>
    public static Matrix RowPlace(BeamPicture beam, Vector3 camera, Vector3 cameraForward)
    {
        var place = Matrix.CreateConstrainedBillboard(beam.Origin, camera, beam.Direction, cameraForward, null);
        (place.M21, place.M22, place.M23) = (place.M21 * beam.Length, place.M22 * beam.Length, place.M23 * beam.Length);
        return place;
    }

    public void Draw(GraphicsDevice device, IReadOnlyList<BeamPicture> beams, GlobalEffectValues globals)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(beams);
        ArgumentNullException.ThrowIfNull(globals);
        if (beams.Count == 0)
        {
            return;
        }
        device.DepthStencilState = DepthStencilState.DepthRead;
        device.RasterizerState = RasterizerState.CullNone;
        var forward = Matrix.Invert(globals.View).Forward;
        foreach (var beam in beams)
        {
            if (beam.Rows == BeamRows.None)
            {
                DrawArcane(device, beam);
            }
            else
            {
                DrawRows(device, beam, globals.CameraPosition, forward);
            }
        }
    }

    private void DrawArcane(GraphicsDevice device, BeamPicture beam)
    {
        device.BlendState = Adding;
        device.SetVertexBuffer(strip);
        arcane.Effect.CurrentTechnique = arcane.Effect.Techniques["Beam"];
        var parameters = new FnaEffectParameters(arcane.Effect);
        parameters.SetValue("Origin", beam.Origin);
        parameters.SetValue("Direction", beam.Direction);
        parameters.SetValue("Length", beam.Length);
        parameters.SetValue("ColorCenter", beam.Colour * 0.666f);
        parameters.SetValue("ColorEdge", beam.Colour * 0.333f);
        parameters.SetValue("Cut", 0f);
        parameters.SetValue("Alpha", 1f);
        parameters.SetValue("StartLength", 4f);
        parameters.SetValue("Dropoff", 0.666f);
        parameters.SetValue("Texture", sheet);
        parameters.SetValue("TextureScale", 0.1f);
        DrawStrip(device, parameters, new Ring(beam.Time, true, 0, 0, 0, 0.333f));                // the core
        foreach (var ring in Rings(beam))
        {
            DrawStrip(device, parameters, ring);
        }
    }

    private void DrawStrip(GraphicsDevice device, FnaEffectParameters parameters, Ring ring)
    {
        parameters.SetValue("Time", ring.Time);
        parameters.SetValue("Clockwice", ring.Clockwise);                                // the shader's spelling
        parameters.SetValue("WaveScale", ring.WaveScale);
        parameters.SetValue("MinRadius", ring.MinRadius);
        parameters.SetValue("MaxRadius", ring.MaxRadius);
        parameters.SetValue("RayRadius", ring.RayRadius);
        foreach (var pass in arcane.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, StripPrimitives);
        }
    }

    /// <summary>Each element's row twice: as it is, then mirrored across, narrower and drifting faster; Lightning tinted its colour.</summary>
    private void DrawRows(GraphicsDevice device, BeamPicture beam, Vector3 camera, Vector3 forward)
    {
        device.BlendState = DeferredRenderer.AdditiveBlend;
        device.SetVertexBuffer(quad);
        var place = RowPlace(beam, camera, forward);
        var narrow = place;
        (narrow.M11, narrow.M12, narrow.M13) = (place.M11 * 0.666f, place.M12 * 0.666f, place.M13 * 0.666f);
        var parameters = new FnaEffectParameters(additive.Effect);
        parameters.SetValue("VertexColorEnabled", false);
        parameters.SetValue("TextureEnabled", true);
        parameters.SetValue("Texture", sheet);
        foreach (var (row, v) in Rows.Where(r => (beam.Rows & r.Row) != 0))
        {
            parameters.SetValue("ColorTint", row == BeamRows.Lightning ? new Vector4(1.5f, 1, 2, 1) : new Vector4(2, 2, 2, 1));
            var drift = beam.Time * -0.05f;
            DrawQuad(device, parameters, place, new Vector2(beam.Length * 0.0333f, 1), new Vector2(drift, v));
            DrawQuad(device, parameters, narrow, new Vector2(beam.Length * 0.0333f, -1), new Vector2(drift * 1.3256675f + 0.3546517f, v + 0.125f));
        }
    }

    private void DrawQuad(GraphicsDevice device, FnaEffectParameters parameters, Matrix world, Vector2 scale, Vector2 offset)
    {
        parameters.SetValue("World", world);
        parameters.SetValue("TextureScale", scale);
        parameters.SetValue("TextureOffset", offset);
        foreach (var pass in additive.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }
    }

    public void Dispose()
    {
        strip.Dispose();
        quad.Dispose();
    }
}
