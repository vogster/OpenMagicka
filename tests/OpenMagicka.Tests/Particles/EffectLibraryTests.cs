using OpenMagicka.Content;
using OpenMagicka.Content.Particles;
using static OpenMagicka.Tests.Particles.VisualEffectReaderTests;

namespace OpenMagicka.Tests.Particles;

public sealed class EffectLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omfx-" + Guid.NewGuid().ToString("N"));

    public EffectLibraryTests()
    {
        Write("Effects/Fire_Ball.xml", Effect(Continuous(""), "duration=\"1\""));
        Write("Effects/Spells/Beam/ice.XML", Effect("", "duration=\"2\""));
        Write("Effects/readme.txt", "not an effect");
        Write("Models/other.xml", Effect("", "duration=\"3\""));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void Write(string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    [Fact]
    public void Load_ReadsEveryXmlUnderEffects_KeyedByTheHashOfTheLowerCaseName()
    {
        var library = EffectLibrary.Load(new ContentFileSystem(root));

        Assert.Equal(2, library.Count);
        Assert.Equal(1f, library.Get(LegacyStringHash.Compute("fire_ball")).Duration);
        Assert.True(library.TryGet("ICE", out var ice));
        Assert.Equal(2f, ice.Duration);
        Assert.False(library.TryGet("other", out _));
        Assert.Throws<KeyNotFoundException>(() => library.Get(LegacyStringHash.Compute("missing")));
    }

    [Fact]
    public void Load_RejectsTwoEffectsWithTheSameName_LikeTheOriginal()
    {
        Write("Effects/Other/FIRE_BALL.xml", Effect(""));

        var error = Assert.Throws<InvalidDataException>(() => EffectLibrary.Load(new ContentFileSystem(root)));

        Assert.Contains("Effects/Other/FIRE_BALL.xml", error.Message);
        Assert.Contains("Effects/Fire_Ball.xml", error.Message);
    }

    [Fact]
    public void Load_OfABrokenEffect_NamesTheFile()
    {
        Write("Effects/Spells/broken.xml", "<Effect type=\"Never\"/>");

        var error = Assert.Throws<InvalidDataException>(() => EffectLibrary.Load(new ContentFileSystem(root)));

        Assert.Contains("Effects/Spells/broken.xml", error.Message);
    }

    [Fact]
    public void Load_WithoutAnEffectsDirectory_IsEmpty()
    {
        Directory.Delete(Path.Combine(root, "Effects"), recursive: true);

        Assert.Equal(0, EffectLibrary.Load(new ContentFileSystem(root)).Count);
    }
}
