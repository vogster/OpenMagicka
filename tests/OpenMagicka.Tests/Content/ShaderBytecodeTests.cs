using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Tests.Content;

public class ShaderBytecodeTests
{
    private const uint Dcl = 31;
    private const uint End = 0x0000FFFF;

    /// <summary>A dcl instruction (SM2+: its length, 2 tokens, sits in bits 24-27).</summary>
    private static uint[] Declare(uint usage, uint index, uint registerType, uint register) =>
    [
        Dcl | 2u << 24,
        0x80000000 | usage | index << 16,
        0x80000000 | (registerType & 7) << 28 | (registerType & 0x18) << 8 | 0x000F0000 | register,
    ];

    private static byte[] Shader(uint version, params uint[][] instructions)
    {
        uint[] comment = [0xFFFE | 2u << 16, 0x42415443, 0];   // a 2-token comment (a constant table, say)
        uint[] mov = [1u | 2u << 24, 0x800F0000, 0x90E40000];   // mov r0, v0
        uint[] tokens = [version, .. comment, .. instructions.SelectMany(i => i), .. mov, End];
        return tokens.SelectMany(BitConverter.GetBytes).ToArray();
    }

    [Fact]
    public void AVertexShadersInputs_AreItsInputDeclarations()
    {
        var code = Shader(0xFFFE0300,
            Declare(0, 0, 1, 0),        // dcl_position v0
            Declare(10, 0, 1, 1),       // dcl_color v1
            Declare(5, 1, 1, 2),        // dcl_texcoord1 v2
            Declare(5, 0, 6, 1));       // dcl_texcoord o1 (an output: not an input)

        Assert.True(ShaderBytecode.IsVertexShader(code));
        Assert.Equal(
            [new ShaderInput(VertexElementUsageXna31.Position, 0), new ShaderInput(VertexElementUsageXna31.Color, 0),
                new ShaderInput(VertexElementUsageXna31.TextureCoordinate, 1)],
            ShaderBytecode.VertexInputs(code));
    }

    [Fact]
    public void APixelShader_IsNotAVertexShader()
    {
        var code = Shader(0xFFFF0300, Declare(10, 0, 1, 0));

        Assert.False(ShaderBytecode.IsVertexShader(code));
        Assert.Empty(ShaderBytecode.VertexInputs(code));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00, 0x03, 0xFE })]
    public void TooShortCode_IsNotAShader(byte[] code)
    {
        Assert.False(ShaderBytecode.IsVertexShader(code));
    }

    [Fact]
    public void BrokenCode_IsRejected()
    {
        uint[] tokens = [0xFFFE0300, Dcl | 2u << 24, 0x80000000];   // the instruction runs past the end
        var code = tokens.SelectMany(BitConverter.GetBytes).ToArray();

        Assert.Throws<InvalidDataException>(() => ShaderBytecode.VertexInputs(code));
    }

    [Fact]
    public void Shader1x_IsRejected_ForItHasNoInstructionLengths()
    {
        var code = Shader(0xFFFE0101);

        Assert.Throws<InvalidDataException>(() => ShaderBytecode.VertexInputs(code));
    }
}
