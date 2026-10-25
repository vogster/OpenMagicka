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

    /// <summary>The dialogs' state for a checkpoint (none to keep by default).</summary>
    object? Save() => null;

    /// <summary>Back to a checkpoint's state (every dialog ends first).</summary>
    void Restore(object? saved)
    {
    }
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

    /// <summary>The tutorial's locks and hints.</summary>
    public ScriptTutorial Tutorial { get; } = new();

    /// <summary>The boss fight the scripts set up (the game's one, as the original's); <see cref="Boss"/> is it once a boss joins.</summary>
    public ScriptBossFight BossFight { get; } = new();

    /// <summary>The level's generic bar (a timer, a counter).</summary>
    public ScriptHealthBar HealthBar { get; } = new();

    /// <summary>The secret areas found, the moose found and the ponies killed, by name (the profile keeps them, OM-55).</summary>
    public ISet<string> SecretAreas { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ISet<string> Moose { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ISet<string> Ponies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The wizards' staffs give light (dark levels: the Mines, Niflheim).</summary>
    public bool StaffLight { get; set; }

    /// <summary>A stretch played as the default wizard (a level's own look stands aside).</summary>
    public bool DefaultAvatarSection { get; set; }

    /// <summary>A stretch where the wizards hold no weapons.</summary>
    public bool UnequipWeaponsSection { get; set; }
}
