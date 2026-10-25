using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>An achievement was won: the platform is told (OM-55 keeps it in the profile).</summary>
public sealed record AchievementRequest(string Achievement) : ScriptRequest;

/// <summary><c>unlockAchievement achievement=</c>: the achievement is won.</summary>
public sealed class UnlockAchievementAction : ScriptAction
{
    public string Achievement { get; set; } = "";

    protected override void Execute() => Award(Scene, Achievement);

    internal static void Award(ScriptScene scene, string achievement)
    {
        if (scene.Game.Achievements.Add(achievement))
        {
            scene.Stage.Request(new AchievementRequest(achievement));
        }
    }
}

/// <summary>
/// <c>conditionalUnlockAchievement achievement= victim=</c>: one of the few achievements the original knows
/// a rule for: <c>thisismagicka</c> and <c>therecanbeonlyone</c> when the victim's last attacker is a
/// playing wizard, <c>drivenmad</c>, <c>breezedthrough</c> and <c>handlingthefrustration</c> when no player
/// has died on the level. Any other fails the loading (the original throws as it runs).
/// </summary>
public sealed class ConditionalUnlockAchievementAction : ScriptAction
{
    public string Achievement { get; set; } = "";
    public string Victim { get; set; } = "";

    public override void Initialize()
    {
        if (Achievement is not ("thisismagicka" or "therecanbeonlyone" or "drivenmad" or "breezedthrough" or "handlingthefrustration"))
        {
            throw new InvalidDataException($"<conditionalUnlockAchievement achievement=\"{Achievement}\">: no rule for it");
        }
    }

    protected override void Execute()
    {
        var won = Achievement is "thisismagicka" or "therecanbeonlyone"
            ? Scene.World.Entities.FindByUniqueId(Hash(Victim)) is Character { IsPlayer: false, LastAttacker: Character { IsPlayer: true, Player.Playing: true } }
            : !Scene.Game.PlayerDiedInLevel;
        if (won)
        {
            UnlockAchievementAction.Award(Scene, Achievement);
        }
    }
}

/// <summary><c>foundSecretArea id=</c>, <c>foundMoose id=</c> and <c>killedAPony id=</c>: the profile notes it (its achievements count them, OM-55).</summary>
public abstract class ProfileNoteAction(Func<ScriptGame, ISet<string>> notes) : ScriptAction
{
    public string Id { get; set; } = "";

    protected override void Execute() => notes(Scene.Game).Add(Id);
}

public sealed class FoundSecretAreaAction() : ProfileNoteAction(g => g.SecretAreas);

public sealed class FoundMooseAction() : ProfileNoteAction(g => g.Moose);

public sealed class KilledAPonyAction() : ProfileNoteAction(g => g.Ponies);

/// <summary>What wizardControl does.</summary>
public enum WizardAction { StaffLightOn, StaffLightOff }

/// <summary><c>wizardControl action=</c>: the wizards' staffs light up, or go dark.</summary>
public sealed class WizardControlAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public WizardAction Action { get; set; }

    protected override void Execute() => Scene.Game.StaffLight = Action == WizardAction.StaffLightOn;
}

/// <summary><c>beginDefaultAvatarSection</c> and <c>endDefaultAvatarSection</c>: a stretch played as the default wizard begins or ends.</summary>
public abstract class DefaultAvatarSectionAction(bool begins) : ScriptAction
{
    protected override void Execute() => Scene.Game.DefaultAvatarSection = begins;
}

public sealed class BeginDefaultAvatarSectionAction() : DefaultAvatarSectionAction(begins: true);

public sealed class EndDefaultAvatarSectionAction() : DefaultAvatarSectionAction(begins: false);

/// <summary><c>beginUnequipWeaponsSection</c>: a stretch with no weapons in hand begins.</summary>
public sealed class BeginUnequipWeaponsSectionAction : ScriptAction
{
    protected override void Execute() => Scene.Game.UnequipWeaponsSection = true;
}

/// <summary><c>endUnequipWeaponsSection animation=</c>: the stretch ends; every avatar (living or not) plays the attack clip given.</summary>
public sealed class EndUnequipWeaponsSectionAction : ScriptAction
{
    public string Animation { get; set; } = "";

    protected override void Execute()
    {
        Scene.Game.UnequipWeaponsSection = false;
        if (Animation is "" or "none")
        {
            return;
        }
        foreach (var avatar in (Scene.World.Session?.Players ?? []).Select(p => p.Avatar).OfType<Character>())
        {
            avatar.PlayAttack(Animation, allowRotate: true);
        }
    }
}

/// <summary>
/// <c>releaseGrip id= | area= type= factions=</c> (never used by the game): the grabbed would be let go by the
/// named NPC or those in the area; the grips themselves wait for OM-33, so nothing is held yet.
/// </summary>
public sealed class ReleaseGripAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public Factions Factions { get; set; }
    public string Id { get; set; } = "";
    public string Area { get; set; } = "";
    public string Type { get; set; } = "";

    protected override void Execute()
    {
    }
}

/// <summary>
/// <c>physAnimation id= name= speed= start= end= children=</c> (never used by the game): it would bind an animated
/// part's clip to a breakable prop; ours binds none (left out, LEVELS_TRIGGERS.md §9.5).
/// </summary>
public sealed class PhysAnimationAction : ScriptAction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float Speed { get; set; } = 1;
    public float Start { get; set; }
    public float End { get; set; }
    public bool Children { get; set; } = true;

    protected override void Execute()
    {
    }
}
