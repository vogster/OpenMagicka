using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A level checkpoint as a campaign slot keeps it on disk (OM-85, docs/engine/ARCHITECTURE.md §20): our own JSON, not
/// the original's binary blob, but the same content and the same guard, the hashes of the level's files and of its
/// dialogs (a checkpoint of other files is of no use, and is passed over). What the checkpoint in memory points at is
/// named instead: a logged action by its trigger, variant and place; a prop or an item by its asset; the dialogs' and
/// the sound's parts as their owners write them.
/// </summary>
public sealed record SavedCheckpoint
{
    public const int Format = 1;

    /// <summary>
    /// How it is written: named floating-point values allowed (a NaN countdown does not stop the save); a null where
    /// none may be is no checkpoint (what the serializer cannot see, a null in a list, the resolving turns down).
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
    };

    /// <summary>A part of a checkpoint (the dialogs', the sound's) read back; none for one that does not read, or is not whole.</summary>
    public static T? Read<T>(JsonNode? node, Func<T, bool>? whole = null) where T : class
    {
        try
        {
            return node.Deserialize<T>(Json) is { } read && (whole?.Invoke(read) ?? true) ? read : null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public int Version { get; init; } = Format;

    public string LevelHash { get; init; } = "";

    public string DialogHash { get; init; } = "";

    public int Scene { get; init; }

    public Dictionary<int, int> Counters { get; init; } = [];

    public List<SavedTimer> Timers { get; init; } = [];

    public List<SavedTimer> Played { get; init; } = [];

    public Dictionary<int, SavedScene> Scenes { get; init; } = [];

    public List<float[]> SpawnPoints { get; init; } = [];

    public List<int> Ignored { get; init; } = [];

    public ulong UnlockedMagicks { get; init; }

    public bool DefaultAvatarSection { get; init; }

    public bool UnequipWeaponsSection { get; init; }

    public JsonNode? Dialogs { get; init; }

    public JsonNode? Sounds { get; init; }

    /// <summary>A checkpoint as written, for the level and dialogs of those hashes; a prop or item of no known asset is left out.</summary>
    public static SavedCheckpoint From(LevelCheckpoint checkpoint, string levelHash, string dialogHash, IScriptAssets? assets, ScriptGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new SavedCheckpoint
        {
            LevelHash = levelHash,
            DialogHash = dialogHash,
            Scene = checkpoint.Scene,
            Counters = new Dictionary<int, int>(checkpoint.Level.Counters),
            Timers = [.. checkpoint.Level.Timers.Select(t => new SavedTimer(t.Id, t.Time, t.Paused))],
            Played = [.. checkpoint.Level.Played.Select(t => new SavedTimer(t.Id, t.Time, t.Paused))],
            Scenes = checkpoint.Scenes.ToDictionary(p => p.Key, p => SavedScene.Of(p.Value, assets)),
            SpawnPoints = [.. checkpoint.SpawnPoints.Select(Floats)],
            Ignored = [.. checkpoint.Ignored],
            UnlockedMagicks = checkpoint.UnlockedMagicks,
            DefaultAvatarSection = checkpoint.Sections.DefaultAvatar,
            UnequipWeaponsSection = checkpoint.Sections.UnequipWeapons,
            Dialogs = game?.Dialogs?.Write(checkpoint.Dialogs),
            Sounds = game?.Sounds?.Write(checkpoint.Sounds),
        };
    }

    /// <summary>
    /// The checkpoint in memory again, its actions those of the scenes' scripts (by scene key; a scene whose scripts are
    /// not made yet cannot take its state); null when a trigger or an action named is not there (other files), or a part
    /// is missing or does not read (the dialogs' or the sound's, when the game has them).
    /// </summary>
    public LevelCheckpoint? Resolve(Func<int, ScriptScene?> scripts, IScriptAssets? assets, ScriptGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        try
        {
            return Resolved(scripts, assets, game);
        }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;                                                      // a null in a list the serializer let by
        }
    }

    private LevelCheckpoint? Resolved(Func<int, ScriptScene?> scripts, IScriptAssets? assets, ScriptGame? game)
    {
        var scenes = new Dictionary<int, SceneCheckpoint>();
        foreach (var (key, saved) in Scenes)
        {
            if (saved.Resolve(scripts(key), assets) is not { } scene)
            {
                return null;
            }
            scenes[key] = scene;
        }
        if (SpawnPoints.Any(p => p is not { Length: 16 }))
        {
            return null;
        }
        var dialogs = game?.Dialogs?.Read(Dialogs);
        var sounds = game?.Sounds?.Read(Sounds);
        if ((Dialogs is not null && game?.Dialogs is not null && dialogs is null) || (Sounds is not null && game?.Sounds is not null && sounds is null))
        {
            return null;                                                      // ours: the scripts back without their dialogs would replay the story
        }
        var level = new ScriptLevel.Snapshot(new Dictionary<int, int>(Counters), [.. Timers.Select(t => (t.Id, t.Time, t.Paused))],
            [.. Played.Select(t => (t.Id, t.Time, t.Paused))]);
        return new LevelCheckpoint(Scene, level, scenes, [.. SpawnPoints.Select(Matrix)], [.. Ignored], UnlockedMagicks)
        {
            Dialogs = dialogs,
            Sounds = sounds,
            Sections = (DefaultAvatarSection, UnequipWeaponsSection),
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A checkpoint read back; null for none, or for text that is not one of this format.</summary>
    public static SavedCheckpoint? FromJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            return Read<SavedCheckpoint>(JsonNode.Parse(text), s => s.Version == Format);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The level's hash as the original's: the SHA-256 of its file XOR-ed with that of each scene it lists (a file that
    /// does not read gives none), in hexadecimal.
    /// </summary>
    public static string LevelHashOf(ContentFileSystem files, string levelPath, LevelDefinition level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var hash = Sha(files, levelPath);
        foreach (var scene in level.Scenes)
        {
            var of = Sha(files, scene);
            for (var i = 0; i < hash.Length; i++)
            {
                hash[i] ^= of[i];
            }
        }
        return Convert.ToHexString(hash);
    }

    /// <summary>The SHA-256 of the level's dialog file, as the original's; zeros without one.</summary>
    public static string DialogHashOf(ContentFileSystem files, LevelDefinition level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return Convert.ToHexString(level.Dialogs is { } dialogs ? Sha(files, dialogs) : new byte[SHA256.HashSizeInBytes]);
    }

    private static byte[] Sha(ContentFileSystem files, string path)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            return SHA256.HashData(files.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new byte[SHA256.HashSizeInBytes];
        }
    }

    internal static float[] Floats(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    internal static Matrix4x4 Matrix(float[] f) =>
        new(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
}

public sealed record SavedTimer(int Id, float Time, bool Paused);

/// <summary>A scene as saved: entered or not, its scripts (when entered), its kept props and items.</summary>
public sealed record SavedScene(bool FirstEntry, SavedScripts? Scripts, List<SavedEntity> Entities)
{
    public static SavedScene Of(SceneCheckpoint scene, IScriptAssets? assets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new(scene.FirstEntry, scene.Scripts is { } scripts ? SavedScripts.Of(scripts) : null,
            [.. scene.Entities.Select(e => SavedEntity.Of(e, assets)).OfType<SavedEntity>()]);
    }

    /// <summary>Null when its scripts were saved but the scene has none, or name what the scene lacks.</summary>
    public SceneCheckpoint? Resolve(ScriptScene? scene, IScriptAssets? assets)
    {
        ScriptScene.Snapshot? snapshot = null;
        if (Scripts is { } scripts && (scene is null || (snapshot = scripts.Resolve(scene)) is null))
        {
            return null;
        }
        return new SceneCheckpoint(FirstEntry, snapshot, [.. Entities.Select(e => e.Resolve(assets)).OfType<EntityState>()]);
    }
}

/// <summary>A scene's scripts as saved: each trigger's state by key, the log as [trigger key, variant, place], the parts' states in order.</summary>
public sealed record SavedScripts(Dictionary<int, SavedTrigger> Triggers, List<int[]> Log, List<ScenePart.State> Parts)
{
    public static SavedScripts Of(ScriptScene.Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.Triggers.ToDictionary(p => p.Key, p => SavedTrigger.Of(p.Value)), [.. snapshot.Log.Select(Place)], [.. snapshot.Parts]);
    }

    /// <summary>An action's place: its trigger's key, then the first variant holding it (the plain ones are shared) and its index there.</summary>
    private static int[] Place(ScriptAction action)
    {
        var variants = action.Parent.Variants;
        for (var v = 0; v < variants.Count; v++)
        {
            for (var a = 0; a < variants[v].Count; a++)
            {
                if (ReferenceEquals(variants[v][a], action))
                {
                    return [action.Parent.Key, v, a];
                }
            }
        }
        throw new InvalidOperationException("a logged action is not among its trigger's");
    }

    public ScriptScene.Snapshot? Resolve(ScriptScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var log = new List<ScriptAction>(Log.Count);
        foreach (var place in Log)
        {
            if (place is not [var key, var v, var a] || scene.Find(key) is not { } trigger || v < 0 || v >= trigger.Variants.Count
                || a < 0 || a >= trigger.Variants[v].Count)
            {
                return null;
            }
            log.Add(trigger.Variants[v][a]);
        }
        if (Triggers.Keys.Any(k => scene.Find(k) is null))
        {
            return null;
        }
        return new ScriptScene.Snapshot(Triggers.ToDictionary(p => p.Key, p => p.Value.Resolve()), log, [.. Parts]);
    }
}

public sealed record SavedTrigger(bool HasTriggered, float SinceRepeat, SavedAction[][] Actions, bool? Enabled)
{
    public static SavedTrigger Of(ScriptTrigger.State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.HasTriggered, state.SinceRepeat, [.. state.Actions.Select(v => v.Select(SavedAction.Of).ToArray())], state.Enabled);
    }

    public ScriptTrigger.State Resolve() => new(HasTriggered, SinceRepeat, [.. Actions.Select(v => v.Select(a => a.Resolve()).ToArray())], Enabled);
}

/// <summary>
/// An action's waiting runs, and what it keeps: a spawn's survivors' hit points; an executeTrigger's waiting senders,
/// by count only (from disk they are no one, as the original's).
/// </summary>
public sealed record SavedAction(float Countdown, int Queued, List<float>? HitPoints = null, int? Senders = null)
{
    public static SavedAction Of(ScriptAction.State state) => state.Tag switch
    {
        List<float> hitPoints => new(state.Countdown, state.Queued, new List<float>(hitPoints)),
        IEnumerable<Character?> senders => new(state.Countdown, state.Queued, Senders: senders.Count()),
        _ => new(state.Countdown, state.Queued),
    };

    public ScriptAction.State Resolve()
    {
        object? tag = HitPoints is { } hitPoints ? new List<float>(hitPoints)
            : Senders is { } senders ? Enumerable.Repeat<Character?>(null, Math.Max(0, senders)).ToList()
            : null;
        return new(Countdown, Queued, tag);
    }
}

/// <summary>A kept prop ("prop": its transform's 16 values) or item ("item": its box's centre), named by its asset.</summary>
public sealed record SavedEntity(string Kind, string Asset, float[] At, float HitPoints = 0, int OnDeath = 0, int OnDamage = 0,
    bool Pinned = false, int OnPickup = 0, bool IgnoresTractorPull = false, int UniqueId = 0)
{
    public const string Prop = "prop", Item = "item";

    public static SavedEntity? Of(EntityState state, IScriptAssets? assets) => state switch
    {
        PropState prop when assets?.AssetOf(prop.Template) is { } asset => new(Prop, asset, SavedCheckpoint.Floats(prop.Transform), prop.HitPoints,
            prop.OnDeath, prop.OnDamage, UniqueId: prop.UniqueId),
        ItemState item when assets?.AssetOf(item.Template) is { } asset => new(Item, asset, [item.Centre.X, item.Centre.Y, item.Centre.Z],
            Pinned: item.Pinned, OnPickup: item.OnPickup, IgnoresTractorPull: item.IgnoresTractorPull, UniqueId: item.UniqueId),
        _ => null,
    };

    /// <summary>The state again; null for an asset no longer there.</summary>
    public EntityState? Resolve(IScriptAssets? assets) => Kind switch
    {
        Prop when At.Length == 16 && assets?.Prop(Asset) is { } template =>
            new PropState(template, SavedCheckpoint.Matrix(At), HitPoints, OnDeath, OnDamage, UniqueId),
        Item when At.Length == 3 && assets?.Item(Asset) is { } template =>
            new ItemState(template, new Vector3(At[0], At[1], At[2]), Pinned, OnPickup, IgnoresTractorPull, UniqueId),
        _ => null,
    };
}
