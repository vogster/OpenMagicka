using System.Numerics;

namespace OpenMagicka.Engine.Scripting;

/// <summary>How a level light goes off or on.</summary>
public enum LightTransitionType { None, Linear, Flicker }

/// <summary>A light's switch: off or on, how, and over how long.</summary>
public sealed record LightSwitch(bool On, LightTransitionType Effect, float Time);

/// <summary>
/// The level model's lights a scene's scripts switched (by the hash of the light's lowered name); the
/// others shine as the model has them. The renderer fades them (OM-75).
/// </summary>
public sealed class SceneLights
{
    private readonly Dictionary<int, LightSwitch> switched = [];

    public IReadOnlyDictionary<int, LightSwitch> Switched => switched;

    /// <summary>Whether the light shines (one never switched does).</summary>
    public bool IsOn(int key) => switched.GetValueOrDefault(key)?.On ?? true;

    public void Switch(int key, LightSwitch to) => switched[key] = to ?? throw new ArgumentNullException(nameof(to));

    /// <summary>All as the model has them (a checkpoint's load, before the replay switches them again).</summary>
    public void Reset() => switched.Clear();
}

/// <summary>
/// <c>disableLight id= effect= time=</c> and <c>enableLight</c>: the lights named (a comma list) go off or
/// on, at once, fading or flickering over the time. A replay switches them at once. A light the model
/// lacks crashes the original; here it is only noted.
/// </summary>
public abstract class LightAction(bool on) : ScriptAction
{
    private int[] lights = [];

    public string Id { get; set; } = "";
    public LightTransitionType Effect { get; set; }
    public float Time { get; set; }

    public override void Initialize() => lights = [.. Id.Split(',', StringSplitOptions.TrimEntries).Select(Hash)];

    public override void Replay() => Switch(new LightSwitch(on, LightTransitionType.None, 0));

    protected override void Execute() => Switch(new LightSwitch(on, Effect, Time));

    private void Switch(LightSwitch to)
    {
        foreach (var light in lights)
        {
            Scene.Lights.Switch(light, to);
        }
    }
}

public sealed class DisableLightAction() : LightAction(on: false);

public sealed class EnableLightAction() : LightAction(on: true);

/// <summary>The screen flashes white for a time.</summary>
public sealed record FlashRequest(float Duration) : ScriptRequest;

/// <summary>The music stops.</summary>
public sealed record StopMusicRequest : ScriptRequest;

/// <summary>The music's focus follows the players' distance to a point (within the radius; past it, the value it clears to).</summary>
public sealed record MusicFocusRequest(Vector3 At, float Radius, float ClearTo) : ScriptRequest;

/// <summary>A named scene sound plays at a new volume.</summary>
public sealed record SetVolumeRequest(int Id, float Volume) : ScriptRequest;

/// <summary>
/// A title over the level (by loc keys, or as written with <see cref="Raw"/>), upper case, for the time
/// with its fades, aligned left, centre or right (L, C, R).
/// </summary>
public sealed record TitleRequest(string Title, string SubTitle, bool Raw, float DisplayTime, float FadeIn, float FadeOut, char Alignment, bool Boss)
    : ScriptRequest;

/// <summary>A number floats up from a point for its life.</summary>
public sealed record ScoreRequest(string Value, Vector3 At, float Life) : ScriptRequest;

/// <summary>The credits roll from a file.</summary>
public sealed record CreditsRequest(string File) : ScriptRequest;

/// <summary>How a level ends.</summary>
public enum EndGameCondition : byte
{
    None, Victory, LevelComplete, Defeat, VersusPlayer, VersusTeam, EightyeightMilesPerHour, ChallengeExit, EndOffGame, Disconnected, ToBeContinued,
}

/// <summary>The level ends (OM-50 takes it on): its outcome, whether the world freezes, and a phony end that only shows.</summary>
public sealed record EndGameRequest(EndGameCondition Outcome, bool FreezeGame, bool Phony) : ScriptRequest;

/// <summary><c>flash duration=</c>: the screen flashes white.</summary>
public sealed class FlashAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public float Duration { get; set; } = 0.5f;

    protected override void Execute() => Scene.Stage.Request(new FlashRequest(Duration));
}

/// <summary><c>stopMusic</c>: the music stops.</summary>
public sealed class StopMusicAction : ScriptAction
{
    protected override void Execute() => Scene.Stage.Request(new StopMusicRequest());
}

/// <summary><c>setMusicFocus id= radius= clearTo=</c>: the music's focus follows the distance to the locator (none: the origin).</summary>
public sealed class SetMusicFocusAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Id { get; set; } = "";
    public float Radius { get; set; }
    public float ClearTo { get; set; } = 1;

    protected override void Execute() =>
        Scene.Stage.Request(new MusicFocusRequest(Scene.Places.LocatorPosition(Hash(Id)) ?? Vector3.Zero, Radius, ClearTo));
}

