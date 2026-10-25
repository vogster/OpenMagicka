using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenMagicka.Launcher;

/// <summary>
/// The run's log (OM-53): what the game writes to the console also goes to <c>OpenMagicka/logs/openmagicka.log</c>
/// in the local application data (the run before kept as <c>openmagicka.old.log</c>), headed with the version and the
/// system, so a tester can send it; a crash's exception is written there too. A log that cannot be opened is skipped.
/// </summary>
internal sealed class LogFile : IDisposable
{
    private readonly StreamWriter? file;
    private readonly TextWriter output = Console.Out, error = Console.Error;

    private LogFile(StreamWriter? file) => this.file = file;

    public static string Directory => OpenMagicka.Engine.DataFolder.Of("logs");

    public static string FilePath => Path.Combine(Directory, "openmagicka.log");

    /// <summary>The game's version as built (with its commit).</summary>
    public static string Version =>
        typeof(LogFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static LogFile Start(IReadOnlyList<string> args)
    {
        StreamWriter? file = null;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (File.Exists(FilePath))
            {
                File.Move(FilePath, Path.Combine(Directory, "openmagicka.old.log"), overwrite: true);
            }
            file = new StreamWriter(FilePath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: no log file ({e.Message})");
        }
        var log = new LogFile(file);
        if (file is not null)
        {
            var shared = TextWriter.Synchronized(file);
            Console.SetOut(TextWriter.Synchronized(new Tee(log.output, shared)));
            Console.SetError(TextWriter.Synchronized(new Tee(log.error, shared)));
        }
        Console.WriteLine($"OpenMagicka {Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, "
            + $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, arguments: {string.Join(' ', args)}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine($"OpenMagicka: crashed: {e.ExceptionObject}");
        return log;
    }

    public void Dispose()
    {
        Console.SetOut(output);
        Console.SetError(error);
        file?.Dispose();
    }

    /// <summary>Text written to two writers.</summary>
    private sealed class Tee(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}
