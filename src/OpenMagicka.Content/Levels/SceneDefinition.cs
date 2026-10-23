using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Levels;

/// <summary>Reverb presets; the values are the original's (a number in the file is accepted).</summary>
public enum RoomType : byte
{
    None = 0,
    Generic = 10,
    SmallRoom = 20,
    StoneRoom = 30,
    Cave = 40,
    Forest = 50,
}

public enum RulesetType
{
    Survival,
    TimedObjective,
    Versus,
}

/// <summary>The icon and label of an interactable point.</summary>
public enum InteractType
{
    None,
    Pick_Up,
    Use,
    Next,
    Push,
    Pull,
    Boost,
    Examine,
    Talk,
    Interact,
    Activate,
    Attack,
    BreakFree,
}

/// <summary>Fog; present only when the scene has a Fog element.</summary>
public sealed record FogSettings(float Start, float End, Vector4 Color);

public sealed record FilterSettings(float Brightness, float Contrast, float Saturation);

public sealed record BloomSettings(float Threshold, float Multiplier, float Blur);

public sealed record RimLightSettings(float Glow, float Power, float Bias);

/// <summary>A sky texture (content path) and its tint.</summary>
public sealed record SkyMapSettings(string Texture, Vector3 Color);

/// <summary>Cloud shadows projected with the directional light; Texture is a content path or null.</summary>
public sealed record CloudSettings(float Intensity, Vector2 Scale, Vector2 Movement, string? Texture, bool WorldRelative);

/// <summary>A scene's look and sound (docs/formats/LEVELS_TRIGGERS.md §4), with the original's defaults.</summary>
public sealed record SceneSettings(
    bool Indoor,
    float? Magnify,
    RoomType RoomType,
    float ReverbMix,
    FogSettings? Fog,
    FilterSettings Filter,
    BloomSettings Bloom,
    RimLightSettings RimLight,
    SkyMapSettings? SkyMap,
    CloudSettings Clouds,
    float KillPlanePosition)
{
    public static SceneSettings Default { get; } = new(
        false, null, RoomType.None, 0, null, new FilterSettings(1, 1, 1), new BloomSettings(0.8f, 1, 2.5f),
        new RimLightSettings(0.8f, 1.5f, 0.2f), null, new CloudSettings(0, Vector2.Zero, Vector2.Zero, null, false), -50);
}

/// <summary>A game mode's rules: its type and its element, which the ruleset interprets.</summary>
public sealed record RulesetDefinition(RulesetType Type, ScriptNode Node);

/// <summary>A plain action, and whether it comes from a looped block (only in replayed campaigns).</summary>
public sealed record ScriptAction(ScriptNode Node, bool Looped);

/// <summary>A <c>&lt;random&gt;</c> group: one alternative of the actions.</summary>
public sealed record RandomGroup(IReadOnlyList<ScriptNode> Actions, bool Looped)
{
    private readonly IReadOnlyList<ScriptNode> actions = ReadOnly.List(Actions);

    public IReadOnlyList<ScriptNode> Actions { get => actions; init => actions = ReadOnly.List(value); }
}

/// <summary>
/// The actions of a trigger or interactable (§5.3): plain actions in document order and the
/// random groups. When it fires, one group is chosen and the plain actions follow it.
/// </summary>
public sealed record ActionBlock(IReadOnlyList<ScriptAction> Regular, IReadOnlyList<RandomGroup> Random)
{
    private readonly IReadOnlyList<ScriptAction> regular = ReadOnly.List(Regular);
    private readonly IReadOnlyList<RandomGroup> random = ReadOnly.List(Random);

    public IReadOnlyList<ScriptAction> Regular { get => regular; init => regular = ReadOnly.List(value); }

    public IReadOnlyList<RandomGroup> Random { get => random; init => random = ReadOnly.List(value); }

