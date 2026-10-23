using System.Buffers.Binary;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Effects;

/// <summary>A vertex element a vertex shader reads (Direct3D 9 usages are numbered as XNA's).</summary>
public readonly record struct ShaderInput(VertexElementUsageXna31 Usage, int Index);

/// <summary>
/// Reads Direct3D 9 shader bytecode (shader model 2 and 3): a version token (0xFFFE major minor
/// for vertex shaders, 0xFFFF for pixel shaders), then instructions until the end token 0x0000FFFF.
/// An instruction token holds its opcode in the low 16 bits and, from shader model 2 on, its
/// length in tokens in bits 24-27; a comment (opcode 0xFFFE) holds its length in bits 16-30. A
/// declaration (opcode 31) is followed by a usage token (usage in bits 0-4, index in bits 16-19)
/// and the declared register (type in bits 28-30 and 11-12, input = 1).
/// </summary>
public static class ShaderBytecode
{
    private const uint VertexShaderTag = 0xFFFE;
    private const uint CommentOpcode = 0xFFFE;
    private const uint EndToken = 0x0000FFFF;
    private const uint DeclareOpcode = 31;
    private const uint InputRegister = 1;

    public static bool IsVertexShader(ReadOnlySpan<byte> code) =>
        code.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(code) >> 16 == VertexShaderTag;

    /// <summary>The vertex elements a vertex shader declares as inputs, in order; none for a pixel shader.</summary>
    public static IReadOnlyList<ShaderInput> VertexInputs(ReadOnlySpan<byte> code)
    {
        if (!IsVertexShader(code))
        {
            return [];
        }
        var tokens = Tokens(code);
        if ((tokens[0] >> 8 & 0xFF) < 2)
        {
            throw new InvalidDataException("shader model 1 bytecode has no instruction lengths");
        }
        var inputs = new List<ShaderInput>();
        for (var at = 1; at < tokens.Length;)
        {
            var token = tokens[at];
            if (token == EndToken)
            {
                return inputs;
            }
            var opcode = token & 0xFFFF;
            var length = opcode == CommentOpcode ? (int)(token >> 16 & 0x7FFF) : (int)(token >> 24 & 0xF);
            if (at + length >= tokens.Length)
            {
                throw new InvalidDataException($"shader instruction at token {at} runs past the end");
            }
            if (opcode == DeclareOpcode && length >= 2)
            {
                var usage = tokens[at + 1];
                var register = tokens[at + 2];
                if (((register >> 28 & 7) | (register >> 8 & 0x18)) == InputRegister)
                {
                    inputs.Add(new ShaderInput((VertexElementUsageXna31)(usage & 0x1F), (int)(usage >> 16 & 0xF)));
                }
            }
            at += 1 + length;
        }
        throw new InvalidDataException("shader bytecode has no end token");
    }

    private static uint[] Tokens(ReadOnlySpan<byte> code)
    {
        var tokens = new uint[code.Length / 4];
        for (var i = 0; i < tokens.Length; i++)
        {
            tokens[i] = BinaryPrimitives.ReadUInt32LittleEndian(code[(i * 4)..]);
        }
        return tokens;
    }
}
