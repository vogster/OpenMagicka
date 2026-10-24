namespace OpenMagicka.Engine.Scripting;

/// <summary>A boss fight under way, as the scripts see it.</summary>
public interface IScriptBoss
{
    bool Dead { get; }

    /// <summary>The boss's hit points as a share, 0..1.</summary>
    float NormalizedHealth { get; }
}

/// <summary>The dialogs, as the scripts see them (by the hash of the dialog's lowered name).</summary>
public interface IScriptDialogs
{
    /// <summary>Whether a message box of any dialog is up.</summary>
    bool MessageBoxActive { get; }

    bool IsActive(int dialog);

    /// <summary>Whether the dialog has finished, past the interact (−1: any).</summary>
    bool IsDone(int dialog, int interactIndex);
}

/// <summary>The downloadable packs the scripts ask about.</summary>
public enum Dlc { WizardHat, Vietnam }

/// <summary>
/// What the scripts read of the game beyond the scene's world (LEVELS_TRIGGERS.md §10): the play
/// state, the boss fight, the dialogs and the player's profile. The systems that own them keep it
/// up to date; until they exist, nothing is shown, done, owned or won.
/// </summary>
public sealed class ScriptGame
{
    public bool InCutscene { get; set; }

    /// <summary>A player has died on this level.</summary>
    public bool PlayerDiedInLevel { get; set; }

    /// <summary>The boss fight; none outside one.</summary>
    public IScriptBoss? Boss { get; set; }

    /// <summary>The dialogs; none, so none is up or done.</summary>
    public IScriptDialogs? Dialogs { get; set; }

    /// <summary>The tutorial's dialog hint has been closed.</summary>
    public bool DialogHintDone { get; set; }

    /// <summary>The achievements won, by name in any case.</summary>
    public ISet<string> Achievements { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ISet<Dlc> InstalledDlc { get; } = new HashSet<Dlc>();
}
