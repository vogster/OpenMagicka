using OpenMagicka.Engine.Scripting;

namespace OpenMagicka.Game.Shell;

/// <summary>What the game does once a level's ending has played out.</summary>
public enum EndingNext : byte
{
    /// <summary>The level goes on (a phony ending only shows).</summary>
    None,

    /// <summary>The chapter is done: the next one.</summary>
    NextChapter,

    /// <summary>The players lost: back to the last checkpoint.</summary>
    Restart,
}

/// <summary>
/// A level's ending (OM-53, docs/engine/ARCHITECTURE.md §20), as the original times it: after its delay (3 s for a
/// defeat by death, none for a script's), its word fades in over a second with its music, stays, and fades out from
/// 2 s on; at 3 s it is over. A defeat is not covered (the restart fades the screen out and in after it, OM-67); a
/// chapter's end is, in that last second, unless it goes to the book's end page; "to be continued" is an 8 s fade to
/// black without a word. A defeat by death is called off while its delay runs if a player lives again.
/// </summary>
public sealed class LevelEnding(EndGameCondition outcome, float delay = 0, bool phony = false)
{
    public const float DeathDelay = 3, FadeIn = 1, Hold = 2, Over = 3, ContinuedFade = 8;

    private float timer = Math.Max(0, delay);
    private bool cued;

    public EndGameCondition Outcome { get; } = outcome;

    public bool Phony { get; } = phony;

    /// <summary>The word shown (a string key), or none.</summary>
    public string? Word => Outcome switch
    {
        EndGameCondition.Victory => "#add_game_victory",
        EndGameCondition.LevelComplete => "#add_game_level_complete",
        EndGameCondition.Defeat => "#add_game_defeated",
        _ => null,
    };

    /// <summary>The music played when the word comes (in the Music bank), or none.</summary>
    public string? Music => Outcome switch
    {
        EndGameCondition.Victory or EndGameCondition.LevelComplete => "music_fanfare",
        EndGameCondition.Defeat => "music_defeat",
        _ => null,
    };

    /// <summary>What follows: a phony ending nothing; a defeat the checkpoint; any other the next chapter.</summary>
    public EndingNext Next => Phony ? EndingNext.None : Outcome == EndGameCondition.Defeat ? EndingNext.Restart : EndingNext.NextChapter;

    /// <summary>The delay is still running (a defeat by death can be called off).</summary>
    public bool Waiting => timer > 0;

    public float WordAlpha { get; private set; }

    /// <summary>The chapter's end goes to the book's end page (OM-83), which shows the level's last frame: not covered.</summary>
    public bool ToEndPage { get; init; }

    /// <summary>How much of the screen is covered (black).</summary>
    public float Cover => Next is EndingNext.None or EndingNext.Restart || ToEndPage && Next == EndingNext.NextChapter && Outcome != EndGameCondition.ToBeContinued ? 0
        : Outcome == EndGameCondition.ToBeContinued ? Math.Clamp(-timer / ContinuedFade, 0, 1)
        : Math.Clamp(-timer - Hold, 0, 1);

    public bool IsOver => Outcome == EndGameCondition.ToBeContinued ? -timer >= ContinuedFade : -timer >= Over && WordAlpha <= 0;

    /// <summary>A step; the music to start now (once, when the word comes), or none.</summary>
    public string? Step(float dt)
    {
        timer -= Math.Max(0, dt);
        if (timer > 0)
        {
            return null;
        }
        WordAlpha = Word is null ? 0 : -timer < Hold ? Math.Min(1, WordAlpha + dt / FadeIn) : Math.Max(0, WordAlpha - dt);
        if (cued)
        {
            return null;
        }
        cued = true;
        return Music;
    }
}
