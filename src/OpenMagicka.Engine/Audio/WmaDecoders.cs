using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenMagicka.Content.Audio;

namespace OpenMagicka.Engine.Audio;

/// <summary>Decodes a bank's WMA wave to 16-bit samples (interleaved, as many as the entry's length).</summary>
public interface IWmaDecoder
{
    string Name { get; }

    short[] Decode(WaveBankEntry entry, ReadOnlySpan<byte> wave);
}

/// <summary>The WMA decoders this system has: Windows' own (Media Foundation), then an ffmpeg it can find.</summary>
public static class WmaDecoders
{
    /// <summary>The first decoder that works here; none (the speech stays silent) when there is none.</summary>
    public static IWmaDecoder? Find()
    {
        if (OperatingSystem.IsWindows() && MediaFoundationWma.Open() is { } windows)
        {
            return windows;
        }
        return FfmpegWma.Find();
    }

    /// <summary>Samples cut or padded with silence to the entry's length.</summary>
    internal static short[] Fit(ReadOnlySpan<short> samples, WaveBankEntry entry)
    {
        var fitted = new short[entry.Duration * entry.Format.Channels];
        samples[..Math.Min(samples.Length, fitted.Length)].CopyTo(fitted);
        return fitted;
    }
}

/// <summary>
/// WMA decoded by an ffmpeg program (Linux and macOS have no WMA decoder of their own): the wave goes in as an xWMA
/// file, 16-bit samples come out. Found at <c>OPENMAGICKA_FFMPEG</c>, else on the PATH.
/// </summary>
public sealed class FfmpegWma(string program) : IWmaDecoder
{
    public const string Variable = "OPENMAGICKA_FFMPEG";

    /// <summary>The longest a wave may take (one hung ffmpeg does not hold the transcoding).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public string Name => $"ffmpeg ({program})";

    public static FfmpegWma? Find()
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } named && File.Exists(named))
        {
            return new FfmpegWma(named);
        }
        var file = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => dir.Trim('"')).Where(Path.IsPathFullyQualified)          // not ".": the current folder is no place to run from
            .Select(dir => Path.Combine(dir, file)).Where(File.Exists).Select(path => new FfmpegWma(path)).FirstOrDefault();
    }

    public short[] Decode(WaveBankEntry entry, ReadOnlySpan<byte> wave)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var xwma = XwmaFile.Wrap(entry, wave);
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "xwma", "-i", "pipe:0", "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start");
        var feeding = Task.Run(() =>
        {
            try
            {
                using var input = process.StandardInput.BaseStream;
                input.Write(xwma);
            }
            catch (IOException)
            {
                // ffmpeg stopped reading: its exit code and message say why
            }
        });
        var errors = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        var reading = process.StandardOutput.BaseStream.CopyToAsync(output);
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidDataException($"ffmpeg took over {Timeout.TotalSeconds} s on a wave");
        }
        reading.Wait();
        feeding.Wait();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"ffmpeg could not decode the wave: {errors.Result.Trim()}");
        }
        return WmaDecoders.Fit(MemoryMarshal.Cast<byte, short>(output.GetBuffer().AsSpan(0, (int)output.Length & ~1)), entry);
    }
}
