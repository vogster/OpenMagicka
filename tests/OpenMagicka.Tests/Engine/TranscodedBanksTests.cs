using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;
using OpenMagicka.Content;
using OpenMagicka.Content.Audio;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>The WMA banks transcoded once into OpenMagicka's data folder (OM-61 phase C).</summary>
public class TranscodedBanksTests(ITestOutputHelper output)
{
    /// <summary>A decoder that gives a tone of the wave's length.</summary>
    private sealed class ToneDecoder : IWmaDecoder
    {
        public int Calls { get; private set; }

        public string Name => "tone";

        public short[] Decode(WaveBankEntry entry, ReadOnlySpan<byte> wave)
        {
            Calls++;
            return [.. Enumerable.Range(0, entry.Duration * entry.Format.Channels).Select(i => (short)(8000 * Math.Sin(i * 0.05)))];
        }
    }

    /// <summary>A bank of a PCM wave and a WMA one (its data any bytes: the decoder makes the tone).</summary>
    private static byte[] MixedBank()
    {
        var bank = WaveBankWriter.Write("Wave Bank",
        [
            new WaveBankWave(0, 1000, new MiniWaveFormat(WaveFormat.Pcm, 1, 44100, 2, true), new byte[2000], 0, 0, "kept"),
            new WaveBankWave(0, 300, new MiniWaveFormat(WaveFormat.Pcm, 1, 22050, 2, true), new byte[600], 0, 0, "spoken"),
        ]);
        var entries = BitConverter.ToInt32(bank, 12 + 8);
        BitConverter.GetBytes(new MiniWaveFormat(WaveFormat.Wma, 1, 22050, 96, false).Packed).CopyTo(bank, entries + WaveBankWriter.EntrySize + 4);
        return bank;
    }

    [Fact]
    public void ABanksWmaWaves_BecomeMsAdpcm_AndTheOthersStay()
    {
        var decoder = new ToneDecoder();
        var source = WaveBankFile.Read(MixedBank());

        var bank = WaveBankFile.Read(TranscodedBanks.Transcode(source, decoder));

        Assert.Equal(1, decoder.Calls);
        Assert.Equal(("Wave Bank", 2), (bank.Name, bank.Entries.Count));
        Assert.Equal(source.Entries[0].Format, bank.Entries[0].Format);
        var spoken = bank.Entries[1];
        Assert.Equal((MiniWaveFormat.Adpcm(1, 22050, 70), 300, "spoken"), (spoken.Format, spoken.Duration, spoken.Name));
        Assert.Equal((300 + 127) / 128 * 70, spoken.Length);
    }

    [Fact]
    public void ACopy_IsMadeOnce_ByItsSourcesHash()
    {
        var dir = Directory.CreateTempSubdirectory("om61");
        try
        {
            var source = Path.Combine(dir.FullName, "Wave Bank.xwb");
            File.WriteAllBytes(source, MixedBank());
            var decoder = new ToneDecoder();
            var banks = new TranscodedBanks(Path.Combine(dir.FullName, "cache"), () => decoder);

            Assert.Null(banks.Find(source));
            var made = banks.FindOrMake(source);
            Assert.NotNull(made);
            Assert.Equal(made, banks.FindOrMake(source));
            Assert.Equal(1, decoder.Calls);

            Assert.Null(new TranscodedBanks(Path.Combine(dir.FullName, "other"), () => null).FindOrMake(source));   // no decoder: none
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [GameContentFact]
    public void TheGamesWaveBank_TranscodedByThisSystemsDecoder_PlaysInFact()
    {
        if (WmaDecoders.Find() is not { } decoder)
        {
            return;                                                           // no WMA decoder here
        }
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var dir = Directory.CreateTempSubdirectory("om61");
        try
        {
            var clock = Stopwatch.StartNew();
            var path = new TranscodedBanks(dir.FullName, () => decoder).FindOrMake(files.Resolve("Audio/Wave Bank.xwb")!)!;
            output.WriteLine($"{decoder.Name}: Wave Bank in {clock.Elapsed.TotalSeconds:F1} s, {new FileInfo(path).Length / 1e6:F1} MB");
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
            using (var sounds = new SoundBank(engine, files.Resolve("Audio/Sound Bank.xsb")!))
            {
                var bank = WaveBankFile.Read(path);
                Assert.All(bank.Entries, e => Assert.Equal(WaveFormat.Adpcm, e.Format.Tag));
                using var cue = sounds.GetCue("goblin_die");
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

    private sealed class FailingDecoder : IWmaDecoder
    {
        public string Name => "failing";

        public short[] Decode(WaveBankEntry entry, ReadOnlySpan<byte> wave) => throw new InvalidDataException("not this one");
    }

    [Fact]
    public void AWaveThatDoesNotDecode_IsSilence_TheBankKept()
    {
        var bank = WaveBankFile.Read(TranscodedBanks.Transcode(WaveBankFile.Read(MixedBank()), new FailingDecoder()));

        var spoken = bank.Entries[1];
        Assert.Equal(WaveFormat.Adpcm, spoken.Format.Tag);
        Assert.All(MsAdpcm.Decode(bank.Wave(spoken), 1, TranscodedBanks.ChannelBlock, spoken.Duration), s => Assert.Equal(0, s));
    }
}
