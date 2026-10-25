using Microsoft.Xna.Framework.Audio;
using OpenMagicka.Content;
using OpenMagicka.Content.Audio;

namespace OpenMagicka.Tests.Content;

/// <summary>Wave banks read whole, written back, and MS-ADPCM (OM-61 phase A).</summary>
public class WaveBankTests
{
    private static short[] Sine(int frames, int channels, double hz, int rate = 44100) =>
        [.. Enumerable.Range(0, frames * channels).Select(i => (short)(12000 * Math.Sin(2 * Math.PI * hz * (i / channels + i % channels * 7) / rate)))];

    private static double SignalToNoise(short[] original, short[] decoded)
    {
        double signal = 0, noise = 0;
        for (var i = 0; i < original.Length; i++)
        {
            signal += (double)original[i] * original[i];
            noise += (double)(original[i] - decoded[i]) * (original[i] - decoded[i]);
        }
        return 10 * Math.Log10(signal / Math.Max(noise, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MsAdpcm_CodesSoundCloseInBlocksOf70BytesAChannel(int channels)
    {
        var samples = Sine(1000, channels, 440);

        var blocks = MsAdpcm.Encode(samples, channels, 70);
        var decoded = MsAdpcm.Decode(blocks, channels, 70, 1000);

        Assert.Equal(128, MsAdpcm.SamplesPerBlock(70));
        Assert.Equal((1000 + 127) / 128 * 70 * channels, blocks.Length);                              // the last block padded
        Assert.Equal(samples.Length, decoded.Length);
        Assert.Equal(samples[..(2 * channels)], decoded[..(2 * channels)]);                              // a block's first two samples are whole
        Assert.True(SignalToNoise(samples, decoded) > 25, $"{SignalToNoise(samples, decoded):F1} dB");
    }

    [Fact]
    public void ABankWritten_ReadsBack_WithItsWavesInOrder()
    {
        var adpcm = MsAdpcm.Encode(Sine(300, 1, 220), 1, 70);
        WaveBankWave[] waves =
        [
            new(0, 300, MiniWaveFormat.Adpcm(1, 44100, 70), adpcm, 0, 0, "first"),
            new(1, 3, new MiniWaveFormat(WaveFormat.Pcm, 2, 22050, 4, true), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 1, 2, null),
        ];

        var bank = WaveBankFile.Read(WaveBankWriter.Write("Wave Bank", waves, alignment: 4, buildTime: 7));

        Assert.Equal(("Wave Bank", WaveBankFile.NamesFlag, 4, 7UL), (bank.Name, bank.Flags, bank.Alignment, bank.BuildTime));
        Assert.Equal(2, bank.Entries.Count);
        var (first, second) = (bank.Entries[0], bank.Entries[1]);
        Assert.Equal((0, 300, MiniWaveFormat.Adpcm(1, 44100, 70), "first"), (first.Flags, first.Duration, first.Format, first.Name));
        Assert.Equal(48, first.Format.BlockAlign);
        Assert.Equal(adpcm, bank.Wave(first).ToArray());
        Assert.Equal((1, 3, 1, 2, ""), (second.Flags, second.Duration, second.LoopStart, second.LoopLength, second.Name));
        Assert.Equal(waves[1].Data, bank.Wave(second).ToArray());
        Assert.Equal(0, second.Offset % 4);
    }

    [Fact]
    public void AFormat_PacksAsItReads()
    {
        var format = new MiniWaveFormat(WaveFormat.Wma, 2, 44100, 99, false);

        Assert.Equal(format, MiniWaveFormat.From(format.Packed));
        Assert.Equal((6000, 2230), (format.WmaByteRate, format.WmaBlockSize));
    }

    [GameContentFact]
    public void TheGamesWmaBank_HasSeekTables_AndItsWavesWrapAsXwma()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var bank = WaveBankFile.Read(files.Resolve("Audio/Wave Bank.xwb")!);

        Assert.Equal(384, bank.Entries.Count);
        Assert.All(bank.Entries, e => Assert.Equal(WaveFormat.Wma, e.Format.Tag));
        var entry = bank.Entries[0];
        Assert.Equal((uint)entry.Duration * 2 * (uint)entry.Format.Channels, entry.SeekTable![^1]);       // the table ends at the decoded size

        var xwma = XwmaFile.Wrap(entry, bank.Wave(entry));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(xwma, 0, 4));
        Assert.Equal("XWMA", System.Text.Encoding.ASCII.GetString(xwma, 8, 4));
        Assert.Equal(xwma.Length - 8, BitConverter.ToInt32(xwma, 4));
    }

    [GameContentFact]
    public void ABankRewrittenAsMsAdpcm_PlaysInFact()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var source = WaveBankFile.Read(files.Resolve("Audio/UI.xwb")!);
        var waves = source.Entries.Select(e =>
        {
            Assert.Equal(WaveFormat.Pcm, e.Format.Tag);
            var pcm = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(source.Wave(e)).ToArray();
            return new WaveBankWave(e.Flags, e.Duration, MiniWaveFormat.Adpcm(e.Format.Channels, e.Format.SampleRate, 70),
                MsAdpcm.Encode(pcm, e.Format.Channels, 70), e.LoopStart, e.LoopLength, e.Name);
        }).ToList();
        var dir = Directory.CreateTempSubdirectory("om61");
        try
        {
            var path = Path.Combine(dir.FullName, "UI.xwb");
            File.WriteAllBytes(path, WaveBankWriter.Write(source.Name, waves, source.Alignment));
            AudioEngine engine;
            try
            {
                engine = new AudioEngine(files.Resolve("Audio/magicka.xgs")!);
            }
            catch (Exception e) when (e is NoAudioHardwareException or DllNotFoundException or InvalidOperationException)
            {
                return;                                                       // no sound device here
            }
            using (engine)
            using (var wave = new WaveBank(engine, path))
            using (var sounds = new SoundBank(engine, files.Resolve("Audio/UI.xsb")!))
            {
                using var cue = sounds.GetCue("ui_checkpoint01");
                cue.Play();
                var played = false;
                for (var i = 0; i < 20 && !played; i++)
                {
                    engine.Update();
                    played = cue.IsPlaying;
                    Thread.Sleep(5);
                }
                Assert.True(played);
                cue.Stop(AudioStopOptions.Immediate);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ACorruptBank_FailsToRead_AsBadData()
    {
        var bank = WaveBankWriter.Write("Wave Bank", [new WaveBankWave(0, 4, new MiniWaveFormat(WaveFormat.Pcm, 1, 44100, 2, true), new byte[8], 0, 0, null)]);
        var otherVersion = (byte[])bank.Clone();
        otherVersion[4] = 30;
        var cut = bank[..100];

        Assert.Throws<InvalidDataException>(() => WaveBankFile.Read(otherVersion));
        Assert.Throws<InvalidDataException>(() => WaveBankFile.Read(cut));
        Assert.Throws<InvalidDataException>(() => WaveBankFile.Read("RIFF0000WAVE"u8.ToArray()));
    }
}
