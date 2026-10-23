using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Particles;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Tools;

/// <summary>
/// <c>omx check</c>: reads every XNB file, every level with its scenes and dialogs, every visual
/// effect and string table, then the sets the game builds from them (the effect library, each
/// level's dialogs, each language), and prints one line per kind plus every failure.
/// </summary>
internal static class ContentCheck
{
    public static int Run(string contentDirectory, TextWriter output)
    {
        var files = new ContentFileSystem(contentDirectory);
        var all = files.ListFiles("", recursive: true);
        var levelPaths = files.ListFiles("Levels", recursive: true).Where(p => Is(p, ".lvl")).ToArray();
        var effectPaths = files.ListFiles("Effects", recursive: true).Where(p => Is(p, ".xml")).ToArray();
        var languages = LanguagePack.Available(files);
        if (!all.Any(p => Is(p, ".xnb")) && levelPaths.Length == 0 && effectPaths.Length == 0 && languages.Count == 0)
        {
            output.WriteLine($"no Magicka content found under {contentDirectory}");
            return Omx.Usage;
        }

        var failures = new List<string>();
        Stream Open(string path) => new MemoryStream(files.ReadAllBytes(path));

        Report(output, "xnb", all.Where(p => Is(p, ".xnb")), failures,
            path => XnbContent.Load(XnbFile.Read(files.ReadAllBytes(path)), XnbReaderRegistry.Magicka));
        var levels = new List<LevelDefinition>();
        Report(output, "levels", levelPaths, failures, path => levels.Add(LevelReader.Read(Open(path), path)));
        Report(output, "scenes", levels.SelectMany(l => l.Scenes).Distinct(StringComparer.OrdinalIgnoreCase), failures,
            path => SceneReader.Read(Open(path), path));
        var dialogFiles = levels.Select(l => l.Dialogs).OfType<string>()
            .Append(files.Exists(DialogCollection.FairyDialogs) ? DialogCollection.FairyDialogs : null).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);
        Report(output, "dialog files", dialogFiles, failures, path => DialogReader.Read(Open(path)));
        Report(output, "effects", effectPaths, failures, path => VisualEffectReader.Read(Open(path)));
        Report(output, "string tables", files.ListFiles("Languages", recursive: true).Where(p => Is(p, ".loctable.xml")), failures,
            path => LocTableReader.Read(Open(path)));

        // The sets built from those files, where names and ids must not clash.
        Report(output, "level dialogs", levels.Where(l => l.Dialogs is not null).Select(l => l.Dialogs!).Distinct(StringComparer.OrdinalIgnoreCase),
            failures, path => DialogCollection.Load(files, path));
        Report(output, "effect library", effectPaths.Length > 0 ? ["Effects"] : [], failures, _ => EffectLibrary.Load(files));
        Report(output, "languages", languages, failures, language => LanguagePack.Load(files, language));

        foreach (var failure in failures)
        {
            output.WriteLine("FAILED " + failure);
        }
        output.WriteLine(failures.Count == 0 ? "all content read" : $"{failures.Count} failed");
        return failures.Count == 0 ? Omx.Success : Omx.Failed;
    }

    /// <summary>Reads each item; every failure is recorded and the check goes on.</summary>
    private static void Report(TextWriter output, string kind, IEnumerable<string> items, List<string> failures, Action<string> read)
    {
        var count = 0;
        var failed = 0;
        foreach (var item in items)
        {
            count++;
            try
            {
                read(item);
            }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
            {
                failed++;
                var expected = e is InvalidDataException or IOException or XnbUnsupportedReaderException;
                var message = expected ? e.Message : $"{e.GetType().Name}: {e.Message}";
                failures.Add(message.StartsWith(item, StringComparison.OrdinalIgnoreCase) ? message : $"{item}: {message}");
            }
        }
        output.WriteLine($"{kind}: {count - failed} of {count} read");
    }

    private static bool Is(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