/// <summary><c>setVolume id= volume=</c>: the named scene sound plays at the volume.</summary>
public sealed class SetVolumeAction : ScriptAction
{
    public string Id { get; set; } = "";
    public float Volume { get; set; }

    protected override void Execute() => Scene.Stage.Request(new SetVolumeRequest(Hash(Id), Volume));
}

/// <summary>
/// <c>displayTitleInLevel title= subTitle= displayTime= fadeIn= fadeOut= noTranslation= textAlignment=</c>: a title
/// over the level, for 2 s when no time is given. With no title nor subtitle it shows none (raw, both
/// are needed, as the original).
/// </summary>
public sealed class DisplayTitleInLevelAction : ScriptAction
{
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public float DisplayTime { get; set; } = -1;
    public float FadeIn { get; set; } = 1;
    public float FadeOut { get; set; } = 1;
    public bool NoTranslation { get; set; }
    public string TextAlignment { get; set; } = "";

    protected override void Execute()
    {
        if (NoTranslation ? Title.Length > 0 && SubTitle.Length > 0 : Title.Length > 0 || SubTitle.Length > 0)
        {
            var alignment = TextAlignment is ['l' or 'r'] ? char.ToUpperInvariant(TextAlignment[0]) : 'C';
            Scene.Stage.Request(new TitleRequest(Title, SubTitle, NoTranslation, DisplayTime < 0 ? 2 : DisplayTime, FadeIn, FadeOut, alignment, Boss: false));
        }
    }
}

/// <summary><c>bossTitle title= subTitle= displayTime= fadeIn= fadeOut=</c>: the boss's title (by loc keys), for 2 s when no time is given.</summary>
public sealed class BossTitleAction : ScriptAction
{
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public float DisplayTime { get; set; } = -1;
    public float FadeIn { get; set; } = 1;
    public float FadeOut { get; set; } = 1;

    protected override void Execute()
    {
        if (Title.Length > 0 || SubTitle.Length > 0)
        {
            Scene.Stage.Request(new TitleRequest(Title, SubTitle, Raw: false, DisplayTime < 0 ? 2 : DisplayTime, FadeIn, FadeOut, 'C', Boss: true));
        }
    }
}

/// <summary>
/// <c>scrollScore value= area= offset= ttl=</c>: a number floats up from a random point of the area (else
/// the locator, else the origin) plus the offset ("x,y,z").
/// </summary>
public sealed class ScrollScoreAction : ScriptAction
{
    private Vector3 offset;

    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string Value { get; set; } = "";
    public float Ttl { get; set; }
    public string Area { get; set; } = "";
    public string Offset { get; set; } = "";

    public override void Initialize()
    {
        var parts = Offset.Split(',');
        offset = parts.Length == 3 && parts.All(p => float.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out _))
            ? new Vector3([.. parts.Select(p => float.Parse(p, System.Globalization.CultureInfo.InvariantCulture))])
            : Vector3.Zero;
    }

    protected override void Execute()
    {
        var key = Hash(Area);
        var at = key != ScriptPlaces.Any && Scene.Places.Area(key) is { } area ? area.RandomPoint(Scene.Random) : Scene.Places.LocatorPosition(key) ?? Vector3.Zero;
        Scene.Stage.Request(new ScoreRequest(Value, at + offset, Ttl));
    }
}

/// <summary><c>rollCredits file=</c>: the credits roll.</summary>
public sealed class RollCreditsAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string File { get; set; } = "";

    protected override void Execute() => Scene.Stage.Request(new CreditsRequest(File));
}

/// <summary><c>endGame type= freezeGame= phony=</c>: the level ends with the outcome.</summary>
public sealed class EndGameAction : ScriptAction
{
    public EndGameCondition Type { get; set; }
    public bool FreezeGame { get; set; } = true;
    public bool Phony { get; set; }

    protected override void Execute() => Scene.Stage.Request(new EndGameRequest(Type, FreezeGame, Phony));
}

/// <summary><c>killDialogs</c>: every dialog ends.</summary>
public sealed class KillDialogsAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Stage.Request(new EndDialogsRequest());
}

/// <summary><c>disableUI</c> and <c>enableUI</c>: the HUD hides or shows.</summary>
public abstract class UiAction(bool enabled) : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Stage.IsUiEnabled = enabled;
}

public sealed class DisableUiAction() : UiAction(enabled: false);

public sealed class EnableUiAction() : UiAction(enabled: true);
