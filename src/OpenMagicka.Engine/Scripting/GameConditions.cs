using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary><c>dialogDone dialog= interactIndex=</c>: the dialog has finished, past the interact (−1: any).</summary>
public sealed class DialogDoneCondition : ScriptCondition
{
    public string Dialog { get; set; } = "";

    public int InteractIndex { get; set; } = -1;

    protected override bool Holds(Character? sender) => Scene.Game.Dialogs?.IsDone(Hash(Dialog), InteractIndex) ?? false;
}

/// <summary><c>dialogActive dialog=</c>: the dialog is shown; "any" or none: any message box is up.</summary>
public sealed class DialogActiveCondition : ScriptCondition
{
    public string Dialog { get; set; } = "";

    protected override bool Holds(Character? sender) => Scene.Game.Dialogs is { } dialogs
        && (Dialog is "" or "any" ? dialogs.MessageBoxActive : dialogs.IsActive(Hash(Dialog)));
}

/// <summary><c>dialogHintDone</c>: the tutorial's dialog hint has been closed.</summary>
public sealed class DialogHintDoneCondition : ScriptCondition
{
    protected override bool Holds(Character? sender) => Scene.Game.Tutorial.DialogHint is null;
}

/// <summary>A condition on the boss fight: with none under way it does not hold, inverted or not (as the original).</summary>
public abstract class BossCondition : ScriptCondition
{
    public override bool IsMet(Character? sender) => Scene.Game.Boss is not null && base.IsMet(sender);

    protected sealed override bool Holds(Character? sender) => Holds(Scene.Game.Boss!);

    protected abstract bool Holds(IScriptBoss boss);
}

/// <summary><c>bossHealth compareMethod= value=</c>: the boss's share of hit points; equal is within 0.000001.</summary>
public sealed class BossHealthCondition : BossCondition
{
    public CompareMethod CompareMethod { get; set; }

    public float Value { get; set; }

    protected override bool Holds(IScriptBoss boss) => CompareMethod switch
    {
        CompareMethod.Less => boss.NormalizedHealth < Value,
        CompareMethod.Equal => MathF.Abs(boss.NormalizedHealth - Value) <= 1e-6f,
        CompareMethod.Greater => boss.NormalizedHealth > Value,
        _ => false,
    };
}

/// <summary><c>bossDead</c>: the boss is dead.</summary>
public sealed class BossDeadCondition : BossCondition
{
    protected override bool Holds(IScriptBoss boss) => boss.Dead;
}

/// <summary><c>noCutsceneActive</c>: no cutscene plays.</summary>
public sealed class NoCutsceneActiveCondition : ScriptCondition
{
    protected override bool Holds(Character? sender) => !Scene.Game.InCutscene;
}

/// <summary><c>hasAchievement name=</c>: the player has won the achievement.</summary>
public sealed class HasAchievementCondition : ScriptCondition
{
    public string Name { get; set; } = "";

    protected override bool Holds(Character? sender) => Scene.Game.Achievements.Contains(Name);
}

/// <summary><c>anyPlayerHasDied</c>: a player has died on this level.</summary>
public sealed class AnyPlayerHasDiedCondition : ScriptCondition
{
    protected override bool Holds(Character? sender) => Scene.Game.PlayerDiedInLevel;
}

/// <summary><c>hasDLC type=</c>: the pack is installed.</summary>
public sealed class HasDlcCondition : ScriptCondition
{
    public Dlc Type { get; set; }

    protected override bool Holds(Character? sender) => Scene.Game.InstalledDlc.Contains(Type);
}

/// <summary>
/// <c>isCampaignLooped</c>: the campaign is being played again (the scene is looped). Outside a
/// campaign (or with no game) the original crashes; here it does not hold.
/// </summary>
public sealed class IsCampaignLoopedCondition : ScriptCondition
{
    protected override bool Holds(Character? sender) => Scene.World.Session?.Type == GameType.Campaign && Scene.Looped;
}
