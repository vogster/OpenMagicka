using OpenMagicka.Content;
using OpenMagicka.Content.Levels;

namespace OpenMagicka.Game.Viewer;

/// <summary>A scene the viewer can show: its level (file name), scene name and level model asset.</summary>
public sealed record ViewerScene(string Level, string Scene, string Model);

/// <summary>Every scene of every level, in level then file order; scenes that fail to read are listed as problems.</summary>
public sealed record SceneCatalog(IReadOnlyList<ViewerScene> Scenes, IReadOnlyList<string> Problems)
{
    public static SceneCatalog Load(ContentFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var scenes = new List<ViewerScene>();
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var levelPath in files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)))
        {
            LevelDefinition level;
            try
            {
                level = LevelReader.Read(new MemoryStream(files.ReadAllBytes(levelPath)), levelPath);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
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
                    scenes.Add(new ViewerScene(levelName, scene.Name, scene.Model));
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                    problems.Add($"{scenePath}: {e.Message}");
                }
            }
        }
        return new SceneCatalog(scenes, problems);
    }

    /// <summary>The index of the scene with this name (ignoring case): 0 for no name, -1 when there is none.</summary>
    public int Find(string? scene)
    {
        if (scene is null)
        {
            return 0;
        }
        for (var i = 0; i < Scenes.Count; i++)
        {
            if (Scenes[i].Scene.Equals(scene, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
