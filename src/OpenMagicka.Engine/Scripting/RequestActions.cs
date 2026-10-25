using System.Globalization;
using System.Numerics;
using System.Xml;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells.Magicks;

namespace OpenMagicka.Engine.Scripting;

/// <summary>A cue written "bank/cue" or "cue" (the bank given), as the original reads it; any other form fails the loading.</summary>
internal static class ScriptCues
{
    public static (SoundBanks Bank, string Cue) Read(string text, SoundBanks bank, string tag) => text.Split('/') switch
    {
        [var cue] => (bank, cue),
        [var named, var cue] when Enum.TryParse<SoundBanks>(named, ignoreCase: true, out var found) && Enum.IsDefined(found) && !int.TryParse(named, out _)
            => (found, cue),
        _ => throw new InvalidDataException($"<{tag} cue=\"{text}\">: not \"bank/cue\""),
    };
}

/// <summary>
/// <c>playSound cue= id= area= volume= radius= apply3D=</c>: a cue (the Ambience bank unless named); with
/// an id a scene sound kept to stop later; at a place a 3D one. The volume is the cue's Volume variable.
/// </summary>
public sealed class PlaySoundAction : ScriptAction
{
    private (SoundBanks Bank, string Cue) cue;

    public string Cue { get; set; } = "";
    public string Id { get; set; } = "";
    public string Area { get; set; } = "";
    public float Volume { get; set; }
    public float Radius { get; set; }
    public bool Apply3D { get; set; }

    public override void Initialize() => cue = ScriptCues.Read(Cue, SoundBanks.Ambience, "playSound");

    protected override void Execute() => Scene.Stage.Request(new PlaySoundRequest(Id.Length > 0 ? Hash(Id) : 0, cue.Bank, cue.Cue, Volume,
        Area.Length > 0 ? Scene.Places.Placement(Hash(Area), Scene.Random).Translation : null, Radius, Apply3D));
}

/// <summary><c>stopSound id= instant=</c>: the named scene sound stops (at once, with no tail, when instant).</summary>
public sealed class StopSoundAction : ScriptAction
{
    public string Id { get; set; } = "";
    public bool Instant { get; set; }

    protected override void Execute() => Scene.Stage.Request(new StopSoundRequest(Hash(Id), Instant));
}

/// <summary><c>playMusic cue= focusValue=</c>: the music (the Music bank unless named).</summary>
public sealed class PlayMusicAction : ScriptAction
{
    private (SoundBanks Bank, string Cue) cue;

    public string Cue { get; set; } = "";
    public float FocusValue { get; set; }

    public override void Initialize() => cue = ScriptCues.Read(Cue, SoundBanks.Music, "playMusic");

    protected override void Execute() => Scene.Stage.Request(new PlayMusicRequest(cue.Bank, cue.Cue, FocusValue));
}

/// <summary>
/// <c>startDialog dialog= position= forceOnScreen=</c>: the dialog's next interact, over the character
/// that set the trigger off ("interactor", the default), a named character, a locator, a screen point
/// "x,y" or a level point "x,y,z" (read as the original: its z goes in for y, and z stays 0).
/// </summary>
public sealed class StartDialogAction : ScriptAction
{
    private readonly Queue<Character?> senders = new();
    private Vector2? onScreen;
    private Vector3? at;

    public string Dialog { get; set; } = "";
    public string Position { get; set; } = "interactor";
    public bool ForceOnScreen { get; set; }

    public override void Initialize()
    {
        var parts = Position.Split(',');
        try
        {
            if (parts.Length == 2)
            {
                onScreen = new Vector2(Number(parts[0]), Number(parts[1]));
            }
            else if (parts.Length == 3)
            {
                at = new Vector3(Number(parts[0]), Number(parts[2]), 0);       // as the original: y is overwritten by z
            }
        }
        catch (FormatException e)
        {
            throw new InvalidDataException($"<startDialog position=\"{Position}\">: {e.Message}", e);
        }
    }

    public override void OnTrigger(Character? sender)
    {
        base.OnTrigger(sender);
        senders.Enqueue(sender);
    }

    protected override void Execute()
    {
        var sender = senders.Count > 0 ? senders.Dequeue() : null;
        var dialog = Hash(Dialog);
        if (onScreen is not null || at is not null)
        {
            Scene.Stage.Request(new StartDialogRequest(dialog, null, at, onScreen, ForceOnScreen));
            return;
        }
        var speaker = Position == "interactor" ? sender : Scene.World.Entities.FindByUniqueId(Hash(Position)) as Character;
        var point = speaker is null ? Scene.Places.Placement(Hash(Position), Scene.Random).Translation : (Vector3?)null;
        Scene.Stage.Request(new StartDialogRequest(dialog, speaker, point, null, ForceOnScreen));
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}

/// <summary>
/// <c>triggerCheckpoint spawnPoint= spawnFairy= saveToDisk=</c> with <c>&lt;ignore trigger=/&gt;</c> children:
/// the level's checkpoint (OM-47) at the spawn points (the name and 0..3, placed when the scene is built),
/// and the players made whole: the living healed (twice over with the Staff of War), the dead raised at
/// their spawn point. The fairy comes for a lone player outside Versus. Any other child fails the loading.
/// </summary>
public sealed class TriggerCheckpointAction : ScriptAction
{
    private readonly List<int> ignore = [];
    private readonly Matrix4x4[] spawnPoints = new Matrix4x4[4];

