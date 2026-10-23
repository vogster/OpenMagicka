using System.Security.Cryptography;
using OpenMagicka.Game;

namespace OpenMagicka.Tests.Engine;

public sealed class GraphicsDriverTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("omdriver-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

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

    private void Ship(byte[] dll, string recordedHash)
    {
        File.WriteAllBytes(Path.Combine(dir, "FNA3D.dll"), dll);
        File.WriteAllLines(Path.Combine(dir, GraphicsDriver.OwnFna3dRecord),
            ["OpenMagicka build of FNA3D (altered source: see native/README.md).", "FNA3D abc", $"FNA3D.dll sha256 {recordedHash}"]);
    }

    [Fact]
    public void OurFna3d_IsRecognisedByItsRecordedHash()
    {
        byte[] dll = [1, 2, 3];
        Ship(dll, Convert.ToHexStringLower(SHA256.HashData(dll)));

        Assert.True(GraphicsDriver.HasOwnFna3d(dir));
    }

    [Fact]
    public void AnotherFna3d_OrNoRecord_IsTheStockOne()
    {
        Assert.False(GraphicsDriver.HasOwnFna3d(dir));                      // nothing there

        Ship([1, 2, 3], Convert.ToHexStringLower(SHA256.HashData([9, 9])));
        Assert.False(GraphicsDriver.HasOwnFna3d(dir));                      // a different DLL

        File.Delete(Path.Combine(dir, "FNA3D.dll"));
        Assert.False(GraphicsDriver.HasOwnFna3d(dir));                      // the record alone
    }
}
