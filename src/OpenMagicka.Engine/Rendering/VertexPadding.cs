using OpenMagicka.Content.Effects;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.XnAnimation;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The vertex elements the original effects' vertex shaders read (every technique together), as
/// their bytecode declares them (<see cref="ShaderBytecode.VertexInputs"/>; a test on the game's
/// effects pins these lists).
/// </summary>
public static class EffectInputs
{
    private static ShaderInput In(VertexElementUsageXna31 usage, int index = 0) => new(usage, index);

    public static IReadOnlyList<ShaderInput> RenderDeferred { get; } =
    [
        In(VertexElementUsageXna31.Position), In(VertexElementUsageXna31.Normal), In(VertexElementUsageXna31.TextureCoordinate),
        In(VertexElementUsageXna31.Tangent), In(VertexElementUsageXna31.TextureCoordinate, 1), In(VertexElementUsageXna31.Tangent, 1),
        In(VertexElementUsageXna31.Color),
    ];

    public static IReadOnlyList<ShaderInput> Additive { get; } =
        [In(VertexElementUsageXna31.Position), In(VertexElementUsageXna31.Color), In(VertexElementUsageXna31.TextureCoordinate)];

    public static IReadOnlyList<ShaderInput> SkinnedModelDeferred { get; } =
    [
        In(VertexElementUsageXna31.Position), In(VertexElementUsageXna31.Normal), In(VertexElementUsageXna31.TextureCoordinate),
        In(VertexElementUsageXna31.BlendIndices), In(VertexElementUsageXna31.BlendWeight),
    ];

    /// <summary>Water and lava (the same inputs); Color1 is the freeze value (see <see cref="LevelLiquids"/>).</summary>
    public static IReadOnlyList<ShaderInput> Liquid { get; } =
    [
        In(VertexElementUsageXna31.Position), In(VertexElementUsageXna31.Normal), In(VertexElementUsageXna31.Tangent),
        In(VertexElementUsageXna31.Binormal), In(VertexElementUsageXna31.TextureCoordinate), In(VertexElementUsageXna31.Color),
        In(VertexElementUsageXna31.Color, 1),
    ];

    /// <summary>A liquid's own vertices: what its effects read but the freeze value, which comes as a second stream (OM-88).</summary>
    public static IReadOnlyList<ShaderInput> LiquidVertices { get; } = [.. Liquid.Where(i => i != In(VertexElementUsageXna31.Color, 1))];

    /// <summary>Every input the vertex shaders of an fx_2_0 effect read, all techniques together, without repeats.</summary>
    public static IReadOnlyList<ShaderInput> Read(byte[] effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return Fx2Effect.Parse(effect).Resources
            .Where(r => ShaderBytecode.IsVertexShader(r.Code))
            .SelectMany(r => ShaderBytecode.VertexInputs(r.Code))
            .Distinct()
            .ToArray();
    }

    /// <summary>The inputs of the effect a batch is drawn with: additive, a character's, a liquid's, or RenderDeferred (its own material or a stand-in).</summary>
    public static IReadOnlyList<ShaderInput> For(LevelBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.Additive ? Additive
            : batch.Material switch
            {
                SkinnedModelBasicEffectContent => SkinnedModelDeferred,
                RenderDeferredLiquidEffectContent or LavaEffectContent => batch.Liquid >= 0 ? LiquidVertices : Liquid,
                _ => RenderDeferred,
            };
    }
}

/// <summary>
/// Completes vertices with the elements an effect reads but a mesh lacks. Direct3D 9 let a shader
/// read a missing element; under FNA, D3D11 refuses to build the input layout and Vulkan reads an
/// unbound attribute. Each missing element is appended to every vertex with a neutral value: white
/// for a colour, up for a normal, +X for a tangent or binormal, a full first weight for blend
/// weights, zero otherwise.
/// </summary>
public static class VertexPadding
{
    public static LevelBatch Complete(LevelBatch batch, IReadOnlyList<ShaderInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(inputs);
        var present = batch.Declaration.Elements.Select(e => new ShaderInput(e.Usage, e.UsageIndex)).ToHashSet();
        var missing = inputs.Where(i => !present.Contains(i)).Distinct().ToArray();
        if (missing.Length == 0)
        {
            return batch;
        }
        return AppendAll(batch, [.. missing.Select(input => (input, Default(input.Usage)))]);
    }

    /// <summary>One vertex with each of the inputs (once) at its neutral value, for a test draw.</summary>
    public static (VertexDeclarationContent Declaration, byte[] Vertex) Probe(IReadOnlyList<ShaderInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var elements = new List<VertexElementContent>();
        var vertex = new List<byte>();
        foreach (var input in inputs.Distinct())
        {
            var (format, value) = Default(input.Usage);
            elements.Add(new VertexElementContent(0, vertex.Count, format, 0, input.Usage, input.Index));
            vertex.AddRange(value);
        }
        return (new VertexDeclarationContent(elements), vertex.ToArray());
    }

    /// <summary>Appends one element with the given value to every vertex.</summary>
    public static LevelBatch Append(LevelBatch batch, ShaderInput input, VertexElementFormatXna31 format, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(value);
        return AppendAll(batch, [(input, (format, value))]);
    }

    private static LevelBatch AppendAll(LevelBatch batch, IReadOnlyList<(ShaderInput Input, (VertexElementFormatXna31 Format, byte[] Value) Element)> elements)
    {
        if (batch.Stride < batch.Declaration.Stride)
        {
            throw new InvalidDataException($"vertex stride {batch.Stride} is shorter than its declaration ({batch.Declaration.Stride} bytes)");
        }
        var added = new List<VertexElementContent>();
        var values = new List<byte>();
        foreach (var (input, (format, value)) in elements)
        {
            added.Add(new VertexElementContent(0, batch.Stride + values.Count, format, 0, input.Usage, input.Index));
            values.AddRange(value);
        }
        var stride = batch.Stride + values.Count;
        var data = new byte[batch.VertexCount * stride];
        var tail = values.ToArray();
        for (var v = 0; v < batch.VertexCount; v++)
        {
            batch.VertexData.AsSpan(v * batch.Stride, batch.Stride).CopyTo(data.AsSpan(v * stride));
            tail.CopyTo(data.AsSpan(v * stride + batch.Stride));
        }
        return batch with
        {
            VertexData = data,
            Stride = stride,
            Declaration = new VertexDeclarationContent([.. batch.Declaration.Elements, .. added]),
        };
    }

    private static (VertexElementFormatXna31 Format, byte[] Value) Default(VertexElementUsageXna31 usage) => usage switch
    {
        VertexElementUsageXna31.Color => (VertexElementFormatXna31.Color, [255, 255, 255, 255]),
        VertexElementUsageXna31.Normal => (VertexElementFormatXna31.Vector3, Floats(0, 1, 0)),
        VertexElementUsageXna31.Tangent or VertexElementUsageXna31.Binormal => (VertexElementFormatXna31.Vector3, Floats(1, 0, 0)),
        VertexElementUsageXna31.BlendWeight => (VertexElementFormatXna31.Vector4, Floats(1, 0, 0, 0)),
        VertexElementUsageXna31.BlendIndices => (VertexElementFormatXna31.Byte4, [0, 0, 0, 0]),
        _ => (VertexElementFormatXna31.Vector4, Floats(0, 0, 0, 0)),
    };

    private static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
}
