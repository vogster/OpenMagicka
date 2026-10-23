namespace OpenMagicka.Content.Levels;

/// <summary>
/// Where the players start. Scene is lowered (scene IDs are hashes of lower-case names); Area is
/// kept as written, because the original appends the player index and hashes it without lowering.
/// ForceCamera is null when the file does not set it (the default depends on the game type).
/// </summary>
public sealed record LevelStart(string? Scene, string? Area, bool SpawnPlayers, bool NoItems, bool? ForceCamera, bool SpawnFairy)
{
    public static LevelStart Default { get; } = new(null, null, true, false, null, true);

    /// <summary>The spawn point names of players 0..3: Area followed by the index.</summary>
    public IReadOnlyList<string> PlayerAreas => Area is null ? [] : [Area + "0", Area + "1", Area + "2", Area + "3"];
}

/// <summary>An item given to every avatar, attached to a bone.</summary>
public sealed record AvatarItem(string Item, string Bone);

/// <summary>
/// A level file (<c>.lvl</c>, docs/formats/LEVELS_TRIGGERS.md §3). Name and descriptions are
/// localization keys as written; paths are content paths resolved against the level's directory.
/// </summary>
public sealed record LevelDefinition(
    string Name,
    string Description,
    string ShortDescription,
    string Loading,
    LevelStart Start,
    string? Dialogs,
    IReadOnlyList<string> Scenes,
    string PreferredAvatar,
    IReadOnlyList<string> AllowedAvatars,
    IReadOnlyList<AvatarItem> AdditionalItems)
{
    public const string DefaultAvatar = "wizard";

    private readonly IReadOnlyList<string> scenes = ReadOnly.List(Scenes);
    private readonly IReadOnlyList<string> allowedAvatars = ReadOnly.List(AllowedAvatars);
    private readonly IReadOnlyList<AvatarItem> additionalItems = ReadOnly.List(AdditionalItems);

    public IReadOnlyList<string> Scenes { get => scenes; init => scenes = ReadOnly.List(value); }

    public IReadOnlyList<string> AllowedAvatars { get => allowedAvatars; init => allowedAvatars = ReadOnly.List(value); }

    public IReadOnlyList<AvatarItem> AdditionalItems { get => additionalItems; init => additionalItems = ReadOnly.List(value); }
}
