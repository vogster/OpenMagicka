using System.Numerics;
using System.Xml;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Content.Levels;

/// <summary>A key of a map cutscene's camera: its time (seconds) and value.</summary>
public readonly record struct CameraKey<T>(float Time, T Value);

/// <summary>
/// The world map's cutscene before a chapter (OM-84, docs/engine/ARCHITECTURE.md §20): the camera over the map
/// (its zoom, page pixels per map texel, and the map texel at the page's centre), the narration's cue and subtitles.
/// It lasts till its last key; the narration, longer, holds the map there.
/// </summary>
public sealed record MapCutscene(IReadOnlyList<CameraKey<float>> Zoom, IReadOnlyList<CameraKey<Vector2>> Position, string? Subtitles,
    SoundBanks Bank, string? Cue)
{
    public float Duration => Math.Max(Zoom.Count > 0 ? Zoom[^1].Time : 0, Position.Count > 0 ? Position[^1].Time : 0);

    /// <summary>
    /// The camera at a time, as the original's: each track eases (smoothstep) from key to key, holding its first key
    /// before it and its last after.
    /// </summary>
    public (Vector2 Position, float Zoom) Camera(float time) =>
        (Track(Position, time, (a, b, t) => new Vector2(SmoothStep(a.X, b.X, t), SmoothStep(a.Y, b.Y, t)), Vector2.Zero),
         Track(Zoom, time, SmoothStep, 1f));

    private static T Track<T>(IReadOnlyList<CameraKey<T>> keys, float time, Func<T, T, float, T> ease, T none)
    {
        if (keys.Count == 0)
        {
            return none;
        }
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Time > time)
            {
                if (i == 0)
                {
                    return keys[0].Value;
                }
                var span = keys[i].Time - keys[i - 1].Time;
                return ease(keys[i - 1].Value, keys[i].Value, span > 0 ? (time - keys[i - 1].Time) / span : 1);
            }
        }
        return keys[^1].Value;
    }

    /// <summary>XNA's smoothstep: the amount clamped to 0..1, then eased in and out.</summary>
    public static float SmoothStep(float from, float to, float amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return from + (to - from) * (t * t * (3 - 2 * t));
    }
}

/// <summary>
/// Reads <c>Levels/Cutscenes.xml</c>: each <c>Cutscene</c> for the chapters its <c>id</c> lists (0-based, commas between),
/// its <c>camera</c>'s <c>zoom</c> and <c>position</c> keys (sorted by time; no time is 0) and its <c>Dialog</c>
/// (the subtitles' key, and the cue, <c>bank/cue</c> or the wave bank's alone). Names are compared ignoring case.
/// </summary>
public static class MapCutsceneReader
{
    public const string Path = "Levels/Cutscenes.xml";

    public static IReadOnlyDictionary<int, MapCutscene> Read(Stream stream)
    {
        var document = LegacyXml.Load(stream, "cutscene file");
        var root = LegacyXml.Children(document, "Cutscenes").FirstOrDefault() ?? throw new InvalidDataException("no Cutscenes element in the cutscene file");
        var cutscenes = new Dictionary<int, MapCutscene>();
        foreach (var node in LegacyXml.Children(root, "Cutscene"))
        {
            var cutscene = ReadCutscene(node);
            foreach (var id in (LegacyXml.Attributes(node, "id").FirstOrDefault() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                cutscenes[LegacyXml.Int(id)] = cutscene;
            }
        }
        return cutscenes;
    }

    private static MapCutscene ReadCutscene(XmlNode node)
    {
        var zoom = new List<CameraKey<float>>();
        var position = new List<CameraKey<Vector2>>();
        foreach (var camera in LegacyXml.Children(node, "camera"))
        {
            foreach (var key in camera.ChildNodes.OfType<XmlElement>())
            {
                var time = LegacyXml.Attributes(key, "time").FirstOrDefault() is { } at ? LegacyXml.Float(at) : 0;
                if (LegacyXml.Is(key, "zoom"))
                {
                    zoom.Add(new CameraKey<float>(time, LegacyXml.Float(key.InnerText)));
                }
                else if (LegacyXml.Is(key, "position"))
                {
                    var xy = key.InnerText.Split(',');
                    position.Add(xy.Length == 2 ? new CameraKey<Vector2>(time, new Vector2(LegacyXml.Float(xy[0]), LegacyXml.Float(xy[1])))
                        : throw new InvalidDataException($"a cutscene position \"{key.InnerText}\" is not x,y"));
                }
            }
        }
        string? subtitles = null, cue = null;
        var bank = SoundBanks.WaveBank;
        if (LegacyXml.Children(node, "Dialog").FirstOrDefault() is { } dialog)
        {
            subtitles = LegacyXml.Attributes(dialog, "subtitles").FirstOrDefault()?.ToLowerInvariant();
            var parts = dialog.InnerText.Trim().ToLowerInvariant().Split('/');
            (bank, cue) = parts.Length switch
            {
                1 => (SoundBanks.WaveBank, parts[0]),
                2 => (LegacyXml.Enum<SoundBanks>(parts[0]), parts[1]),
                _ => throw new InvalidDataException($"a cutscene cue \"{dialog.InnerText}\" has more than one bank"),
            };
            cue = cue.Length > 0 ? cue : null;
        }
        return new MapCutscene([.. zoom.OrderBy(k => k.Time)], [.. position.OrderBy(k => k.Time)], subtitles, bank, cue);
    }
}