    public string SpawnPoint { get; set; } = "";
    public bool SpawnFairy { get; set; } = true;
    public bool SaveToDisk { get; set; } = true;

    public override void Initialize()
    {
        ignore.Clear();
        foreach (var child in Node?.Children.Where(c => c.Type == XmlNodeType.Element) ?? [])
        {
            if (!child.Is("ignore"))
            {
                throw new InvalidDataException($"<triggerCheckpoint>: \"{child.Name}\" is not <ignore>");
            }
            if (child.Attribute("trigger") is { } trigger)
            {
                ignore.Add(Hash(trigger));
            }
        }
        for (var i = 0; i < spawnPoints.Length; i++)
        {
            spawnPoints[i] = Scene.Places.Placement(Hash(SpawnPoint + i), Scene.Random);
        }
    }

    protected override void Execute()
    {
        var session = Scene.World.Session;
        var players = session?.Players ?? [];
        var fairy = SpawnFairy && players.Count(p => p.Playing) == 1 && session?.Type != GameType.Versus;
        Scene.Stage.Request(new CheckpointRequest([.. spawnPoints], [.. ignore], SaveToDisk, fairy));
        Scene.Stage.Request(new PlaySoundRequest(0, SoundBanks.UI, "ui_checkpoint01", 1, null, 0, false));
        foreach (var player in players.Where(p => p.Playing && p.Id < spawnPoints.Length))
        {
            if (player.Avatar is not { } avatar)
            {
                continue;
            }
            if (!avatar.IsDead)
            {
                var staffOfWar = string.Equals(avatar.HeldItem(Character.ResistanceSlot)?.Template.Id, "staff_of_war", StringComparison.OrdinalIgnoreCase);
                avatar.SetHealth(staffOfWar ? 2 : 1);
            }
            else if (Scene.IsHost)
            {
                Revive.RaiseAt(Scene.World.Entities, Scene.World.Physics, player, spawnPoints[player.Id].Translation);
            }
        }
    }
}

/// <summary>
/// <c>changeScene scene= spawnPoint= transition= transitionTime= spawnPlayers= saveNPCs=</c>: the level goes to
/// another scene (OM-47), the players at the spawn point's locators (its name and 0..3). A network client
/// waits for the host's.
/// </summary>
public sealed class ChangeSceneAction : ScriptAction
{
    /// <summary>The scene to go to (the attribute's name hides the base's scene; that one is <c>base.Scene</c>).</summary>
    public new string Scene { get; set; } = "";
    public string SpawnPoint { get; set; } = "";
    public ScriptTransition Transition { get; set; } = ScriptTransition.Fade;
    public float TransitionTime { get; set; } = 1;
    public bool SpawnPlayers { get; set; }
    public bool SaveNpcs { get; set; }

    protected override void Execute()
    {
        if (base.Scene.IsHost)
        {
            base.Scene.Stage.Request(new ChangeSceneRequest(Scene, [.. Enumerable.Range(0, 4).Select(i => Hash(SpawnPoint + i))], Transition,
                TransitionTime, SpawnPlayers, SaveNpcs));
        }
    }
}

/// <summary>
/// <c>enterScene scene= spawnPoint= transition= transitionTime= saveNPCs= moveTarget= facingDirection= trigger=
/// ignoreTriggers= instantTrigger=</c>: a scene change that brings the players in; once there, each playing
/// avatar walks to the locator <c>moveTarget</c> and its number (0..3), the trigger <c>trigger</c> set off on
/// arrival, unseen by the trigger areas meanwhile with <c>ignoreTriggers</c>; then <c>instantTrigger</c>
/// (of the new scene) is set off, its conditions checked.
/// </summary>
public sealed class EnterSceneAction : ScriptAction
{
    /// <summary>The scene to go to (the attribute's name hides the base's scene; that one is <c>base.Scene</c>).</summary>
    public new string Scene { get; set; } = "";
    public string SpawnPoint { get; set; } = "";
    public ScriptTransition Transition { get; set; } = ScriptTransition.Fade;
    public float TransitionTime { get; set; } = 1;
    public bool SaveNpcs { get; set; }
    public string MoveTarget { get; set; } = "";
    public bool FacingDirection { get; set; }
    public string Trigger { get; set; } = "";
    public bool IgnoreTriggers { get; set; }
    public string InstantTrigger { get; set; } = "";

    protected override void Execute()
    {
        if (base.Scene.IsHost)
        {
            base.Scene.Stage.Request(new ChangeSceneRequest(Scene, [.. Enumerable.Range(0, 4).Select(i => Hash(SpawnPoint + i))], Transition,
                TransitionTime, SpawnPlayers: true, SaveNpcs, Entered));
        }
    }

    private void Entered(ScriptScene entered)
    {
        if (MoveTarget.Length > 0)
        {
            foreach (var player in entered.World.Session?.Players ?? [])
            {
                if (player is { Playing: true, Avatar: { } avatar, Id: < 4 } && entered.Places.LocatorTransform(Hash(MoveTarget + player.Id)) is { } target)
                {
                    avatar.PlayEvents([new Gameplay.AI.MoveAiEvent(Trigger.Length > 0 ? Hash(Trigger) : 0, 0, target.Translation, 1, FacingDirection,
                        ScriptPlaces.Forward(target))]);
                    avatar.IgnoresTriggers = IgnoreTriggers;
                }
            }
        }
        if (InstantTrigger.Length > 0)
        {
            entered.ExecuteTrigger(Hash(InstantTrigger), null);
        }
    }
}
