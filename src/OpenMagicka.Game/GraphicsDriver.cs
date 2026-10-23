using System.Runtime.InteropServices;

namespace OpenMagicka.Game;

/// <summary>
/// Which FNA3D driver to run when the user has not chosen one (FNA3D_FORCE_DRIVER). The original
/// effects are translated by MojoShader for each driver, and two of its translations get some of
/// them wrong in the stock FNA3D (OM-63): for SDL_GPU (Vulkan, FNA3D's first pick) the additive
/// light shafts come out in the wrong colours, and for D3D11 the characters' effect does not
/// compile. Our own FNA3D build (native/README.md) fixes both, so with it FNA3D picks as it likes;
/// with the stock one the OpenGL translation, which draws them all right, is the default.
/// </summary>
public static class GraphicsDriver
{
    public const string Variable = "FNA3D_FORCE_DRIVER";

    public const string Preferred = "OpenGL";

    /// <summary>The driver to set, or null to leave the choice to the user or to FNA3D.</summary>
    public static string? Default(string? requested, bool ownFna3d) =>
        string.IsNullOrWhiteSpace(requested) && !ownFna3d ? Preferred : null;

    /// <summary>Whether the launcher ships our FNA3D for this platform (only Windows x64 so far).</summary>
    public static bool HasOwnFna3d(bool isWindows, Architecture architecture) => isWindows && architecture == Architecture.X64;

    /// <summary><see cref="HasOwnFna3d(bool, Architecture)"/> for the running process.</summary>
    public static bool HasOwnFna3d() => HasOwnFna3d(OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture);
}
