using System.Numerics;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;

namespace OpenMagicka.Tests.Engine;

/// <summary>The game's sounds at play (OM-51 phase B): cues, the music, its focus and threat, the reverb.</summary>
public class SoundSystemTests
{
    private const float Tick = 1 / 60f;

    private static (SoundSystem Sounds, FakeAudio Audio) Make(params string[] cues)
    {
        var audio = new FakeAudio(cues);
        return (new SoundSystem(audio, new SoundNames(cues)), audio);
    }

    [Fact]
    public void ACue_PlaysFlatOrForTheListener_WithItsVariables_AndIsLetGoWhenStopped()
    {
        var (sounds, audio) = Make("spell_fire_hit", "UI_Checkpoint01");
        sounds.Listen(new Vector3(4, 0, 2));

        Assert.Null(sounds.Play(SoundBanks.Spells, "spell_no_such"));
        var flat = (FakeCue)sounds.Play(SoundBanks.UI, "ui_checkpoint01")!;
        var placed = (FakeCue)sounds.Play(SoundBanks.Spells, "spell_fire_hit", SoundPlace.At(Vector3.UnitX, Vector3.UnitZ), ("Magnitude", 3))!;

        Assert.Equal("UI_Checkpoint01", flat.Name);
        Assert.True(flat.IsPlaying);
        Assert.Empty(flat.Places);
        Assert.Equal(Vector3.UnitX, Assert.Single(placed.Places).Position);
        Assert.Equal(3, placed.Variables["Magnitude"]);
        Assert.Equal(new Vector3(4, SoundSystem.ListenerHeight, 2), sounds.Listener.Position);
        Assert.Equal(2, sounds.Active.Count);

        flat.Finish();
        sounds.Update(Tick, []);
        Assert.Same(placed, Assert.Single(sounds.Active));
        Assert.True(flat.IsDisposed);
        Assert.Equal(1, audio.Updates);
    }

    [Fact]
    public void StoppingPausingAndResumingAll_LeaveTheMusicAlone()
    {
        var (sounds, audio) = Make("ambience_wind", "music_menu");
        var wind = (FakeCue)sounds.Play(SoundBanks.Ambience, "ambience_wind")!;
        sounds.PlayMusic(SoundBanks.Music, "music_menu");
        var music = audio.Made.Single(c => c.Name == "music_menu");

        sounds.PauseAll();
        Assert.True(wind.IsPaused);
        Assert.False(music.IsPaused);
        sounds.ResumeAll();
        Assert.False(wind.IsPaused);

        sounds.StopAll(immediate: true);
        Assert.True(wind.StoppedAtOnce);
        Assert.True(music.IsPlaying);
        Assert.Equal("music_menu", sounds.Music);
    }

    [Fact]
    public void TheMusic_GoesOnForTheSameCue_FadesForAnother_AndStops()
    {
        var (sounds, audio) = Make("music_menu", "music_battle");
        sounds.PlayMusic(SoundBanks.Music, "music_menu");
        sounds.PlayMusic(SoundBanks.Music, "MUSIC_MENU");
        Assert.Single(audio.Made);

        var menu = audio.Made[0];
        sounds.PlayMusic(SoundBanks.Music, "music_battle");
        Assert.True(menu.IsStopping);
        Assert.False(menu.StoppedAtOnce);
        Assert.Equal("music_battle", sounds.Music);

        menu.Finish();
        sounds.Update(Tick, []);
        Assert.True(menu.IsDisposed);

        sounds.StopMusic();
        Assert.True(audio.Made[1].IsStopping);
        audio.Made[1].Finish();
        sounds.Update(Tick, []);
        Assert.Null(sounds.Music);
    }

    [Fact]
    public void TheMusicsFocus_IsTheNearestAvatarsDistanceOverTheRadius_OrItsDefault()
    {
        var (sounds, audio) = Make("music_menu");
        sounds.Update(Tick, []);
        Assert.Equal(1, audio.Globals["MusicFocus"]);

        sounds.FocusMusic(Vector3.Zero, 10, clearTo: 0.25f);
        sounds.Update(Tick, [new Vector3(5, 0, 0), new Vector3(20, 0, 0)]);
        Assert.Equal(0.5f, audio.Globals["MusicFocus"], 4);
        sounds.Update(Tick, []);
        Assert.Equal(1, audio.Globals["MusicFocus"]);                                                  // nobody alive: far

        sounds.ClearMusicFocus();
        sounds.Update(Tick, [Vector3.Zero]);
        Assert.Equal(0.25f, audio.Globals["MusicFocus"]);

        sounds.FocusMusic(Vector3.Zero, 10, clearTo: 1);
        sounds.PlayMusic(SoundBanks.Music, "music_menu", focus: 0.6f);                                // a new music lets the point go
        sounds.Update(Tick, [Vector3.Zero]);
        Assert.Equal(0.6f, audio.Globals["MusicFocus"]);
    }

    [Fact]
    public void TheThreat_Rises5ASecondTo10_AndFalls2()
    {
        var (sounds, audio) = Make();
        sounds.Threat = true;
        for (var i = 0; i < 60; i++)
        {
            sounds.Update(Tick, []);
        }
        Assert.Equal(5, audio.Globals["MusicThreat"], 3);
        for (var i = 0; i < 120; i++)
        {
            sounds.Update(Tick, []);
        }
        Assert.Equal(10, audio.Globals["MusicThreat"]);

        sounds.Threat = false;
        sounds.Update(0.5f, []);                                                                        // a long step counts as 0.05 s
        Assert.Equal(10 - 2 * SoundSystem.MaxStep, audio.Globals["MusicThreat"], 4);
    }

    [Fact]
    public void AScenesRoom_IsSetAtOnce_AndItsMixMoves50ASecond()
    {
        var (sounds, audio) = Make();
        sounds.SetRoom(RoomType.Cave, 30);
        Assert.Equal(40, audio.Globals["RoomType"]);

        sounds.Update(0.05f, []);
        Assert.Equal(2.5f, audio.Globals["ReverbMix"], 4);
        for (var i = 0; i < 60; i++)
        {
            sounds.Update(Tick, []);
        }
        Assert.Equal(30, audio.Globals["ReverbMix"]);

        sounds.FadeReverb();
        for (var i = 0; i < 60; i++)
        {
            sounds.Update(Tick, []);
        }
        Assert.Equal(0, audio.Globals["ReverbMix"]);
    }

    [Fact]
    public void TheVolumes_GoToTheMusicAndTheSoundsCategories()
    {
        var (sounds, audio) = Make();
        Assert.Equal(0.7f, audio.Volumes["Music"], 4);

        sounds.SetVolumes(12, 3);

        Assert.Equal(1, audio.Volumes["Music"]);
        Assert.All(SoundSystem.SoundCategories, c => Assert.Equal(0.3f, audio.Volumes[c], 4));
    }
}
