using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Content.Xna;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Scripting;

/// <summary>Where a level at play finds its scenes and their models.</summary>
public interface ILevelContent
{
    /// <summary>A scene file, by its content path.</summary>
    SceneDefinition Scene(string path);

    /// <summary>A scene's level model, by its asset; null when it has none.</summary>
    LevelModelContent? Model(string asset);
}

/// <summary>The game's content as a level's: scene files read as written, models through the loader (kept while it caches them).</summary>
public sealed class ContentLevel(ContentFileSystem files, ContentLoader loader) : ILevelContent
{
    private readonly ContentFileSystem files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly ContentLoader loader = loader ?? throw new ArgumentNullException(nameof(loader));

    public SceneDefinition Scene(string path) => SceneReader.Read(new MemoryStream(files.ReadAllBytes(path)), path);

    public LevelModelContent? Model(string asset) =>
        loader.Load(asset).Root as LevelModelContent ?? throw new InvalidDataException($"{asset} is not a level model");
}

/// <summary>What the scenes of a level at play are given: the characters, the items and props, the kinds of script parts, the game beyond them.</summary>
public sealed record LevelServices(IBestiary? Bestiary = null, IScriptAssets? Assets = null, ScriptRegistry? Registry = null, ScriptGame? Game = null,
    bool Looped = false, int Seed = 0);

/// <summary>
/// Where the players come into a scene (docs/formats/LEVELS_TRIGGERS.md §6.1): the scene (the hash of
/// its lowered name), the locators of players 0..3 (by their hash), and whether the players are put
/// there at all.
/// </summary>
public sealed record SpawnPoint(int Scene, IReadOnlyList<int> Locators, bool SpawnPlayers)
{
    /// <summary>The level's <c>&lt;Start&gt;</c>: its scene, and its area's locators as written (area0..area3), as the original hashes them.</summary>
    public static SpawnPoint Of(LevelStart start, string firstScene)
    {
        ArgumentNullException.ThrowIfNull(start);
        var scene = LegacyStringHash.Lower(start.Scene ?? firstScene);
        return new SpawnPoint(scene, [.. start.PlayerAreas.Select(LegacyStringHash.Compute)], start.SpawnPlayers);
    }
}
