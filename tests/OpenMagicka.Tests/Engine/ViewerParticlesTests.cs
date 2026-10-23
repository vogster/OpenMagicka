using Microsoft.Xna.Framework;
using OpenMagicka.Content;
using OpenMagicka.Content.Particles;
using OpenMagicka.Game.Viewer;
using static OpenMagicka.Tests.Particles.VisualEffectReaderTests;

namespace OpenMagicka.Tests.Engine;

public sealed class ViewerParticlesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omvp-" + Guid.NewGuid().ToString("N"));

    public ViewerParticlesTests()
    {
        var path = Path.Combine(root, "Effects", "torch_fire.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Effect(Continuous("<ParticlesPerSecond value=\"20\"/><LifeTimeMin value=\"5\"/><LifeTimeMax value=\"5\"/>")));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static OpenMagicka.Engine.Particles.PlacedEffect At(string effect, float x) => new(effect, Matrix.CreateTranslation(x, 0, 0));

    [Fact]
    public void ALevelsEffects_StartWarmedUp_AndUnknownOnesAreReported()
    {
        var particles = new ViewerParticles(Task.FromResult(EffectLibrary.Load(new ContentFileSystem(root))));

        var started = particles.Start([At("torch_fire", 0), At("no_such_effect", 5)]);

        Assert.True(started);
        Assert.Equal(["no_such_effect"], particles.Missing);
        Assert.InRange(particles.Pool.LastActive + 1, 30, 41);   // 2 s of warm-up at 20 per second
    }

    [Fact]
    public void EffectsWaitForTheLibrary_AndAFailedLibraryStartsNothing()
    {
        var pending = new TaskCompletionSource<EffectLibrary>();
        var waiting = new ViewerParticles(pending.Task);
        var failed = new ViewerParticles(Task.FromException<EffectLibrary>(new IOException("no Effects folder")));

        Assert.False(waiting.Start([At("torch_fire", 0)]));
        Assert.True(failed.Start([At("torch_fire", 0)]));
        Assert.Equal(-1, failed.Pool.LastActive);
    }

    [Fact]
    public void AStep_IsCut_SoAPauseDoesNotSpawnABurst()
    {
        var particles = new ViewerParticles(Task.FromResult(EffectLibrary.Load(new ContentFileSystem(root))));
        particles.Start([At("torch_fire", 0)]);
        var before = particles.Pool.LastActive;

        particles.Step(10);

        Assert.InRange(particles.Pool.LastActive - before, 1, 3);   // 0.1 s at 20 per second, not 10 s
    }
}
