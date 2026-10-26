using SoundBanks = OpenMagicka.Content.Magicka.SoundBanks;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Entities;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>A level's sounds (OM-51 phase C): the gameplay's cues, the scripts' sounds and music, the scene's reverb, checkpoints.</summary>
public class SoundDirectorTests
{
    private static readonly string[] Cues = ["spell_fire_hit", "chr_goblin_pain", "footstep_grass", "music_a", "music_b", "amb_wind", "amb_bird",
        "spell_fire_onfire", "spell_steam_hit", "ui_element_fire", "ui_element_water", "ui_element_cold", "spell_water_drip", "spell_mine01", "misc_gib", "hum", "spell_fire_spray"];

    private static (LevelPlay Play, FakeAudio Audio) Heard(string scripts = "")
    {
        var play = EffectDirectorTests.Play(scripts);
        var audio = new FakeAudio(Cues);
        play.Hear(new SoundSystem(audio, new SoundNames(Cues)));
        play.Start();
        play.Tick(Tick);
        return (play, audio);
    }

    private static void Raise(LevelPlay play, Entity source, AnimationCue cue) =>
        typeof(EntityManager).GetMethod("RaiseCue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(play.World.Entities, [source, cue]);

    private static FakeCue Made(FakeAudio audio, string name) => audio.Made.Last(c => c.Name == name);

    [Fact]
    public void TheScenesReverb_IsSetAsItIsEntered()
    {
        var (play, audio) = Heard("""<Reverb roomType="Cave" mix="30"/>""");
        using var _ = play;

        Assert.Equal(40, audio.Globals["RoomType"]);
        Assert.True(audio.Globals["ReverbMix"] > 0);
    }

    [Fact]
    public void AnEntitysSound_PlaysWhereItRose_AndStopsByName_AStepByItsGround()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var avatar = play.Player.Avatar!;

        Raise(play, avatar, new SoundCue("chr_goblin_pain", (int)SoundBanks.Characters, new Numerics.Vector3(1, 0, 2), Numerics.Vector3.UnitX, 4));
        Raise(play, avatar, new FootstepCue(2, 80, Numerics.Vector3.Zero, Numerics.Vector3.UnitZ));
        play.Tick(Tick);

        var pain = Made(audio, "chr_goblin_pain");
        Assert.Equal((SoundBanks.Characters, true, 4f), (pain.Bank, pain.IsPlaying, pain.Variables["Magnitude"]));
        Assert.Equal(new Numerics.Vector3(1, 0, 2), Assert.Single(pain.Places).Position);
        var step = Made(audio, "footstep_grass");
        Assert.Equal((SoundBanks.Footsteps, 80f), (step.Bank, step.Variables["Weight"]));

        Raise(play, avatar, new SoundStopCue("CHR_GOBLIN_PAIN"));
        play.Tick(Tick);
        Assert.True(pain.IsStopping);
    }

    [Fact]
    public void ASpellsSound_GoesWithItsCaster_TheRestStayWhereTheyRose()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var avatar = play.Player.Avatar!;

        Raise(play, avatar, new SoundCue("spell_fire_spray", (int)SoundBanks.Spells, new Numerics.Vector3(9, 0, 9), Numerics.Vector3.UnitX) { Follow = true });
        Raise(play, avatar, new SoundCue("chr_goblin_pain", (int)SoundBanks.Characters, new Numerics.Vector3(1, 0, 2), Numerics.Vector3.UnitX));
        play.Tick(Tick);

