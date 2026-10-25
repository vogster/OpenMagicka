namespace OpenMagicka.Content.Audio;

/// <summary>
/// Microsoft ADPCM (the RIFF format's, docs/engine/ARCHITECTURE.md §8.2), which FACT plays from wave banks. A block
/// holds, for each channel, a predictor (0…6), a step and its first two samples (the second, then the first), then a
/// 4-bit code for every later sample, channels interleaved, high nibble first. A sample is predicted from the two
/// before by the predictor's pair of coefficients (over 256, truncated), the code times the step added; the step
/// then scales by the code's factor (over 256, kept a 16-bit value of at least 16). The encoder tries every predictor
/// and a few first steps on each block and keeps the closest, decoding as FACT does.
/// </summary>
public static class MsAdpcm
{
    public static readonly (int First, int Second)[] Coefficients = [(256, 0), (512, -256), (0, 0), (192, 64), (240, 0), (460, -208), (392, -232)];

    private static readonly int[] Adaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

    /// <summary>A channel's block header: predictor, step and two samples.</summary>
    public const int HeaderSize = 7;

    /// <summary>The samples in a block of that many bytes per channel.</summary>
    public static int SamplesPerBlock(int channelBlock) => (channelBlock - HeaderSize) * 2 + 2;

    /// <summary>Interleaved 16-bit samples as blocks of <paramref name="channelBlock"/> bytes per channel; the last one padded with silence.</summary>
    public static byte[] Encode(ReadOnlySpan<short> samples, int channels, int channelBlock)
    {
        if (channels < 1 || channelBlock <= HeaderSize || samples.Length % channels != 0)
        {
            throw new ArgumentException("samples do not fill whole frames of the channels, or the block is too small");
        }
        var perBlock = SamplesPerBlock(channelBlock);
        var frames = samples.Length / channels;
        var blocks = (frames + perBlock - 1) / perBlock;
        var output = new byte[blocks * channelBlock * channels];
        var channel = new short[perBlock];
        var codes = new byte[channels, perBlock];
        var best = new byte[perBlock];
        for (var block = 0; block < blocks; block++)
        {
            var at = block * channelBlock * channels;
            for (var c = 0; c < channels; c++)
            {
                for (var i = 0; i < perBlock; i++)
                {
                    var frame = block * perBlock + i;
                    channel[i] = frame < frames ? samples[frame * channels + c] : (short)0;
                }
                var (predictor, step) = Choose(channel, best);
                for (var i = 2; i < perBlock; i++)
                {
                    codes[c, i] = best[i];
                }
                output[at + c] = (byte)predictor;
                Write16(output, at + channels + c * 2, step);
                Write16(output, at + channels * 3 + c * 2, channel[1]);
                Write16(output, at + channels * 5 + c * 2, channel[0]);
            }
            var nibble = 0;
            var data = at + channels * HeaderSize;
            for (var i = 2; i < perBlock; i++)
            {
                for (var c = 0; c < channels; c++, nibble++)
                {
                    output[data + nibble / 2] |= (byte)(nibble % 2 == 0 ? codes[c, i] << 4 : codes[c, i]);
                }
            }
        }
        return output;
    }

    /// <summary>The blocks as interleaved samples (<paramref name="frames"/> of them: the padding left out).</summary>
    public static short[] Decode(ReadOnlySpan<byte> blocks, int channels, int channelBlock, int frames)
    {
        var perBlock = SamplesPerBlock(channelBlock);
        var output = new short[frames * channels];
        var (predictor, step, older, newer) = (new int[channels], new int[channels], new int[channels], new int[channels]);
        for (var block = 0; block * channelBlock * channels < blocks.Length && block * perBlock < frames; block++)
        {
            var at = block * channelBlock * channels;
            for (var c = 0; c < channels; c++)
            {
                predictor[c] = Math.Min((int)blocks[at + c], Coefficients.Length - 1);
                step[c] = Read16(blocks, at + channels + c * 2);
                newer[c] = Read16(blocks, at + channels * 3 + c * 2);
                older[c] = Read16(blocks, at + channels * 5 + c * 2);
            }
            var nibble = 0;
            var data = at + channels * HeaderSize;
            for (var i = 0; i < perBlock && block * perBlock + i < frames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    int sample;
                    if (i < 2)
                    {
                        sample = i == 0 ? older[c] : newer[c];
                    }
                    else
                    {
                        var packed = blocks[data + nibble / 2];
                        var code = nibble++ % 2 == 0 ? packed >> 4 : packed & 0xF;
                        sample = Step(code, Coefficients[predictor[c]], ref step[c], ref older[c], ref newer[c]);
                    }
                    output[(block * perBlock + i) * channels + c] = (short)sample;
                }
            }
        }
        return output;
    }

    /// <summary>The predictor and first step that code a channel's block closest; its codes in <paramref name="codes"/>.</summary>
    private static (int Predictor, int Step) Choose(ReadOnlySpan<short> channel, byte[] codes)
    {
        var trial = new byte[channel.Length];
        var (bestError, bestPredictor, bestStep) = (long.MaxValue, 0, 16);
        for (var predictor = 0; predictor < Coefficients.Length; predictor++)
        {
            var (c1, c2) = Coefficients[predictor];
            var guess = channel.Length > 2 ? Math.Abs(channel[2] - (channel[1] * c1 + channel[0] * c2) / 256) : 0;
            foreach (var first in new[] { 16, guess / 8, guess / 4, guess / 2 })
            {
                var step = Math.Clamp(first, 16, short.MaxValue);
                var error = Code(channel, Coefficients[predictor], step, trial, bestError);
                if (error < bestError)
                {
                    (bestError, bestPredictor, bestStep) = (error, predictor, step);
                    trial.AsSpan().CopyTo(codes);
                }
            }
        }
        return (bestPredictor, bestStep);
    }

    /// <summary>Codes a channel's block from a predictor and step; the squared error (stopping once past <paramref name="limit"/>).</summary>
    private static long Code(ReadOnlySpan<short> channel, (int First, int Second) coefficients, int step, byte[] codes, long limit)
    {
        var (newer, older) = ((int)channel[1], (int)channel[0]);
        long error = 0;
        for (var i = 2; i < channel.Length && error < limit; i++)
        {
            var predicted = (newer * coefficients.First + older * coefficients.Second) / 256;
            var code = Math.Clamp((int)Math.Round((channel[i] - predicted) / (double)step), -8, 7);
            codes[i] = (byte)(code & 0xF);
            var sample = Step(codes[i], coefficients, ref step, ref older, ref newer);
            error += (long)(channel[i] - sample) * (channel[i] - sample);
        }
        return error;
    }

    /// <summary>A sample decoded from its code, as FACT does; the step and the two samples move on.</summary>
    private static int Step(int code, (int First, int Second) coefficients, ref int step, ref int older, ref int newer)
    {
        var signed = code >= 8 ? code - 16 : code;
        var sample = Math.Clamp((newer * coefficients.First + older * coefficients.Second) / 256 + signed * step, short.MinValue, short.MaxValue);
        (older, newer) = (newer, sample);
        step = (short)(Adaptation[code] * step / 256);
        step = step < 16 ? 16 : step;
        return sample;
    }

    private static void Write16(byte[] to, int at, int value) => (to[at], to[at + 1]) = ((byte)value, (byte)(value >> 8));

    private static short Read16(ReadOnlySpan<byte> from, int at) => (short)(from[at] | from[at + 1] << 8);
}
