using System.Diagnostics;
using System.Security.Cryptography;

namespace OpenMagicka.Tests.Engine;

/// <summary>Our prebuilt FNA3D (native/README.md) must match the submodules and patches it records.</summary>
public class NativeForkTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenMagicka.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("the repository root was not found");
    }

    private static Dictionary<string, string> Recorded() =>
        File.ReadAllLines(Path.Combine(Root, "native", "win-x64", "SOURCE.txt"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("FNA3D ", StringComparison.Ordinal) || line.StartsWith("MojoShader", StringComparison.Ordinal)
                || line.EndsWith(".dll sha256", StringComparison.Ordinal) || line.Contains(" sha256 ", StringComparison.Ordinal))
            .Select(line => line.Split(' '))
            .ToDictionary(parts => parts[0], parts => parts[^1]);

    private static string Head(string submodule)
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", Path.Combine(Root, submodule), "rev-parse", "HEAD"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var head = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return head;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void TheBuild_WasMadeFromTheSubmodulesFnaPins()
    {
        var recorded = Recorded();

        Assert.Equal(Head("lib/FNA/lib/FNA3D"), recorded["FNA3D"]);
        Assert.Equal(Head("lib/FNA/lib/FNA3D/MojoShader"), recorded["MojoShader"]);
    }

    [Fact]
    public void TheBuild_HasEveryPatch_AsItIsNow_AndIsTheShippedDll()
    {
        var recorded = Recorded();
        var patches = Directory.GetFiles(Path.Combine(Root, "native", "patches", "MojoShader"), "*.patch");

        Assert.NotEmpty(patches);
        Assert.All(patches, patch => Assert.Equal(Sha256(patch), recorded["MojoShader/" + Path.GetFileName(patch)]));
        Assert.Equal(patches.Length, recorded.Keys.Count(k => k.StartsWith("MojoShader/", StringComparison.Ordinal)));
        Assert.Equal(Sha256(Path.Combine(Root, "native", "win-x64", "FNA3D.dll")), recorded["FNA3D.dll"]);
    }
}
