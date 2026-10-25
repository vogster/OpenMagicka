namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A cutscene's bars and skip prompt, as the original's (docs/engine/ARCHITECTURE.md §18): the bars slide in over a second
/// while the level is in a cutscene (at once for one replayed from a checkpoint) and out over a second after it; the
/// prompt fades in at 2 a second once a press asked for it (only with a skip trigger) and out again; it is no longer
/// asked for once the bars are gone. The level counts as in a cutscene while the bars are fully in, the frame after its
/// end too.
/// </summary>
public sealed class CutsceneBars
{
    public const float SlideTime = 1, PromptFade = 2;

    /// <summary>How far the bars are in: 0 out, 1 in.</summary>
    public float Amount { get; private set; }

    /// <summary>How much the skip prompt shows.</summary>
    public float Prompt { get; private set; }

    /// <summary>In a cutscene, or its bars still fully in.</summary>
    public bool IsInCutscene(ScriptGame game) => game.InCutscene || Amount >= 1;

    public void Update(float dt, ScriptGame game, ScriptStage stage)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(stage);
        if (game.InCutscene)
        {
            if (stage.CutsceneSkipsBarMove && Amount == 0)
            {
                Amount = 1;                                                   // a replayed cutscene starts with its bars in
            }
            Prompt = stage.CutsceneSkipTrigger != 0 && stage.ShowsSkipTip ? MathF.Min(Prompt + PromptFade * dt, 1) : MathF.Max(Prompt - PromptFade * dt, 0);
            Amount = MathF.Min(Amount + dt / SlideTime, 1);
        }
        else if (Amount > 0)
        {
            Amount = MathF.Max(Amount - dt / SlideTime, 0);
            if (stage.ShowsSkipTip)
            {
                Prompt = MathF.Max(Prompt - dt, 0);
            }
        }
        else
        {
            Prompt = 0;
            stage.ShowsSkipTip = false;
        }
    }
}
