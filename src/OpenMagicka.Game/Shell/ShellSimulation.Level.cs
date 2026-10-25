using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Engine.Text;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Menu;
using OpenMagicka.Game.Play;

namespace OpenMagicka.Game.Shell;

/// <summary>
/// A chapter from the menu to its end: it loads in the background (the loading screen up), starts once the render
/// thread shows its scene, pauses on Escape or Start, and ends as its script or the players' deaths say — the next
/// chapter, the checkpoint again, or back to the lobby; the slot keeps the time played and the chapter reached.
/// </summary>
public sealed partial class ShellSimulation
{
    private readonly PauseMenu pause;
    private Task<LevelPlay>? loading;
    private LevelPlay? play;
    private PlaySimulation? playing;
    private LevelEnding? ending;
    private int level, slot, chapter, tip = 1;
    private double loadingSeconds, waitSeconds, playSeconds;

    /// <summary>How long a started level waits for the render thread to show it before it runs anyway.</summary>
    public const double ShowTimeout = 60;
    private bool waiting, started, paused, byDeath;

    /// <summary>The level at play, or none.</summary>
    public LevelPlay? Level => play;

    public bool Paused => paused;

    public LevelEnding? Ending => ending;

    /// <summary>A chapter is started from a slot: it loads in the background.</summary>
    public void Play(int slot, int chapter)
    {
        if (slot is < 0 or >= CampaignSaves.Slots || chapter < 0 || chapter >= Chapters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(chapter), $"no chapter {chapter} in slot {slot}");
        }
        (this.slot, this.chapter, tip, loadingSeconds, mode) = (slot, chapter, Random.Shared.Next(1, LoadingScreen.Tips + 1), 0, ShellMode.Loading);
        content.Sounds?.StopMusic();
        var language = Settings.Language;
        loading = Task.Run(() => Open(chapter, language));
    }

    private LevelPlay Open(int chapter, string language)
    {
        var files = content.Files;
        var path = PlayGame.FindLevel(files, Chapters.Levels[chapter]) ?? throw new FileNotFoundException($"the game has no level {Chapters.Levels[chapter]}");
        var definition = LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);
        return LevelPlay.Open(definition, files, content.Loader, language);
    }

    private void TickLoading(double dt)
    {
        loadingSeconds += dt;
        if (loading is not { IsCompleted: true } done)
        {
            return;
        }
        loading = null;
        if (!done.IsCompletedSuccessfully)
        {
            Fail(done.Exception?.GetBaseException().Message ?? "the level did not load");
            return;
        }
        play = done.Result;
        if (content.Sounds is { } sounds)
        {
            play.Hear(sounds, owned: false);
        }
        if (content.Controls is { } controls)
        {
            play.Bind(controls);
        }
        ApplyHud();
        playing = new PlaySimulation(host, play, OpenPause);
        playing.Pause();                                                      // an Escape held through the loading does not pause the start
        (level, mode, waiting, started, paused, ending, byDeath, playSeconds, waitSeconds) = (level + 1, ShellMode.Play, true, false, false, null, false, 0, 0);
    }

    private void TickPlay(double dt, MenuInput read)
    {
        if (play is null || playing is null)
        {
            return;
        }
        if (waiting)
        {
            loadingSeconds += dt;
            if (!started)
            {
                started = true;
                playing.Tick(dt);                                            // the level starts: its first scene, for the render thread to show
            }
            else if (playing.Error is { } failed)
            {
                Fail(failed);
            }
            else if (signals.Shown == level || play.Scene?.Definition.Model is not { Length: > 0 } || (waitSeconds += dt) > ShowTimeout)
            {
                waiting = false;
                playing.Pause();                                              // what was held on the loading screen is let go
            }
            return;
        }
        if (paused)
        {
            pause.Update(read, screen);
            content.Sounds?.Update((float)dt, []);
            return;
        }
        playing.Tick(dt);
        if (playing.Error is { } error)
        {
            Fail(error);
            return;
        }
        if (paused || play is null)
        {
            return;                                                           // it paused this tick
        }
        playSeconds += dt;
        TakeEnding();
        StepEnding((float)dt);
    }

    /// <summary>A script's ending (the first), or a defeat when every player is dead, called off if one lives again in time.</summary>
    private void TakeEnding()
    {
        var requests = play!.Requests;
        if ((ending is null || byDeath) && requests.OfType<EndGameRequest>().FirstOrDefault(r => r.Outcome is EndGameCondition.Victory
            or EndGameCondition.LevelComplete or EndGameCondition.Defeat or EndGameCondition.ToBeContinued or EndGameCondition.EndOffGame) is { } end)
        {
            (ending, byDeath) = (new LevelEnding(end.Outcome, 0, end.Phony), false);
        }
        requests.RemoveAll(r => r is EndGameRequest);
        var dead = AllDead(play);
        if (ending is null && dead)
        {
            (ending, byDeath) = (new LevelEnding(EndGameCondition.Defeat, LevelEnding.DeathDelay), true);
        }
        else if (byDeath && ending is { Waiting: true } && !dead)
        {
            (ending, byDeath) = (null, false);
        }
    }

    /// <summary>Every player in the level has a wizard, and every one is dead.</summary>
    public static bool AllDead(LevelPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        var players = play.Session.Players.Where(p => p.Playing).ToList();
        return players.Count > 0 && players.All(p => p.Avatar is { IsDead: true });
    }

    private void StepEnding(float dt)
    {
        if (ending is not { } end)
        {
            return;
        }
        if (end.Step(dt) is { } music)
        {
            content.Sounds?.PlayMusic(SoundBanks.Music, music);
        }
        if (!end.IsOver)
        {
            return;
        }
        (ending, byDeath) = (null, false);
        switch (end.Next)
        {
            case EndingNext.Restart when !play!.Level.RestartAtCheckpoint():
                RestartLevel();                                               // no checkpoint yet: the level from its start
                break;
            case EndingNext.NextChapter:
                CompleteChapter();
                break;
        }
    }

    private void OpenPause()
    {
        if (ending is { Waiting: false, Next: not EndingNext.None })
        {
            return;                                                           // the level is ending: its cover would hide the menu
        }
        paused = true;
        pause.Open(host.Input.Value.Pad.IsButtonDown(Buttons.Start));
        content.Sounds?.PauseAll();
        Cue("ui_menu_increase");
    }

    private void Resume()
    {
        paused = false;
        content.Sounds?.ResumeAll();
        playing?.Pause();
    }

    /// <summary>The level again from its start (loaded anew), its progress gone.</summary>
    private void RestartLevel()
    {
        KeepTime();
        LeaveLevel();
        Play(slot, chapter);
    }

    /// <summary>The level left for the lobby (the slot keeps its chapter: it starts again from there).</summary>
    private void QuitLevel()
    {
        KeepTime();
        LeaveLevel();
        BackToMenu();
    }

    /// <summary>The chapter is done: the slot goes on to the next (after the last, the first, looped), which loads at once.</summary>
    private void CompleteChapter()
    {
        var saved = Saves[slot] ?? new CampaignSlot(chapter, chapter);
        var done = (saved with { Chapter = chapter }).Completed() with { Seconds = saved.Seconds + playSeconds };
        playSeconds = 0;
        Save(done);
        LeaveLevel();
        if (done.Looped && chapter == Chapters.Count - 1)
        {
            BackToMenu();
        }
        else
        {
            Play(slot, done.Chapter);
        }
    }

    /// <summary>The game closes: the time played in the level is kept (the logic thread has stopped).</summary>
    public void Close()
    {
        if (play is not null)
        {
            KeepTime();
        }
    }

    private void KeepTime()
    {
        if (Saves[slot] is { } saved && playSeconds > 0)
        {
            Save(saved with { Seconds = saved.Seconds + playSeconds });
        }
        playSeconds = 0;
    }

    private void Save(CampaignSlot value) => Saves.Set(slot, value);

    private void LeaveLevel()
    {
        play?.Dispose();
        content.Sounds?.ResumeAll();
        content.Sounds?.StopAll(immediate: true);
        (play, playing, paused, ending, byDeath, waiting) = (null, null, false, null, false, false);
    }

    private void BackToMenu()
    {
        mode = ShellMode.Menu;
        menu.Page?.Enter();
        content.Sounds?.PlayMusic(SoundBanks.Music, MenuMusic);
    }

    /// <summary>The level could not load or stopped: back to the menu, with a box saying why.</summary>
    private void Fail(string why)
    {
        Console.Error.WriteLine($"OpenMagicka: the level stopped: {why}");
        KeepTime();
        LeaveLevel();
        BackToMenu();
        menu.Ask(why, [strings.Get("#add_menu_ok")], 0, _ => { });
    }

    private void ApplyHud()
    {
        if (play is not null)
        {
            play.HealthBarOption = Settings.HealthBars;
            play.ShowsDamageNumbers = Settings.DamageNumbers;
            play.ShowsSpellWheel = Settings.SpellWheel;
        }
    }

    private void WritePlay(ShellFrame frame, FrameTiming timing)
    {
        if (playing is null)
        {
            return;
        }
        playing.WriteFrame(frame.Play, timing);
        if (waiting)
        {
            frame.Mode = ShellMode.Loading;
            LoadingScreen.Write(frame.Menu, strings, chapter, tip, loadingSeconds, screen);
            return;
        }
        if (paused)
        {
            pause.Draw(frame.Menu, screen);
        }
        if (ending is { } end)
        {
            var s = Math.Max(screen.Y, 1) / 720f;
            if (end.Word is { } word && end.WordAlpha > 0)
            {
                frame.Menu.ScreenTexts.Add(new MenuText("Stonecross50", strings.Get(word), new Vector2(screen.X / 2f, screen.Y / 2f - 30 * s),
                    TextAlignment.Center, new Vector4(1, 1, 1, end.WordAlpha), s));
            }
            frame.Menu.Cover = end.Cover;
        }
    }
}
