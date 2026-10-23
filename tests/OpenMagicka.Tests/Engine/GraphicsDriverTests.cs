using OpenMagicka.Game;

namespace OpenMagicka.Tests.Engine;

public class GraphicsDriverTests
{
    [Theory]
    [InlineData(null, "OpenGL")]
    [InlineData("", "OpenGL")]
    [InlineData(" ", "OpenGL")]
    [InlineData("Vulkan", null)]     // the user's choice stays
    [InlineData("D3D11", null)]
    public void WithoutAChoice_TheDriverThatDrawsTheOriginalEffectsRightIsUsed(string? requested, string? expected)
    {
        Assert.Equal(expected, GraphicsDriver.Default(requested));
    }
}
