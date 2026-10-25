using System.Numerics;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>How a scene change goes over (the screen fades, or crosses to the next).</summary>
public enum ScriptTransition { None, Fade, CrossFade }

/// <summary>
/// What a script asks of a system beyond the world (LEVELS_TRIGGERS.md §9.3): the sound (OM-51), the
/// dialogs (OM-49), the scene changes and checkpoints (OM-47). The host takes them each frame.
/// </summary>
public abstract record ScriptRequest;

/// <summary>A cue of a bank; with a name (its id) a scene sound kept to stop or tune later; at a place, a 3D one.</summary>
public sealed record PlaySoundRequest(int Id, SoundBanks Bank, string Cue, float Volume, Vector3? At, float Radius, bool Apply3D) : ScriptRequest;

public sealed record StopSoundRequest(int Id, bool Instant) : ScriptRequest;

public sealed record PlayMusicRequest(SoundBanks Bank, string Cue, float Focus) : ScriptRequest;

/// <summary>
/// A dialog's next interact, shown over a speaker, at a point in the level, or at a point on the screen
/// (0..1); none of them: at the level's origin.
/// </summary>
public sealed record StartDialogRequest(int Dialog, Character? Speaker, Vector3? At, Vector2? OnScreen, bool ForceOnScreen) : ScriptRequest;

/// <summary>Every dialog ends (a cutscene's start or end).</summary>
public sealed record EndDialogsRequest : ScriptRequest;

/// <summary>
/// A checkpoint (OM-47): the players' spawn points, the triggers it leaves out, whether it is saved to
/// disk and whether the fairy comes; the "checkpoint" text and sound go with it.
/// </summary>
public sealed record CheckpointRequest(IReadOnlyList<Matrix4x4> SpawnPoints, IReadOnlyList<int> Ignore, bool SaveToDisk, bool SpawnFairy) : ScriptRequest;

/// <summary>A move to another scene of the level (OM-47): the players at the locators of the spawn point (its name and 0..3).</summary>
public sealed record ChangeSceneRequest(string Scene, IReadOnlyList<int> SpawnPoints, ScriptTransition Transition, float Time, bool SpawnPlayers, bool SaveNpcs)
    : ScriptRequest;

/// <summary>The level left one scene for another (the sound pauses the old scene's sounds when its NPCs were kept, else stops them).</summary>
public sealed record SceneChangedRequest(int From, int To, bool Kept) : ScriptRequest;

/// <summary>A scene change's screen transition: out over the time, the change, then back in.</summary>
public sealed record SceneTransition(ScriptTransition Kind, float Time, bool Out);

/// <summary>A screen fade: in (to the colour) or out, over a time; an override cancels the scene change's own.</summary>
public sealed record ScreenFade(bool In, Vector4 Color, float Time, bool Override);

/// <summary>
/// The scene's stage (§9.3): the camera the scripts move, the screen's fade, the cutscene, the input
/// locks, and the requests for the systems beyond the world. The game's loop steps the camera with
/// the players and draws the rest.
/// </summary>
public sealed class ScriptStage
{
    /// <summary>A lock lifted keeps input back a little longer, as the original.</summary>
    public const float UnlockDelay = 0.1f;

    private readonly Queue<ScriptRequest> requests = new();
    private readonly HashSet<object> locks = new(ReferenceEqualityComparer.Instance);
    private float unlockDelay;

    public GameCamera Camera
    {
        get => camera;
        set => camera = value ?? throw new ArgumentNullException(nameof(value));
    }

    private GameCamera camera = GameCamera.Initial;

    public ScreenFade? Fade { get; internal set; }

    /// <summary>The scene change under way, for the renderer's transition; none when there is none.</summary>
    public SceneTransition? Transition { get; internal set; }

    /// <summary>The HUD shows (a cutscene hides it).</summary>
    public bool IsUiEnabled { get; internal set; } = true;

    /// <summary>The trigger a skip of the cutscene sets off (0: it cannot be skipped).</summary>
    public int CutsceneSkipTrigger { get; internal set; }

    /// <summary>The cutscene's bars move in (or out) at once.</summary>
    public bool CutsceneSkipsBarMove { get; internal set; }

    public bool IsCutsceneSkipped { get; internal set; }

    /// <summary>A skip asked once shows the tip; the next one skips. The tip goes when the cutscene ends.</summary>
    public bool ShowsSkipTip { get; internal set; }

    /// <summary>The players' input is held back: by a script, by the camera's move, or for a moment after.</summary>
    public bool IsInputLimited => locks.Count > 0 || unlockDelay > 0;

    public void LimitInput(object locker)
    {
        ArgumentNullException.ThrowIfNull(locker);
        locks.Add(locker);
        unlockDelay = UnlockDelay;
    }

    public void UnlimitInput(object locker)
    {
        ArgumentNullException.ThrowIfNull(locker);
        if (locks.Remove(locker))
        {
            unlockDelay = UnlockDelay;
        }
    }

    public void Request(ScriptRequest request) => requests.Enqueue(request ?? throw new ArgumentNullException(nameof(request)));

    /// <summary>The requests made since the last time, in order.</summary>
    public IReadOnlyList<ScriptRequest> TakeRequests()
    {
        var taken = requests.ToArray();
        requests.Clear();
        return taken;
    }

    internal void Update(float dt) => unlockDelay = MathF.Max(0, unlockDelay - dt);
}
