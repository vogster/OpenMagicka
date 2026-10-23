using Microsoft.Xna.Framework;
using OpenMagicka.Content;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Tests.Content;
using static OpenMagicka.Tests.Content.Fx2TestBuilder;

namespace OpenMagicka.Tests.Engine;

public sealed class ShaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omfx2-" + Guid.NewGuid().ToString("N"));

    public ShaderTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Shaders", "Maya"));
        Write("Shaders/TestEffect.xnb", new Fx2TestBuilder().Matrix("View", 1).Matrix("Time", 0).State(AlphaTestEnable, 1).State(ZEnable, 1).Build());
        Write("Shaders/Maya/Skinned.xnb", new Fx2TestBuilder().State(ZEnable, 1).Build());
        Write("Shaders/ArcaneEffect.xnb", new Fx2TestBuilder().State(ZEnable, 1).Build());
        File.WriteAllBytes(Path.Combine(root, "Shaders", "NotAnEffect.xnb"), new XnbTestWriter().StringObject("text").BuildBytes());
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void Write(string path, byte[] effect)
    {
        var w = new XnbTestWriter().TypeId(XnbTestWriter.XnaContent + "EffectReader" + XnbTestWriter.Xna);
        w.Writer.Write(effect.Length);
        w.Writer.Write(effect);
        File.WriteAllBytes(Path.Combine(root, path), w.BuildBytes());
    }

    private ShaderSource Source() => new(new ContentLoader(new ContentFileSystem(root), XnbReaderRegistry.Magicka));

    [Fact]
    public void Bytecode_IsTheEffectPatchedForFna_FoundByNameOrPath()
    {
        var source = Source();

        var bytecode = source.Bytecode("testeffect");

        Assert.Equal([ZEnable], Fx2Effect.Parse(bytecode).Passes.Single().States.Select(s => s.Operation));
        Assert.Same(bytecode, source.Bytecode("Shaders/TestEffect"));          // cached
        Assert.NotNull(source.Bytecode("Maya/Skinned"));
        Assert.Equal(["ArcaneEffect", "Maya/Skinned", "NotAnEffect", "TestEffect"], source.Names);
        Assert.Equal(["View"], source.Get("TestEffect").SharedParameters);
    }

    [Fact]
    public void EffectsThatCrashTheStockMojoShader_AreRefused()
    {
        var error = Assert.Throws<KnownBrokenEffectException>(() => Source().Bytecode("ArcaneEffect"));

        Assert.Contains("OM-62", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithOurFna3d_KnownBrokenEffectsAreNotSkipped()
    {
        var source = new ShaderSource(new ContentLoader(new ContentFileSystem(root), XnbReaderRegistry.Magicka), ownFna3d: true);

        Assert.NotNull(source.Bytecode("ArcaneEffect"));
        Assert.False(source.Skips("ArcaneEffect"));
        Assert.True(Source().Skips("arcaneeffect"));
    }

    [Fact]
    public void MissingAndNonEffectAssets_FailWithTheirName()
    {
        var source = Source();

        Assert.Contains("Missing", Assert.Throws<FileNotFoundException>(() => source.Bytecode("Missing")).Message, StringComparison.Ordinal);
        Assert.Contains("NotAnEffect", Assert.Throws<InvalidDataException>(() => source.Bytecode("NotAnEffect")).Message, StringComparison.Ordinal);
    }

    private sealed class FakeParameters(params string[] names) : IEffectParameters
    {
        public Dictionary<string, object> Set { get; } = [];

        public bool Contains(string name) => names.Contains(name);

        public void SetValue(string name, Matrix value) => Set[name] = value;

        public void SetValue(string name, Vector3 value) => Set[name] = value;

        public void SetValue(string name, float value) => Set[name] = value;

        public void SetValue(string name, bool value) => Set[name] = value;

        public void SetValue(string name, Vector2 value) => Set[name] = value;

        public void SetValue(string name, Vector4 value) => Set[name] = value;

        public void SetValue(string name, Microsoft.Xna.Framework.Graphics.Texture? value) => Set[name] = value!;
    }

    [Fact]
    public void GlobalValues_DeriveTheCombinedMatrices()
    {
        var view = Matrix.CreateLookAt(new Vector3(0, 144, 171), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(5), 16f / 9, 105, 500);
        var values = new GlobalEffectValues(view, projection, new Vector3(0, 144, 171), Vector3.Zero, 1.5f);

        Assert.Equal(view * projection, values.ViewProjection);
        var identity = values.ViewProjection * values.InverseViewProjection;
        Assert.True(Vector4.Distance(new Vector4(identity.M11, identity.M22, identity.M33, identity.M44), Vector4.One) < 1e-3f);
    }

    [Fact]
    public void Binding_SetsOnlyGlobalsTheEffectMarksShared()
    {
        // RenderDeferredEffect has its own, non-shared Time; the material sets it.
        var parameters = new FakeParameters("View", "Time", "World");
        var binding = new GlobalEffectBinding(parameters, shared: ["View", "World"]);

        binding.Apply(new GlobalEffectValues(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Zero, 3));

        Assert.Equal(["View"], parameters.Set.Keys);         // World is shared but per object, Time is not shared
    }

    [Fact]
    public void GlobalValues_StayConsistentAfterWith()
    {
        var values = new GlobalEffectValues(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Zero, 0);
        var moved = values with { View = Matrix.CreateTranslation(1, 2, 3) };

        Assert.Equal(moved.View * moved.Projection, moved.ViewProjection);
    }

    [Fact]
    public void Binding_SetsTheGlobalsAnEffectHas_AndNothingElse()
    {
        string[] all = ["View", "Projection", "ViewProjection", "InverseViewProjection", "CameraPosition", "EyeOfTheBeholder", "Time"];
        var parameters = new FakeParameters([.. all, "DiffuseColor"]);
        var binding = new GlobalEffectBinding(parameters, shared: [.. all, "DiffuseColor"]);
        var values = new GlobalEffectValues(Matrix.CreateTranslation(1, 2, 3), Matrix.Identity, new Vector3(4, 5, 6), Vector3.One, 2.5f);

        binding.Apply(values);

        Assert.Equal(all.Order(StringComparer.Ordinal), parameters.Set.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(values.ViewProjection, parameters.Set["ViewProjection"]);
        Assert.Equal(values.InverseViewProjection, parameters.Set["InverseViewProjection"]);
        Assert.Equal(Vector3.One, parameters.Set["EyeOfTheBeholder"]);
        Assert.Equal(2.5f, parameters.Set["Time"]);
        Assert.Equal(all.Order(StringComparer.Ordinal), binding.Bound.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Library_CreatesEachEffectOnce_WhateverTheSpelling_AndForgetsFailures()
    {
        var gpu = new OpenMagicka.Engine.Threading.RenderQueue<Microsoft.Xna.Framework.Graphics.GraphicsDevice>();
        using var library = new ShaderLibrary(Source(), gpu);

        var first = library.Load("Maya/Skinned");
        Assert.Same(first, library.Load("shaders\\MAYA\\skinned"));       // pending creation, shared
        await Assert.ThrowsAsync<FileNotFoundException>(() => library.Load("Missing"));
        await Assert.ThrowsAsync<KnownBrokenEffectException>(() => library.Load("ArcaneEffect"));
        await Assert.ThrowsAsync<ArgumentException>(() => library.Load(".."));

        gpu.Close();                                                         // the device goes away
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.NotSame(first, library.Load("Maya/Skinned"));                 // a cancelled creation is not kept
    }
}
