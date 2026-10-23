using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Game.Viewer;

/// <summary>
/// Something the viewer can show: a scene (its level's file name, the scene name, its level
/// model asset) or a model (its folder, file name and asset).
/// </summary>
public sealed record ViewerScene(string Level, string Scene, string Model, SceneSettings? Settings = null);

/// <summary>
/// Every scene of every level, in level then file order, followed by every model under Models/
/// (props and characters). Files that fail to read are listed as problems.
/// </summary>
public sealed class SceneCatalog
{
    private const string ModelsDirectory = "Models";
    private static readonly HashSet<string> ModelReaders = new(StringComparer.Ordinal) { "ModelReader", "SkinnedModelReader" };

    public SceneCatalog(IReadOnlyList<ViewerScene> scenes, IReadOnlyList<ViewerScene> models, IReadOnlyList<string> problems)
    {
        Scenes = [.. scenes];
        Models = [.. models];
        Problems = [.. problems];
        Items = [.. Scenes, .. Models];
        SectionStarts = Scenes.Count > 0 && Models.Count > 0 ? [0, Scenes.Count] : [0];
    }

    public IReadOnlyList<ViewerScene> Scenes { get; }

    public IReadOnlyList<ViewerScene> Models { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Scenes, then models.</summary>
    public IReadOnlyList<ViewerScene> Items { get; }

    /// <summary>Where each non-empty section (scenes, models) starts in <see cref="Items"/>.</summary>
    public IReadOnlyList<int> SectionStarts { get; }

    public static SceneCatalog Load(ContentFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var problems = new List<string>();
        var scenes = LoadScenes(files, problems);
        var models = LoadModels(files, problems);
        return new SceneCatalog(scenes, models, problems);
    }

    /// <summary>
    /// The index in <see cref="Items"/> of a scene by name or a model by file name or asset path
    /// (ignoring case; the first match in scene, then path order): 0 for no name, -1 when there is none.
    /// </summary>
    public int Find(string? name)
    {
        if (name is null)
        {
            return 0;
        }
        var asset = ContentFileSystem.Normalize(name);
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Scene.Equals(name, StringComparison.OrdinalIgnoreCase)
                || (i >= Scenes.Count && Items[i].Model.Equals(asset, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<ViewerScene> LoadScenes(ContentFileSystem files, List<string> problems)
    {
        var scenes = new List<ViewerScene>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var levelPath in files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)))
        {
            LevelDefinition level;
            try
            {
                level = LevelReader.Read(new MemoryStream(files.ReadAllBytes(levelPath)), levelPath);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{levelPath}: {e.Message}");
                continue;
            }
            var levelName = Path.GetFileNameWithoutExtension(levelPath);
            foreach (var scenePath in level.Scenes.Where(seen.Add))
            {
                try
                {
                    var scene = SceneReader.Read(new MemoryStream(files.ReadAllBytes(scenePath)), scenePath);
                    scenes.Add(new ViewerScene(levelName, scene.Name, scene.Model, scene.Settings));
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{scenePath}: {e.Message}");
                }
            }
        }
        return scenes;
    }

    /// <summary>The .xnb files under Models/ whose root reader is a model reader (the rest are their textures).</summary>
    private static List<ViewerScene> LoadModels(ContentFileSystem files, List<string> problems)
    {
        var paths = files.ListFiles(ModelsDirectory, recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)).ToArray();
        var kinds = paths.AsParallel().AsOrdered().Select(path =>
        {
            try
            {
                var readers = XnbFile.Read(files.ReadAllBytes(path)).TypeReaders;
                return (path, IsModel: readers.Count > 0 && ModelReaders.Contains(readers[0].ShortName), Problem: (string?)null);
            }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
            {
                // Per file, so that one unreadable file is a problem listed, not the whole catalog lost.
                return (path, IsModel: false, Problem: $"{path}: {e.Message}");
            }
        }).ToArray();
        problems.AddRange(kinds.Select(k => k.Problem).OfType<string>());
        return kinds.Where(k => k.IsModel).Select(k => k.path[..^4]).Select(asset =>
            new ViewerScene(asset[..Math.Max(asset.LastIndexOf('/'), 0)], asset[(asset.LastIndexOf('/') + 1)..], asset)).ToList();
    }
}
