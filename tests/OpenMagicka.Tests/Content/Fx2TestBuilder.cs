namespace OpenMagicka.Tests.Content;

/// <summary>
/// Builds minimal DX9 fx_2_0 effect binaries: one sampler parameter, one technique with one
/// pass, and shader resources bound to pass states. Layout as in tools/research/fx_states.py.
/// </summary>
internal sealed class Fx2TestBuilder
{
    public const int AlphaTestEnable = 4;
    public const int ZEnable = 0;
    public const int ZWriteEnable = 3;
    public const int VertexShader = 146;
    public const int PixelShader = 147;
    public const int AddressU = 165;
    public const int AddressV = 166;
    public const int BorderColor = 168;

    private readonly List<(int Op, uint Value)> passStates = [];
    private readonly List<(int Op, uint Value)> samplerStates = [];
    private readonly List<(int State, byte[] Code)> shaders = [];

    public Fx2TestBuilder State(int op, uint value)
    {
        passStates.Add((op, value));
        return this;
    }

    public Fx2TestBuilder Sampler(int op, uint value)
    {
        samplerStates.Add((op, value));
        return this;
    }

    /// <summary>Binds shader code to the pass state with the given index (in insertion order).</summary>
    public Fx2TestBuilder Shader(int stateIndex, byte[] code)
    {
        shaders.Add((stateIndex, code));
        return this;
    }

    public byte[] Build()
    {
        var blob = new List<byte>();
        int Add(params uint[] words)
        {
            var at = blob.Count;
            foreach (var w in words)
            {
                blob.AddRange(BitConverter.GetBytes(w));
            }
            return at;
        }
        int String(string s)
        {
            var at = Add((uint)s.Length + 1);
            blob.AddRange(System.Text.Encoding.ASCII.GetBytes(s + "\0"));
            while (blob.Count % 4 != 0)
            {
                blob.Add(0);
            }
            return at;
        }

        var dwordType = Add(1, 0, 0, 0);                 // a scalar typedef for state values
        var samplerName = String("Sampler0");
        var samplerType = Add(10, 4, (uint)samplerName, 0);
        var samplerValue = Add((uint)samplerStates.Count);
        var samplerSlots = new List<int>();
        foreach (var _ in samplerStates)
        {
            samplerSlots.Add(Add(0, 0, (uint)dwordType, 0));
        }
        for (var i = 0; i < samplerStates.Count; i++)
        {
            var valueAt = Add(samplerStates[i].Value);
            WriteAt(blob, samplerSlots[i], (uint)samplerStates[i].Op);
            WriteAt(blob, samplerSlots[i] + 12, (uint)valueAt);
        }
        var techniqueName = String("Technique1");
        var passName = String("Pass1");
        var passValues = passStates.Select(s => Add(s.Value)).ToList();

        var structure = new List<byte>();
        void S(params uint[] words)
        {
            foreach (var w in words)
            {
                structure.AddRange(BitConverter.GetBytes(w));
            }
        }
        S(1, 1, 0, 0);                                    // parameters, techniques, unused, objects
        S((uint)samplerType, (uint)samplerValue, 0, 0);   // sampler parameter, no annotations
        S((uint)techniqueName, 0, 1);                     // technique: name, annotations, passes
        S((uint)passName, 0, (uint)passStates.Count);     // pass: name, annotations, states
        for (var i = 0; i < passStates.Count; i++)
        {
            S((uint)passStates[i].Op, 0, (uint)dwordType, (uint)passValues[i]);
        }
        S(0, (uint)shaders.Count);                        // no strings, resources
        foreach (var (state, code) in shaders)
        {
            S(0, 0, 0, (uint)state, 0, (uint)code.Length);
            structure.AddRange(code);
            while (structure.Count % 4 != 0)
            {
                structure.Add(0);
            }
        }

        var file = new List<byte>();
        file.AddRange(BitConverter.GetBytes(0xFEFF0901u));
        file.AddRange(BitConverter.GetBytes((uint)blob.Count));
        file.AddRange(blob);
        file.AddRange(structure);
        return [.. file];
    }

    /// <summary>
    /// A vs_3_0 shader whose preshader computes DOT x3 into temp 7 (needs 10 temps, MojoShader
    /// allocates 8) followed by a MOV of temp 7 to output 0.
    /// </summary>
    public static byte[] ShaderWithOverflowingPreshader() => ShaderWithPreshader(
    [
        0x50000003, 2, 0, 2, 0, 0, 2, 0, 0, 7, 7,         // DOT x3, 2 sources: input 0, input 0 -> temp 7
        0x10000001, 1, 0, 7, 7, 0, 4, 0,                  // MOV x1, 1 source: temp 7 -> output 0
    ], instructions: 2);

    /// <summary>A shader whose preshader only moves an input to an output.</summary>
    public static byte[] ShaderWithHarmlessPreshader() => ShaderWithPreshader(
    [
        0x10000001, 1, 0, 2, 0, 0, 4, 0,                  // MOV x1, 1 source: input 0 -> output 0
    ], instructions: 1);

    private static byte[] ShaderWithPreshader(uint[] instructionTokens, int instructions)
    {
        var fxlc = new List<uint> { 0x434C5846, (uint)instructions };   // "FXLC", count
        fxlc.AddRange(instructionTokens);
        var pres = new List<uint> { 0x53455250, 0x46580201 };           // "PRES", preshader version
        pres.Add(((uint)fxlc.Count << 16) | 0xFFFE);
        pres.AddRange(fxlc);
        pres.Add(0x0000FFFF);                                           // end of preshader
        var code = new List<uint> { 0xFFFE0300 };                       // vs_3_0
        code.Add(((uint)pres.Count << 16) | 0xFFFE);
        code.AddRange(pres);
        code.Add(0x0000FFFF);                                           // end of shader
        return code.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static void WriteAt(List<byte> blob, int at, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 4; i++)
        {
            blob[at + i] = bytes[i];
        }
    }
}