    /// <summary>
    /// The variants the original builds when the scene loads: each random group followed by the
    /// plain actions (or the plain actions alone when there is no group). Looped content is
    /// included only in a looped (replayed) campaign.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ScriptNode>> Variants(bool looped)
    {
        var plain = Regular.Where(a => looped || !a.Looped).Select(a => a.Node).ToArray();
        var groups = Random.Where(g => looped || !g.Looped).ToArray();
        return groups.Length == 0
            ? [ReadOnly.List(plain)]
            : groups.Select(g => ReadOnly.List(g.Actions.Concat(plain))).ToArray();
    }
}

/// <summary>
/// A block of a survival wave (§7.1): any child of <c>&lt;Wave&gt;</c>. Area is null for "any" or
/// none (a random spawn area then); Delay is from the wave's start.
/// </summary>
public sealed record WaveBlock(string? Area, float Delay, ActionBlock Actions);

public sealed record WaveDefinition(IReadOnlyList<WaveBlock> Blocks)
{
    private readonly IReadOnlyList<WaveBlock> blocks = ReadOnly.List(Blocks);

    public IReadOnlyList<WaveBlock> Blocks { get => blocks; init => blocks = ReadOnly.List(value); }
}

/// <summary>
/// A trigger (§5.1). Id is lowered, null when absent; Key is its legacy hash (0 without an id).
/// Repeat: -1 once, 0 every frame, N a cooldown in seconds. Conditions are OR-ed blocks of
/// AND-ed condition nodes.
/// </summary>
public sealed record TriggerDefinition(string? Id, int Key, bool AutoRun, float Repeat, IReadOnlyList<IReadOnlyList<ScriptNode>> Conditions, ActionBlock Actions)
{
    private readonly IReadOnlyList<IReadOnlyList<ScriptNode>> conditions = ReadOnly.List(Conditions.Select(ReadOnly.List));

    public IReadOnlyList<IReadOnlyList<ScriptNode>> Conditions { get => conditions; init => conditions = ReadOnly.List(value.Select(ReadOnly.List)); }
}

/// <summary>
/// A point the player can interact with (§5.5); Locator is lowered and Key is its hash.
/// AnimHighlight holds animated level part paths split at '/'.
/// </summary>
public sealed record InteractableDefinition(
    string? Locator,
    int Key,
    InteractType Type,
    bool Enabled,
    IReadOnlyList<IReadOnlyList<string>> AnimHighlight,
    IReadOnlyList<string> PhysHighlight,
    ActionBlock Actions)
{
    private readonly IReadOnlyList<IReadOnlyList<string>> animHighlight = ReadOnly.List(AnimHighlight.Select(ReadOnly.List));
    private readonly IReadOnlyList<string> physHighlight = ReadOnly.List(PhysHighlight);

    public IReadOnlyList<IReadOnlyList<string>> AnimHighlight { get => animHighlight; init => animHighlight = ReadOnly.List(value.Select(ReadOnly.List)); }

    public IReadOnlyList<string> PhysHighlight { get => physHighlight; init => physHighlight = ReadOnly.List(value); }
}

/// <summary>
/// A scene file (§4). Name is the lower-case file name (the scene ID is its hash); Model is the
/// level model's content path. Waves are the survival waves, in order.
/// </summary>
public sealed record SceneDefinition(
    string Name,
    string Model,
    bool ForceNavMesh,
    bool ForceCamera,
    SceneSettings Settings,
    RulesetDefinition? Ruleset,
    IReadOnlyList<WaveDefinition> Waves,
    IReadOnlyList<TriggerDefinition> Triggers,
    IReadOnlyList<InteractableDefinition> Interactables)
{
    private readonly IReadOnlyList<WaveDefinition> waves = ReadOnly.List(Waves);
    private readonly IReadOnlyList<TriggerDefinition> triggers = ReadOnly.List(Triggers);
    private readonly IReadOnlyList<InteractableDefinition> interactables = ReadOnly.List(Interactables);

    public IReadOnlyList<WaveDefinition> Waves { get => waves; init => waves = ReadOnly.List(value); }

    public IReadOnlyList<TriggerDefinition> Triggers { get => triggers; init => triggers = ReadOnly.List(value); }

    public IReadOnlyList<InteractableDefinition> Interactables { get => interactables; init => interactables = ReadOnly.List(value); }
}
