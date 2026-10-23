using System.Buffers.Binary;
using System.Collections.Frozen;

namespace OpenMagicka.Content.Effects;

public sealed record EffectPatchResult(byte[] Bytecode, int RemovedStates, int ChangedSamplerStates, int WidenedPreshaders);

/// <summary>
/// Makes Magicka's original fx_2_0 effects loadable by FNA without changing FNA
/// (docs/engine/RISK_CHECKS.md §1):
/// <list type="bullet">
/// <item>removes pass render states XNA 4.0 does not have (alpha test, point scale, ...) and
/// renumbers the shader resources bound to the remaining states;</item>
/// <item>turns Border / MirrorOnce sampler addressing into Clamp, and the border colour entry
/// into AddressU = Clamp (the only sampler using it, BuffEffect, already clamps U);</item>
/// <item>widens preshaders whose temps MojoShader would under-allocate.</item>
/// </list>
/// </summary>
public static class EffectPatcher
{
    public const int SamplerAddressU = 165;
    public const int SamplerAddressW = 167;
    public const int SamplerBorderColor = 168;
    public const uint AddressClamp = 3;
    public const uint AddressBorder = 4;
    public const uint AddressMirrorOnce = 5;

    private const int RenderStateCount = 103;
    private const int StateEntrySize = 16;
    private const int ParameterBinding = -1;

    /// <summary>Render states (D3DRENDERSTATETYPE order) that FNA's Effect.Apply handles.</summary>
    private static readonly FrozenSet<int> FnaRenderStates = new[]
    {
        0, 1, 3, 6, 7, 8, 9, 13, 22, 23, 24, 25, 26, 27, 28, 29, 67, 68, 73, 75, 78, 79,
        88, 89, 90, 91, 92, 93, 94, 95, 96, 98, 99, 100, 101, 102,
    }.ToFrozenSet();

    public static bool IsUnsupportedRenderState(int operation) =>
        operation < RenderStateCount && !FnaRenderStates.Contains(operation);

    /// <summary>A sampler state the patcher rewrites: Border / MirrorOnce addressing or a border colour.</summary>
    public static bool IsUnsupportedSamplerState(int operation, uint value) =>
        operation == SamplerBorderColor
        || operation is >= SamplerAddressU and <= SamplerAddressW && value is AddressBorder or AddressMirrorOnce;

    public static EffectPatchResult Patch(byte[] effect)
    {
        var parsed = Fx2Effect.Parse(effect);
        var blob = effect.AsSpan(Fx2Effect.HeaderSize).ToArray();

        var cuts = new List<int>();
        var remap = new Dictionary<(int, int, int), int>();
        foreach (var pass in parsed.Passes)
        {
            var kept = 0;
            for (var s = 0; s < pass.States.Count; s++)
            {
                if (IsUnsupportedRenderState(pass.States[s].Operation))
                {
                    cuts.Add(pass.States[s].At);
                }
                else
                {
                    remap[(pass.Technique, pass.Pass, s)] = kept++;
                }
            }
            Write(blob, pass.CountAt, (uint)kept);
        }

        var samplerChanges = 0;
        foreach (var state in parsed.SamplerStates)
        {
            if (state.Operation is >= SamplerAddressU and <= SamplerAddressW
                && state.Value is AddressBorder or AddressMirrorOnce)
            {
                Write(blob, state.ValueAt, AddressClamp);
                samplerChanges++;
            }
            else if (state.Operation == SamplerBorderColor)
            {
                Write(blob, state.At, SamplerAddressU);
                Write(blob, state.ValueAt, AddressClamp);
                samplerChanges++;
            }
        }

        var widened = 0;
        var tail = new List<byte>();
        foreach (var resource in parsed.Resources)
        {
            var state = resource.State;
            var code = resource.Code;
            if (resource.Technique != ParameterBinding)
            {
                if (!remap.TryGetValue((resource.Technique, resource.Pass, resource.State), out state))
                {
                    throw new InvalidDataException($"a shader is bound to a removed state {resource.State}");
                }
                (code, var n) = Preshader.Widen(code);
                widened += n;
            }
            AppendHeader(tail, resource, state, code.Length);
            tail.AddRange(code);
            tail.AddRange(new byte[Fx2Effect.Align(code.Length) - code.Length]);
        }

        if (cuts.Count == 0 && samplerChanges == 0 && widened == 0)
        {
            return new EffectPatchResult(effect, 0, 0, 0);
        }

        var tailStart = parsed.Resources.Count > 0 ? parsed.Resources[0].At : blob.Length;
        var result = new List<byte>(effect.Length + tail.Count);
        result.AddRange(effect.AsSpan(0, Fx2Effect.HeaderSize).ToArray());
        var copied = 0;
        foreach (var cut in cuts.Order())
        {
            result.AddRange(blob.AsSpan(copied, cut - copied).ToArray());
            copied = cut + StateEntrySize;
        }
        result.AddRange(blob.AsSpan(copied, tailStart - copied).ToArray());
        result.AddRange(tail);
        return new EffectPatchResult([.. result], cuts.Count, samplerChanges, widened);
    }

    private static void AppendHeader(List<byte> tail, Fx2Resource resource, int state, int size)
    {
        Span<byte> header = stackalloc byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(header, resource.Technique);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], resource.Pass);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], resource.Element);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], state);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], resource.Usage);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], size);
        tail.AddRange(header.ToArray());
    }

    private static void Write(byte[] blob, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(at), value);
}
