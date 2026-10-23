using System.Xml;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Particles;
using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Tools;

/// <summary>
/// omx, the content tool: <c>dump</c> prints a file as JSON, <c>info</c> an XNB header,
/// <c>check</c> reads all of a game's content and lists what fails. Exit codes: 0 success,
/// 1 a file failed, 2 bad usage.
/// </summary>
public static class Omx
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    private const string UsageText = """
        usage: omx <command> ...
          dump [--full] <file>   print an .xnb, .lvl or content .xml file as JSON (--full: every list item)
          info <file.xnb>        print the header of an XNB file
          check <dir>            read all content of a Magicka installation (or its Content folder)
        """;

    private const int DefaultMaxItems = 256;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Count is < 2 or > 3 || args.Skip(1).Any(string.IsNullOrWhiteSpace))
        {
            return PrintUsage(error);
        }
        try
        {
            return args switch
            {
                ["dump", var file] => Dump(file, DefaultMaxItems, output),
                ["dump", var file, "--full"] => Dump(file, 0, output),
                ["dump", "--full", var file] => Dump(file, 0, output),
                ["info", var file] => Info(file, output),
                ["check", var directory] => Check(directory, output, error),
                _ => PrintUsage(error),
            };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or XnbUnsupportedReaderException)
        {
            error.WriteLine($"omx: {e.Message}");
            return Failed;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            error.WriteLine($"omx: internal error: {e.GetType().Name}: {e.Message}");
            return Failed;
        }
    }

    private static int PrintUsage(TextWriter error)
    {
        error.Write(UsageText);
        return Usage;
    }

    private static int Dump(string file, int maxItems, TextWriter output)
    {
        var value = Read(Path.GetFullPath(file));
        output.WriteLine(ContentJson.Serialize(value, maxItems));
        return Success;
    }

    private static int Info(string file, TextWriter output)
    {
        var xnb = ReadXnb(Path.GetFullPath(file));
        output.WriteLine($"platform: {xnb.Platform}");
        output.WriteLine($"version: {xnb.FormatVersion}");
        output.WriteLine($"compressed: {(xnb.IsCompressed ? "yes" : "no")}");
        output.WriteLine($"objects: {xnb.Objects.Length} bytes, {xnb.SharedResourceCount} shared resources");
        output.WriteLine("type readers:");
        foreach (var reader in xnb.TypeReaders)
        {
            output.WriteLine($"  {reader.Name} (version {reader.Version})");
        }
        return Success;
    }

    private static int Check(string directory, TextWriter output, TextWriter error)
    {
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
        {
            error.WriteLine($"omx: {full}: no such directory");
            return Usage;
        }
        var content = Path.Combine(full, "Content");
        return ContentCheck.Run(Directory.Exists(content) ? content : full, output);
    }

    /// <summary>A file as the reader for its kind sees it; XML is told apart by its root element.</summary>
    private static object? Read(string path)
    {
        if (path.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
        {
            return XnbContent.Load(ReadXnb(path), XnbReaderRegistry.Magicka);
        }
        var bytes = ReadFile(path);
        var contentPath = ContentPath(path);
        try
        {
            if (path.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            {
                return LevelReader.Read(new MemoryStream(bytes), contentPath);
            }
            return RootElement(bytes) switch
            {
                "scene" => SceneReader.Read(new MemoryStream(bytes), contentPath),
                "dialogs" => DialogReader.Read(new MemoryStream(bytes)),
                "effect" => VisualEffectReader.Read(new MemoryStream(bytes)),
                "workbook" => LocTableReader.Read(new MemoryStream(bytes)),
                "level" => LevelReader.Read(new MemoryStream(bytes), contentPath),
                null => throw new InvalidDataException("not an .xnb, .lvl or XML content file"),
                var other => throw new InvalidDataException($"no reader for <{other}> files"),
            };
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }

    private static XnbFile ReadXnb(string path)
    {
        try
        {
            return XnbFile.Read(ReadFile(path));
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }

    private static byte[] ReadFile(string path) =>
        Directory.Exists(path) ? throw new IOException($"{path}: is a directory")
        : File.Exists(path) ? File.ReadAllBytes(path)
        : throw new FileNotFoundException($"{path}: file not found", path);

    /// <summary>The lower-case name of the root element, or null when the bytes are not XML.</summary>
    private static string? RootElement(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CheckCharacters = false };
        using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
        try
        {
            return reader.MoveToContent() == XmlNodeType.Element ? reader.Name.ToLowerInvariant() : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>The path below the nearest folder named Content, so that relative references resolve; else the file name.</summary>
    private static string ContentPath(string path)
    {
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Path.GetFileName(directory).Equals("Content", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
            }
        }
        return Path.GetFileName(path);
    }
}
