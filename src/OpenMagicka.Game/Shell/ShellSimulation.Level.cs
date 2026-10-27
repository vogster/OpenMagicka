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
    private double? capturing;
    private bool restarting;
    private float restartSeconds;

    /// <summary>A defeat's restart waits for the screen to go black (OM-67).</summary>
    public bool Restarting => restarting;

    /// <summary>The fade out before a defeat's restart (the restart fades back in as long), as the original's.</summary>
    public const float RestartFade = 0.5f;
    private int level, slot, chapter, tip = 1;
    private bool resume;
    private Narration? narration;
    private string? told;

    /// <summary>How long a level waits at most for the loading screen's narration (the intro's is 16 s), should its cue never end.</summary>
    public const double NarrationLimit = 30;
    private double loadingSeconds, waitSeconds, playSeconds;

    /// <summary>How long a started level waits for the render thread to show it before it runs anyway.</summary>
    public const double ShowTimeout = 60;
    private bool waiting, started, paused, byDeath;

    /// <summary>The level at play, or none.</summary>
    public LevelPlay? Level => play;

    public bool Paused => paused;

    public LevelEnding? Ending => ending;

    /// <summary>
    /// A chapter is started from a slot: it loads in the background (the music playing on till it starts, as the
    /// original's); with <paramref name="resume"/> (Continue) at the slot's saved checkpoint, else from its start with
    /// that checkpoint kept as the restart point; with a narration, the loading screen tells it and the level waits.
    /// </summary>
    public void Play(int slot, int chapter, bool resume = false, Narration? narration = null)
    {
        if (slot is < 0 or >= CampaignSaves.Slots || chapter < 0 || chapter >= Chapters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(chapter), $"no chapter {chapter} in slot {slot}");
        }
        (this.slot, this.chapter, this.resume, this.narration, told, tip, loadingSeconds, mode, shotShown) =
            (slot, chapter, resume, narration, narration?.TextKey, Random.Shared.Next(1, LoadingScreen.Tips + 1), 0, ShellMode.Loading, 0);
        var language = Settings.Language;
        loading = Task.Run(() => Open(chapter, language));
    }

    private LevelPlay Open(int chapter, string language)
    {
        var files = content.Files;
        var path = PlayGame.FindLevel(files, Chapters.Levels[chapter]) ?? throw new FileNotFoundException($"the game has no level {Chapters.Levels[chapter]}");
        var definition = LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);
        var play = LevelPlay.Open(definition, files, content.Loader, language);
        play.Hashes = (SavedCheckpoint.LevelHashOf(files, path, definition), SavedCheckpoint.DialogHashOf(files, definition));
        return play;
    }

    private void TickLoading(double dt)
    {
        loadingSeconds += dt;
        if (loading is not { IsCompleted: true } done || (narration?.Cue is { IsStopped: false } && loadingSeconds < NarrationLimit))
        {
            return;                                                           // ready, the level waits for the narration's end
        }
        (loading, narration) = (null, null);
        content.Sounds?.StopMusic();
        if (!done.IsCompletedSuccessfully)
        {
            Fail(done.Exception?.GetBaseException().Message ?? "the level did not load");
            return;
        }
        play = done.Result;
        if (Saves.Checkpoint(slot) is { } saved)
        {
            play.Resume = (saved, resume);
        }
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
                (waiting, told) = (false, null);
                playing.Pause();                                              // what was held on the loading screen is let go
            }
            return;
        }
        if (paused)
        {
            pause.Update(read, screen);
            play?.Transitions.Step((float)dt);                                 // the screen's transition runs on through the pause, as the original's
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
        WriteCheckpoint();
        TakeEnding();
        StepEnding((float)dt);
    }

    /// <summary>
    /// A checkpoint the scripts took to keep on disk (all of the game's do): the slot keeps it, and the time played so
    /// far. One that cannot be written is said, and play goes on.
    /// </summary>
    private void WriteCheckpoint()
    {
        if (!play!.Requests.OfType<CheckpointRequest>().Any(r => r.SaveToDisk))
        {
            return;
        }
        try
        {
            if (play.SaveCheckpoint() is { } saved)
            {
                KeepTime();
                Saves.SetCheckpoint(slot, saved);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.Text.Json.JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"OpenMagicka: the checkpoint was not saved ({e.Message})");
        }
    }

    /// <summary>A script's ending (the first), or a defeat when every player is dead, called off if one lives again in time.</summary>
    private void TakeEnding()
    {
        var requests = play!.Requests;
        if (restarting)
        {
            requests.RemoveAll(r => r is EndGameRequest);                     // the run is over: none taken into the next
            return;
        }
        if ((ending is null || byDeath) && requests.OfType<EndGameRequest>().FirstOrDefault(r => r.Outcome is EndGameCondition.Victory
            or EndGameCondition.LevelComplete or EndGameCondition.Defeat or EndGameCondition.ToBeContinued or EndGameCondition.EndOffGame) is { } end)
        {
            (ending, byDeath) = (new LevelEnding(end.Outcome, 0, end.Phony) { ToEndPage = Book is not null }, false);
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
        if (restarting)
        {
            restartSeconds += dt;
            // Out (or a script cleared or replaced it; ours: never held past a little more than its time): the checkpoint.
            if (play!.Transitions.IsCovered || !play.Transitions.IsActive || restartSeconds > RestartFade + 0.5f)
            {
                restarting = false;
                if (!play.RestartAtCheckpoint())
                {
                    RestartLevel();                                           // no checkpoint yet: the level from its start
                }
            }
            return;
        }
        if (capturing is { } waited)
        {
            capturing = waited + dt;
            if (signals.Captured == level || capturing > CaptureTimeout)
            {
                capturing = null;
                CompleteChapter(endPage: signals.Captured == level);          // without a shot in time, straight to the map
            }
            return;
        }
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
            case EndingNext.Restart:
                play!.Transitions.Begin(Engine.Screen.TransitionKind.Fade, System.Numerics.Vector3.Zero, RestartFade);
                (restarting, restartSeconds) = (true, 0);                     // the restart once the screen is black
                break;
            case EndingNext.NextChapter when end.ToEndPage && end.Outcome != EndGameCondition.ToBeContinued && Book is not null:
                capturing = 0;                                                // the level's last frame first, for the end page
                break;
            case EndingNext.NextChapter:
                CompleteChapter();
                break;
        }
    }

    private void OpenPause()
    {
        if (ending is { Waiting: false, Next: not EndingNext.None } || capturing is not null || restarting)
        {
            return;                                                           // the level is ending (or going back): no pause over it
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

    /// <summary>How long a chapter's end waits for its last frame to be kept (seconds).</summary>
    private const double CaptureTimeout = 1;

    /// <summary>
    /// The chapter is done: the slot goes on to the next (after the last, the first, looped). With the book and the
    /// level's last frame, the end page shows it (the book cross-fading in from it, the camera pulling back) and then
    /// riffles to the next chapter's map; else the map comes at once.
    /// </summary>
    private void CompleteChapter(bool endPage = false)
    {
        var saved = Saves[slot] ?? new CampaignSlot(chapter, chapter);
        var done = (saved with { Chapter = chapter }).Completed() with { Seconds = saved.Seconds + playSeconds };
        playSeconds = 0;
        Save(done);
        Saves.SetCheckpoint(slot, null);                                      // ours: the original leaves it, for the next chapter's hash to pass over
        LeaveLevel();
        BackToMenu();
        int? next = !done.Looped || chapter != Chapters.Count - 1 ? done.Chapter : null;
        if (endPage && Book is { } book)
        {
            book.EndLevel();
            menu.PushInstantly(new EndPage(menu, slot, next));
            shotShown = 1;
        }
        else if (next is { } chapterNext)
        {
            CutscenePages.Begin(menu, slot, chapterNext, resume: true);      // the next chapter's map, over the lobby
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
        (play, playing, paused, ending, byDeath, waiting, capturing, restarting) = (null, null, false, null, false, false, null, false);
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

    /// <summary>The loading screen fades in from black over half a second, as the original's.</summary>
    private float LoadingAlpha => (float)Math.Min(1, loadingSeconds / 0.5);

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
        frame.CaptureShot = capturing is not null && !paused;
        if (waiting)
        {
            frame.Mode = ShellMode.Loading;
            LoadingScreen.Write(frame.Menu, strings, chapter, tip, loadingSeconds, screen, LoadingAlpha, told);
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
