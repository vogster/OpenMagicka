using OpenMagicka.Content;
using OpenMagicka.Content.Audio;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>The game's WMA waves decoded by Windows' decoder and by ffmpeg (OM-61 phase B).</summary>
public class WmaDecoderTests(ITestOutputHelper output)
{
    private static WaveBankFile Bank(string asset) => WaveBankFile.Read(new ContentFileSystem(GameContent.ContentDirectory!).Resolve(asset)!);

    private static double Loudness(short[] samples) => Math.Sqrt(samples.Average(s => (double)s * s));

    private static IEnumerable<IWmaDecoder> Decoders()
    {
        if (OperatingSystem.IsWindows() && MediaFoundationWma.Open() is { } windows)
        {
            yield return windows;
        }
        if (FfmpegWma.Find() is { } ffmpeg)
        {
            yield return ffmpeg;
        }
    }

    [GameContentFact]
    public void EachDecoder_GivesAWaveItsLength_InSound()
    {
        var bank = Bank("Audio/Wave Bank.xwb");
        foreach (var decoder in Decoders())
        {
            foreach (var entry in new[] { bank.Entries[0], bank.Entries.First(e => e.Format.Channels == 2) })
            {
                var samples = decoder.Decode(entry, bank.Wave(entry));

                Assert.Equal(entry.Duration * entry.Format.Channels, samples.Length);
                Assert.True(Loudness(samples) > 100, $"{decoder.Name}: silent");
                Assert.True(Array.FindLastIndex(samples, v => v != 0) > samples.Length * 9 / 10, $"{decoder.Name}: cut short");   // not padded silence
                output.WriteLine($"{decoder.Name}: {entry.Format.Channels} ch {entry.Format.SampleRate}, loudness {Loudness(samples):F0}");
            }
        }
    }

    [GameContentFact]
    public void TheDecoders_Agree()
    {
        var decoders = Decoders().ToList();
        if (decoders.Count < 2)
        {
            return;                                                           // one decoder here: nothing to compare
        }
        var (speech, effects) = (Bank("Languages/eng/Loc.xwb"), Bank("Audio/Wave Bank.xwb"));
        Compare(decoders, speech, speech.Entries[3]);
        Compare(decoders, effects, effects.Entries[0]);                                              // 22 kHz mono: its stated rate is wrong
    }

    private void Compare(List<IWmaDecoder> decoders, WaveBankFile bank, WaveBankEntry entry)
    {
        var (a, b) = (decoders[0].Decode(entry, bank.Wave(entry)), decoders[1].Decode(entry, bank.Wave(entry)));
        double signal = 0, difference = 0;
        for (var i = 0; i < a.Length; i++)
        {
            signal += (double)a[i] * a[i];
            difference += (double)(a[i] - b[i]) * (a[i] - b[i]);
        }
        var ratio = 10 * Math.Log10(signal / Math.Max(difference, 1));
        output.WriteLine($"{decoders[0].Name} vs {decoders[1].Name}: {ratio:F1} dB");
        Assert.True(ratio > 20, $"{ratio:F1} dB apart");
    }
}
