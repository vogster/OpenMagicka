using OpenMagicka.Content;
using OpenMagicka.Content.Audio;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Engine;

/// <summary>The sound device and the game's banks (OM-51 phase A).</summary>
public class AudioBackendTests
{
    [Fact]
    public void SoundNames_FindACueByTheHashOfItsLoweredName()
    {
        var names = new SoundNames(["UI_Checkpoint01", "music_menu", null, ""]);

        Assert.Equal(2, names.Count);
        Assert.Equal("UI_Checkpoint01", names.Find(LegacyStringHash.Lower("ui_checkpoint01")));
        Assert.Equal("music_menu", names.Find("Music_Menu"));
        Assert.Null(names.Find("nothing"));
    }

    [Fact]
    public void Silence_HasNoBanksAndNoCues()
    {
        using var audio = new SilentAudio();
        audio.SetGlobal("RoomType", 10);
        audio.SetVolume("Music", 0.7f);
        audio.Update();

        Assert.False(audio.IsLoaded(SoundBanks.UI));
        Assert.Null(audio.GetCue(SoundBanks.UI, "ui_checkpoint01"));
    }

    [GameContentFact]
    public void TheGamesWaveBanks_SayWhichStreamAndWhichAreWma()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        WaveBankInfo Bank(string name) => WaveBankInfo.Read(files.Resolve($"Audio/{name}.xwb")!);

        Assert.True(Bank("Wave Bank").UsesWma);
        Assert.True(WaveBankInfo.Read(files.Resolve("Languages/eng/Loc.xwb")!).UsesWma);
        Assert.False(Bank("Characters").UsesWma);
        Assert.True(Bank("Music").IsStreaming);
        Assert.False(Bank("UI").IsStreaming);
        Assert.Equal("UI", Bank("UI").Name, ignoreCase: true);
        Assert.NotEmpty(Bank("Spells").Entries);
    }

    [GameContentFact]
    public void TheGame_LoadsItsBanks_LeavingOutTheWmaOnes_AndFindsItsCues()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var names = SoundNames.Load(new ContentLoader(files, XnbReaderRegistry.Magicka));
        Assert.Equal("ui_checkpoint01", names.Find("UI_CHECKPOINT01"));
        foreach (var element in (global::OpenMagicka.Gameplay.Spells.Elements[])[global::OpenMagicka.Gameplay.Spells.Elements.Water,
            global::OpenMagicka.Gameplay.Spells.Elements.Cold, global::OpenMagicka.Gameplay.Spells.Elements.Fire, global::OpenMagicka.Gameplay.Spells.Elements.Lightning,
            global::OpenMagicka.Gameplay.Spells.Elements.Arcane, global::OpenMagicka.Gameplay.Spells.Elements.Life, global::OpenMagicka.Gameplay.Spells.Elements.Steam,
            global::OpenMagicka.Gameplay.Spells.Elements.Poison])
        {
            var sound = Assert.Single(global::OpenMagicka.Gameplay.Spells.SpellSounds.For(global::OpenMagicka.Gameplay.Spells.Spell.Of(element),
                global::OpenMagicka.Gameplay.Spells.SpellType.Spray, global::OpenMagicka.Gameplay.Spells.CastType.Force));
            Assert.NotNull(names.Find(sound.Cue));                           // what sprays and strikes sounds (0.0.3); earth, shield and ice never spray
        }
        foreach (var raised in (string[])["misc_hit_blood", "wep_deathblow", "spell_arcane_ray_stage1", "spell_arcane_ray_stage2", "spell_arcane_ray_stage3",
            "spell_arcane_ray_stage4", "spell_life_ray_stage1", "spell_life_ray_stage2", "spell_shield_self", "spell_shield_self_ice", "spell_shield_self_earth",
            "spell_earth_barrier_break", "spell_ice_barrier_break", "spell_ice_earth_barrier_break", "spell_mine_activate", "ui_pickup_magick"])
        {
            Assert.NotNull(names.Find(raised));                                 // the cues the code raises (0.0.3)
        }

        using var audio = XactAudio.Open(files, "eng", out var failure);
        if (audio is null)
        {
            Assert.NotNull(failure);
            return;                                                           // no sound device (or FAudio) here
        }
        Assert.True(audio.WaitLoaded(TimeSpan.FromSeconds(60)));
        Assert.Contains(audio.Skipped, s => s.StartsWith("Audio/Wave Bank.xwb", StringComparison.Ordinal));
        Assert.Contains(audio.Skipped, s => s.Contains("Loc.xwb", StringComparison.Ordinal));
        Assert.All(XactAudio.Banks, b => Assert.True(audio.IsLoaded(b.Bank), b.Bank.ToString()));

        using var cue = audio.GetCue(SoundBanks.UI, "ui_checkpoint01");
        Assert.NotNull(cue);
        cue.SetVariable("NoSuchVariable", 1);
        Assert.Null(audio.GetCue(SoundBanks.UI, "no_such_cue"));
        audio.SetGlobal("RoomType", 10);
        audio.SetGlobal("NoSuchGlobal", 1);
        audio.SetVolume("Music", 0);
        audio.Update();
    }

    [GameContentFact]
    public void TheWmaBanks_LoadTranscoded_WhenThereIsADecoder()
    {
        if (WmaDecoders.Find() is not { } decoder)
        {
            return;                                                           // no WMA decoder here
        }
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var dir = Directory.CreateTempSubdirectory("om61");
        try
        {
            using (var audio = XactAudio.Open(files, "eng", out _, new TranscodedBanks(dir.FullName, () => decoder)))
            {
                if (audio is null)
                {
                    return;                                                   // no sound device here
                }
                Assert.True(audio.WaitLoaded(TimeSpan.FromMinutes(2)));
                Assert.DoesNotContain(audio.Skipped, s => s.Contains("WMA", StringComparison.Ordinal));
                using var cue = audio.GetCue(SoundBanks.WaveBank, "goblin_die");
                Assert.NotNull(cue);
            }
            Assert.Equal(2, Directory.GetFiles(dir.FullName, "*.xwb").Length);                         // the effects' bank and the speech's
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class SlowDecoder : IWmaDecoder
    {
        public string Name => "slow";

        public short[] Decode(OpenMagicka.Content.Audio.WaveBankEntry entry, ReadOnlySpan<byte> wave)
        {
            Thread.Sleep(250);                                                // slow enough that quitting always comes mid-bank
            return new short[entry.Duration * entry.Format.Channels];
        }
    }

    [GameContentFact]
    public void QuittingWhileABankIsTranscoded_StopsQuietly_AndLeavesNoCopy()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var dir = Directory.CreateTempSubdirectory("om61");
        try
        {
            var audio = XactAudio.Open(files, "eng", out _, new TranscodedBanks(dir.FullName, () => new SlowDecoder()));
            if (audio is null)
            {
                return;                                                       // no sound device here
            }
            Thread.Sleep(1500);                                               // the other banks load, the transcoding begins
            audio.Dispose();

            Assert.Empty(Directory.GetFiles(dir.FullName, "*.xwb"));
        }
        finally
        {
            Thread.Sleep(1500);                                               // a transcoding left running ends before its folder goes
            dir.Delete(recursive: true);
        }
    }
}
