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
    public void WithTheStockFna3d_TheDriverThatDrawsTheOriginalEffectsRightIsUsed(string? requested, string? expected)
    {
        Assert.Equal(expected, GraphicsDriver.Default(requested, ownFna3d: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OpenGL")]
    public void WithOurFna3d_FnasOwnChoiceOrTheUsersStays(string? requested)
    {
        Assert.Null(GraphicsDriver.Default(requested, ownFna3d: true));
    }

    [Fact]
    public void OurFna3d_IsShippedForWindowsX64Only()
    {
        Assert.True(GraphicsDriver.HasOwnFna3d(isWindows: true, System.Runtime.InteropServices.Architecture.X64));
        Assert.False(GraphicsDriver.HasOwnFna3d(isWindows: true, System.Runtime.InteropServices.Architecture.Arm64));
        Assert.False(GraphicsDriver.HasOwnFna3d(isWindows: false, System.Runtime.InteropServices.Architecture.X64));
    }
}
