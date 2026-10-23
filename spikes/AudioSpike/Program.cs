using System.Text;
using Microsoft.Xna.Framework.Audio;

// Risk check OM-18. Usage: AudioSpike <Magicka/Content/Audio> [seconds per cue]
// Loads magicka.xgs, every wave bank and every sound bank, then plays each cue briefly.
if (args.Length is < 1 or > 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: AudioSpike <Content/Audio dir> [seconds per cue]");
    return 2;
}
var dir = args[0];
var listen = args.Length == 2 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0.05;
// AUDIOSPIKE_TRACE=1 prints every cue before it plays, to find native crashes.
var trace = Environment.GetEnvironmentVariable("AUDIOSPIKE_TRACE") == "1";
// AUDIOSPIKE_BANKS=a.xsb,b.xsb limits the run to those sound banks.
var onlyBanks = Environment.GetEnvironmentVariable("AUDIOSPIKE_BANKS")?.Split(',', StringSplitOptions.RemoveEmptyEntries);

using var engine = new AudioEngine(Path.Combine(dir, "magicka.xgs"));
Console.WriteLine("engine: magicka.xgs loaded");

var waveBanks = new List<WaveBank>();
foreach (var path in Directory.GetFiles(dir, "*.xwb").Order())
{
    var streaming = XactFiles.IsStreamingWaveBank(path);
    try
    {
        waveBanks.Add(streaming ? new WaveBank(engine, path, 0, 16) : new WaveBank(engine, path));
        Console.WriteLine($"wavebank: {Path.GetFileName(path)} ({(streaming ? "streaming" : "in-memory")}) ok");
    }
    catch (Exception e)
    {
        Console.WriteLine($"wavebank: {Path.GetFileName(path)} FAIL {e.GetType().Name}: {e.Message}");
    }
}
for (var i = 0; i < 50 && waveBanks.Any(w => !w.IsPrepared); i++)
{
    engine.Update();
    Thread.Sleep(20);
}

var failures = 0;
foreach (var path in Directory.GetFiles(dir, "*.xsb").Order())
{
    if (onlyBanks is not null && !onlyBanks.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
    {
        continue;
    }
    SoundBank bank;
    try
    {
        bank = new SoundBank(engine, path);
    }
    catch (Exception e)
    {
        Console.WriteLine($"soundbank: {Path.GetFileName(path)} FAIL {e.GetType().Name}: {e.Message}");
        failures++;
        continue;
    }

    var (cues, needs) = XactFiles.ReadSoundBank(path);
    int played = 0, silent = 0, errors = 0;
    foreach (var name in cues)
    {
        if (trace)
        {
            Console.Error.WriteLine($"  play {Path.GetFileName(path)}:{name}");
        }
        try
        {
            using var cue = bank.GetCue(name);
            cue.Play();
            var deadline = DateTime.UtcNow.AddSeconds(listen);
            var wasPlaying = false;
            do
            {
                engine.Update();
                wasPlaying |= cue.IsPlaying;
                Thread.Sleep(5);
            } while (DateTime.UtcNow < deadline);
            cue.Stop(AudioStopOptions.Immediate);
            engine.Update();
            if (wasPlaying)
            {
                played++;
            }
            else
            {
                silent++;
            }
        }
        catch (Exception e)
        {
            errors++;
            if (errors <= 3)
            {
                Console.WriteLine($"  cue {name}: {e.GetType().Name}: {e.Message}");
            }
        }
    }
    failures += errors;
    Console.WriteLine($"soundbank: {Path.GetFileName(path)} cues={cues.Count} played={played} not-playing={silent} errors={errors} wavebanks=[{string.Join(", ", needs)}]");
    bank.Dispose();
}

foreach (var w in waveBanks)
{
    w.Dispose();
}
return failures == 0 ? 0 : 1;

internal static class XactFiles
{
    // Wave bank header: "WBND", u32 version, u32 header version, 5 x (u32 offset, u32 length);
    // segment 0 is the bank data that starts with u32 flags (bit 0: streaming).
    public static bool IsStreamingWaveBank(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 12;
        var bankData = reader.ReadUInt32();
        reader.BaseStream.Position = bankData;
        return (reader.ReadUInt32() & 1) != 0;
    }

    // Sound bank header (content version 45): cue counts at 19/21, wave bank count at 27,
    // cue name block offset at 42 and wave bank name table (64-byte entries) offset at 58.
    public static (List<string> Cues, List<string> WaveBanks) ReadSoundBank(string path)
    {
        var data = File.ReadAllBytes(path);
        var cueCount = BitConverter.ToUInt16(data, 19) + BitConverter.ToUInt16(data, 21);
        var waveBankCount = data[27];
        var names = BitConverter.ToInt32(data, 42);
        var waveBankNames = BitConverter.ToInt32(data, 58);

        var cues = new List<string>(cueCount);
        var at = names;
        for (var i = 0; i < cueCount && names > 0; i++)
        {
            var end = Array.IndexOf(data, (byte)0, at);
            cues.Add(Encoding.ASCII.GetString(data, at, end - at));
            at = end + 1;
        }

        var banks = new List<string>(waveBankCount);
        for (var i = 0; i < waveBankCount; i++)
        {
            var start = waveBankNames + 64 * i;
            var end = Array.IndexOf(data, (byte)0, start);
            banks.Add(Encoding.ASCII.GetString(data, start, end - start));
        }
        return (cues, banks);
    }
}