        Assert.Equal(avatar.Position, Made(audio, "spell_fire_spray").Places.Last().Position);
        Assert.Equal(new Numerics.Vector3(1, 0, 2), Assert.Single(Made(audio, "chr_goblin_pain").Places).Position);
    }

    [Fact]
    public void AScriptsNamedSound_PlaysAgainWhenAskedAgain_StopsAndIsTuned()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;

        sound.Take(new PlaySoundRequest(0, SoundBanks.Spells, "spell_fire_hit", null, null, 0, false));
        Assert.Empty(Made(audio, "spell_fire_hit").Variables);                                          // no volume asked: none set

        sound.Take(new PlaySoundRequest(7, SoundBanks.Ambience, "amb_wind", 0.5f, null, 0, false));
        var wind = Made(audio, "amb_wind");
        Assert.Equal((true, 0.5f), (wind.IsPlaying, wind.Variables["Volume"]));
        sound.Take(new SetVolumeRequest(7, -3));
        Assert.Equal(-3, wind.Variables["Volume"]);

        sound.Take(new StopSoundRequest(7, Instant: true));
        Assert.True(wind.StoppedAtOnce);
        sound.Take(new PlaySoundRequest(7, SoundBanks.Ambience, "amb_wind", null, null, 0, false));
        var again = Made(audio, "amb_wind");
        Assert.NotSame(wind, again);
        Assert.Equal((true, -3f), (again.IsPlaying, again.Variables["Volume"]));                     // the tuned volume kept
    }

    [Fact]
    public void ALocatedSound_PlaysOnce_ItsDistanceOverItsRadiusFollowingTheCamera()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;
        var at = new Numerics.Vector3(-5, 0, 0);

        sound.Take(new PlaySoundRequest(9, SoundBanks.Ambience, "amb_bird", 0, at, 10, true));
        sound.Take(new PlaySoundRequest(9, SoundBanks.Ambience, "amb_bird", 0, at, 10, true));
        play.Tick(Tick);

        var bird = Assert.Single(audio.Made, c => c.Name == "amb_bird");
        var ground = new Numerics.Vector3(play.Camera.Ground.X, play.Camera.Ground.Y, play.Camera.Ground.Z);
        Assert.Equal(Numerics.Vector3.Distance(at, ground) / 10, bird.Variables["NormalizedDistance"], 3);
        Assert.All(bird.Places, p => Assert.Equal(at, p.Position));

        bird.Finish();
        play.Tick(Tick);
        sound.Take(new PlaySoundRequest(9, SoundBanks.Ambience, "amb_bird", 0, at, 10, true));
        Assert.Equal(2, audio.Made.Count(c => c.Name == "amb_bird"));                                 // ended: it may play again
    }

    [Fact]
    public void TheScriptsMusic_PlaysWithItsFocus_AndComesBackWithACheckpoint()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;

        sound.Take(new PlayMusicRequest(SoundBanks.Music, "music_a", 0.3f));
        var saved = sound.Save();
        sound.Take(new PlayMusicRequest(SoundBanks.Music, "music_b", null));
        sound.Take(new PlaySoundRequest(4, SoundBanks.Ambience, "amb_wind", 0.5f, null, 0, false));
        Assert.Equal("music_b", play.Sound!.Sounds.Music);

        sound.Restore(saved);

        Assert.True(Made(audio, "amb_wind").StoppedAtOnce);
        Assert.Equal("music_a", sound.Sounds.Music);
        play.Tick(Tick);
        Assert.Equal(0.3f, audio.Globals["MusicFocus"], 4);
    }

    [Fact]
    public void SilentLevels_TakeNoSoundRequests()
    {
        using var play = EffectDirectorTests.Play();
        play.Start();
        play.Tick(Tick);

        Assert.Null(play.Sound);
    }

    [Fact]
    public void ABurningCharacter_SoundsOfFireTillItIsPutOut_ThenHisses()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var avatar = play.Player.Avatar!;

        avatar.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 1), null, Numerics.Vector3.Zero);
        play.Tick(Tick);
        play.Tick(Tick);

        var fire = Assert.Single(audio.Made, c => c.Name == "spell_fire_onfire");
        Assert.True(fire.IsPlaying);
        Assert.True(fire.Places.Count >= 2);                                                         // it follows the burning one

        avatar.StopStatuses(StatusEffects.Burning);
        play.Tick(Tick);

        Assert.True(fire.IsStopping);
        Assert.DoesNotContain(audio.Made, c => c.Name == "spell_steam_hit");                         // it burnt out: no water, no hiss

        avatar.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 60, 1), null, Numerics.Vector3.Zero);
        play.Tick(Tick);
        var drip = Assert.Single(audio.Made, c => c.Name == "spell_water_drip");
        drip.Finish();
        play.Tick(Tick);
        Assert.Single(audio.Made, c => c.Name == "spell_water_drip");                                // a loop that ended is not started again
        avatar.StopStatuses(StatusEffects.Wet);
        play.Tick(Tick);
        Assert.Single(audio.Made, c => c.Name == "spell_steam_hit");                                 // it dried
    }

    [Fact]
    public void AnElementJoiningAPlayersQueue_SoundsOfIt()
    {
        var (play, audio) = Heard();
        using var _ = play;

        var avatar = play.Player.Avatar!;
        Assert.True(avatar.Conjure(Elements.Fire));
        play.Tick(Tick);
        Assert.Equal(SoundBanks.UI, Assert.Single(audio.Made, c => c.Name == "ui_element_fire").Bank);

        avatar.Conjure(Elements.Water);                                                              // fire and water make steam: no longer queue
        avatar.Conjure(Elements.Cold);
        play.Tick(Tick);
        Assert.Single(audio.Made, c => c.Name == "ui_element_water");
        Assert.Single(audio.Made, c => c.Name == "ui_element_cold");
    }

    [Fact]
    public void ACharactersOwnSounds_PlayWhileItLives_AndItShattersWithAGib()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var hummer = Spawn(play.World, Goblin with { Sounds = [new TemplateSound("hum", (int)SoundBanks.Misc)] }, new Numerics.Vector3(3, 0.9f, 0));
        play.Tick(Tick);

        var hum = Assert.Single(audio.Made, c => c.Name == "hum");
        Assert.True(hum.IsPlaying);
        hummer.OverKill();
        play.Tick(Tick);
        play.Tick(Tick);

        Assert.True(hum.IsStopping);
        Assert.Single(audio.Made, c => c.Name == "misc_gib");
    }

    [Fact]
    public void ElementsAndBarriers_NameTheirCues()
    {
        Assert.Equal("ui_element_cold", SoundDirector.ElementCue(Elements.Ice));
        Assert.Equal("ui_element_fire", SoundDirector.ElementCue(Elements.Steam));
        Assert.Equal("ui_element_lightning", SoundDirector.ElementCue(Elements.Lightning));
        Assert.Null(SoundDirector.ElementCue(Elements.All));

        Assert.Equal(["spell_ice_earth_barrier", "spell_fire_barrier"], SoundDirector.BarrierCues(Elements.Earth | Elements.Ice | Elements.Fire));
        Assert.Equal(["spell_earth_barrier"], SoundDirector.BarrierCues(Elements.Earth | Elements.Shield));
    }

    [Fact]
    public void ATemplatesSounds_AreItsFirstFourNamedOnes_Lowered()
    {
        var sounds = OpenMagicka.Engine.Characters.ItemTemplates.Sounds(
            [new("Hum_A", SoundBanks.Misc), new("", SoundBanks.Misc), new("b", SoundBanks.Weapons), new("c", SoundBanks.Misc), new("d", SoundBanks.Misc), new("e", SoundBanks.Misc)]);

        Assert.Equal(["hum_a", "b", "c", "d"], sounds.Select(s => s.Cue));
        Assert.Equal((int)SoundBanks.Weapons, sounds[1].Banks);
    }

    [Fact]
    public void ARestartInItsOwnScene_KeepsTheSoundsTheCheckpointGaveBack_ButNotTheStoppedOnes()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;
        var scene = play.Scene!.Key;
        sound.Take(new PlaySoundRequest(4, SoundBanks.Ambience, "amb_wind", 0.5f, null, 0, false));
        sound.Take(new PlaySoundRequest(5, SoundBanks.Ambience, "amb_bird", null, null, 0, false));
        sound.Take(new StopSoundRequest(5, Instant: true));
        var saved = sound.Save();

        sound.Restore(saved);
        sound.Take(new SceneChangedRequest(scene, scene, Kept: false));

        var wind = Made(audio, "amb_wind");
        Assert.True(wind.IsPlaying && !wind.IsStopping);
        Assert.Single(audio.Made, c => c.Name == "amb_bird");                                         // stopped at the checkpoint: not again
        sound.Take(new PlaySoundRequest(5, SoundBanks.Ambience, "amb_bird", null, null, 0, false));
        Assert.True(Made(audio, "amb_bird").IsPlaying);                                              // still known: asked again, it plays
    }

    [Fact]
    public void ADialogsLines_PlayOnce_AndAreNoScenesSounds()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;

        sound.Take(new PlaySoundRequest(-1, SoundBanks.Characters, "chr_goblin_pain", null, null, 0, false));
        var line = Made(audio, "chr_goblin_pain");
        sound.Take(new StopSoundRequest(-1, Instant: false));
        Assert.True(line.IsStopping);
        line.Finish();

        sound.Restore(sound.Save());
        sound.Take(new SceneChangedRequest(play.Scene!.Key, play.Scene.Key, Kept: true));

        Assert.Single(audio.Made, c => c.Name == "chr_goblin_pain");
    }

    [Fact]
    public void ARestore_StopsTheMusicTheCheckpointDidNotHave()
    {
        var (play, audio) = Heard();
        using var _ = play;
        var sound = play.Sound!;
        var saved = sound.Save();
        sound.Take(new PlayMusicRequest(SoundBanks.Music, "music_b", null));

        sound.Restore(saved);

        Assert.True(Made(audio, "music_b").IsStopping);
        Assert.Null(sound.Music);
    }

    [Fact]
    public void AnElementAFullQueueRefused_MakesNoSound()
    {
        var (play, audio) = Heard();
        using var _ = play;

        Raise(play, play.Player.Avatar!, new ConjureCue(Elements.Fire, [new QueueAdded(Elements.Fire)], Taken: false));
        play.Tick(Tick);

        Assert.DoesNotContain(audio.Made, c => c.Name == "ui_element_fire");
    }
}
