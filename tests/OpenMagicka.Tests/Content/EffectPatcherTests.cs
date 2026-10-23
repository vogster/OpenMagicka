using OpenMagicka.Content.Effects;
using static OpenMagicka.Tests.Content.Fx2TestBuilder;

namespace OpenMagicka.Tests.Content;

public class EffectPatcherTests
{
    [Fact]
    public void UnsupportedPassStates_AreRemovedAndShaderBindingsRenumbered()
    {
        var shader = ShaderWithHarmlessPreshader();
        var effect = new Fx2TestBuilder()
            .State(ZEnable, 1)
            .State(AlphaTestEnable, 0)
            .State(ZWriteEnable, 1)
            .State(VertexShader, 0)
            .Shader(stateIndex: 3, shader)
            .Build();

        var result = EffectPatcher.Patch(effect);

        Assert.Equal(1, result.RemovedStates);
        var layout = Fx2Effect.Parse(result.Bytecode);
        var pass = Assert.Single(layout.Passes);
        Assert.Equal([ZEnable, ZWriteEnable, VertexShader], pass.States.Select(s => s.Operation));
        Assert.Equal(2, Assert.Single(layout.Resources).State);   // was 3: bound to VertexShader
    }

    [Fact]
    public void BorderAddressing_BecomesClamp()
    {
        var effect = new Fx2TestBuilder()
            .Sampler(AddressU, 4)                                 // BORDER
            .Sampler(AddressV, 5)                                 // MIRRORONCE
            .Sampler(BorderColor, 0)
            .State(ZEnable, 1)
            .Build();

        var result = EffectPatcher.Patch(effect);

        Assert.Equal(3, result.ChangedSamplerStates);
        var states = Fx2Effect.Parse(result.Bytecode).SamplerStates;
        Assert.Equal([AddressU, AddressV, AddressU], states.Select(s => s.Operation));
        Assert.All(states, s => Assert.Equal(3u, s.Value));       // CLAMP
    }

    [Fact]
    public void OverflowingPreshader_GetsAnExtraInstructionAndEnoughTemps()
    {
        var effect = new Fx2TestBuilder().State(VertexShader, 0).Shader(0, ShaderWithOverflowingPreshader()).Build();
        Assert.Equal((8, 10), Preshader.TempUsage(Preshader.Find(Fx2Effect.Parse(effect).Resources[0].Code).Single()));

        var result = EffectPatcher.Patch(effect);

        Assert.Equal(1, result.WidenedPreshaders);
        var resource = Assert.Single(Fx2Effect.Parse(result.Bytecode).Resources);
        var preshader = Assert.Single(Preshader.Find(resource.Code));
        var (allocated, needed) = Preshader.TempUsage(preshader);
        Assert.True(allocated >= needed, $"allocated {allocated}, needed {needed}");
        Assert.Equal(3, Preshader.InstructionCount(preshader));
        Assert.Equal(ShaderWithOverflowingPreshader().Length + 32, resource.Code.Length);
    }

    [Fact]
    public void EffectWithNothingToPatch_IsReturnedUnchanged()
    {
        var effect = new Fx2TestBuilder().State(ZEnable, 1).Sampler(AddressU, 3).State(VertexShader, 0)
            .Shader(1, ShaderWithHarmlessPreshader()).Build();

        var result = EffectPatcher.Patch(effect);

        Assert.Equal(effect, result.Bytecode);
        Assert.Equal((0, 0, 0), (result.RemovedStates, result.ChangedSamplerStates, result.WidenedPreshaders));
    }

    [Fact]
    public void ShaderBoundToARemovedState_IsInvalid()
    {
        var effect = new Fx2TestBuilder().State(AlphaTestEnable, 1).Shader(0, ShaderWithHarmlessPreshader()).Build();

        Assert.Throws<InvalidDataException>(() => EffectPatcher.Patch(effect));
    }

    [Fact]
    public void MalformedEffects_AreInvalid()
    {
        var effect = new Fx2TestBuilder().State(ZEnable, 1).Build();

        Assert.Throws<InvalidDataException>(() => EffectPatcher.Patch([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Throws<InvalidDataException>(() => EffectPatcher.Patch(effect[..^6]));
        Assert.Throws<InvalidDataException>(() => EffectPatcher.Patch([.. effect, 0, 0, 0, 0]));
    }

    private static byte[] RichEffect() => new Fx2TestBuilder()
        .Sampler(AddressU, 4).Sampler(BorderColor, 0)
        .State(AlphaTestEnable, 0).State(ZEnable, 1).State(AlphaTestEnable, 1).State(VertexShader, 0).State(PixelShader, 0)
        .Shader(3, ShaderWithOverflowingPreshader()).Shader(4, ShaderWithHarmlessPreshader())
        .Build();

    [Fact]
    public void SeveralCutsAndShaders_KeepEveryBindingConsistent()
    {
        var result = EffectPatcher.Patch(RichEffect());

        var parsed = Fx2Effect.Parse(result.Bytecode);
        Assert.Equal([ZEnable, VertexShader, PixelShader], Assert.Single(parsed.Passes).States.Select(s => s.Operation));
        Assert.Equal([1, 2], parsed.Resources.Select(r => r.State));
        Assert.Equal((2, 2, 1), (result.RemovedStates, result.ChangedSamplerStates, result.WidenedPreshaders));
    }

    [Fact]
    public void MalformedPreshader_IsInvalid()
    {
        var shader = ShaderWithOverflowingPreshader();
        shader[16 + 8] = 0xFF;                       // arrays count of the first operand
        shader[16 + 9] = 0xFF;
        var effect = new Fx2TestBuilder().State(VertexShader, 0).Shader(0, shader).Build();

        Assert.Throws<InvalidDataException>(() => EffectPatcher.Patch(effect));
    }

    [Fact]
    public void TruncatedAtEveryLength_OnlyEverThrowsInvalidData()
    {
        var effect = RichEffect();
        for (var length = 0; length < effect.Length; length++)
        {
            try
            {
                EffectPatcher.Patch(effect[..length]);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void RandomCorruption_OnlyEverThrowsInvalidData()
    {
        var effect = RichEffect();
        var random = new Random(42);
        for (var i = 0; i < 5000; i++)
        {
            var corrupted = (byte[])effect.Clone();
            for (var m = random.Next(1, 4); m > 0; m--)
            {
                corrupted[random.Next(8, corrupted.Length)] = (byte)random.Next(256);
            }
            try
            {
                EffectPatcher.Patch(corrupted);
            }
            catch (InvalidDataException)
            {
            }
        }
    }
}
