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
}
