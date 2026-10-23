namespace OpenMagicka.Game;

/// <summary>
/// Which FNA3D driver to run when the user has not chosen one (FNA3D_FORCE_DRIVER). The original
/// effects are translated by MojoShader for each driver, and two of its translations get some of
/// them wrong (OM-63): for SDL_GPU (Vulkan, FNA3D's first pick) the additive light shafts come out
/// in the wrong colours, and for D3D11 the characters' effect does not compile. Its OpenGL
/// translation draws them all right, so OpenGL is the default.
/// </summary>
public static class GraphicsDriver
{
    public const string Variable = "FNA3D_FORCE_DRIVER";

    public const string Preferred = "OpenGL";

    /// <summary>The driver to set, or null to leave the user's choice.</summary>
    public static string? Default(string? requested) => string.IsNullOrWhiteSpace(requested) ? Preferred : null;
}
